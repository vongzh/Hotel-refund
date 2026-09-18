using System.Text.Json;
using Microsoft.Extensions.Logging;
using Stayota.RefundAgent.Application.Contracts;
using Stayota.RefundAgent.Domain;
using Stayota.RefundAgent.Domain.Entities;

namespace Stayota.RefundAgent.Application.Services;

public interface IRefundDataStore
{
    Task EnsureSeededAsync(CancellationToken ct = default);
    Task ResetDemoAsync(CancellationToken ct = default);
    ScenarioFixture GetScenario(string code);
    IReadOnlyList<ScenarioFixture> ListScenarios();
    Task<HotelOrder?> GetOrderAsync(string orderId, CancellationToken ct = default);
    Task<PolicySnapshot?> GetPolicyAsync(string code, CancellationToken ct = default);
    Task<RefundCase> UpsertCaseAsync(RefundCase refundCase, CancellationToken ct = default);
    Task AppendEventAsync(string caseId, string eventType, object payload, CancellationToken ct = default);
    Task SaveWorkflowRunAsync(WorkflowRun run, CancellationToken ct = default);
    Task SaveToolAuditAsync(ToolAuditLog log, CancellationToken ct = default);
}

public sealed class ScenarioCatalog(IRefundDataStore store) : IScenarioCatalog
{
    public IReadOnlyList<ScenarioDto> List() =>
        store.ListScenarios()
            .Select(s => new ScenarioDto(s.Code, s.Name, s.Group, s.Goal, s.EntryMessage, s.RiskLevel))
            .ToList();

    public ScenarioFixture GetRequired(string code) => store.GetScenario(code);
}

public sealed class ToolGateway(
    IRefundDataStore store,
    IConfirmationStore confirmationStore,
    IIdempotencyStore idempotencyStore,
    ILogger<ToolGateway> logger) : IToolGateway
{
    private static readonly HashSet<string> ReadTools =
    [
        "get_order_detail", "get_policy_snapshot", "get_refund_status", "get_payment_events", "search_alternative_hotels"
    ];

    private static readonly HashSet<string> WriteTools =
    [
        "submit_cancellation", "create_supplier_case", "create_payment_investigation", "create_human_handoff", "calculate_refund_quote"
    ];

    public async Task<ToolResult> InvokeAsync(ToolCall call, CancellationToken ct = default)
    {
        var isWrite = WriteTools.Contains(call.ToolName);
        var isRead = ReadTools.Contains(call.ToolName);
        if (!isWrite && !isRead)
        {
            return await AuditAsync(call, false, false, null, $"unknown tool: {call.ToolName}", ct);
        }

        if (string.IsNullOrWhiteSpace(call.UserId))
        {
            return await AuditAsync(call, false, false, null, "missing user identity", ct);
        }

        if (isWrite)
        {
            if (call.RiskLevel == RiskLevel.L3 && call.ToolName is "submit_cancellation")
            {
                return await AuditAsync(call, false, false, null, "L3 risk blocks auto write; escalate to human", ct);
            }

            if (call.ToolName is "submit_cancellation")
            {
                if (string.IsNullOrWhiteSpace(call.ConfirmationToken) ||
                    call.ExpectedOrderVersion is null ||
                    string.IsNullOrWhiteSpace(call.OrderId) ||
                    string.IsNullOrWhiteSpace(call.CaseId))
                {
                    return await AuditAsync(call, false, false, null, "write requires confirmation token, order version, case and order", ct);
                }

                var ok = await confirmationStore.ConsumeAsync(
                    call.ConfirmationToken!, call.CaseId!, call.OrderId!, call.ExpectedOrderVersion.Value, call.ToolName, ct);
                if (!ok)
                {
                    return await AuditAsync(call, false, false, null, "invalid or expired confirmation token", ct);
                }
            }

            if (!string.IsNullOrWhiteSpace(call.IdempotencyKey))
            {
                var began = await idempotencyStore.TryBeginAsync(call.IdempotencyKey!, TimeSpan.FromHours(24), ct);
                if (!began)
                {
                    return await AuditAsync(call, true, true, new { duplicate = true }, null, ct);
                }
            }
        }

        object? data = call.ToolName switch
        {
            "get_order_detail" => await store.GetOrderAsync(call.OrderId ?? "", ct),
            "get_policy_snapshot" => await store.GetPolicyAsync(ArgString(call.Arguments, "policyCode"), ct),
            "calculate_refund_quote" => new
            {
                refund = ArgObject(call.Arguments, "refund"),
                fee = ArgObject(call.Arguments, "fee")
            },
            "get_refund_status" => new { status = "channel_processing", eta = DateTimeOffset.UtcNow.AddDays(2) },
            "get_payment_events" => new { events = new[] { "auth_hold", "capture" } },
            "search_alternative_hotels" => new { hotels = new[] { new { name = "同区域可住演示酒店", price = 520 } } },
            "submit_cancellation" => new { accepted = true, refundStatus = "submitted" },
            "create_supplier_case" => new { externalCaseId = $"SUP-{Guid.NewGuid():N}"[..12].ToUpperInvariant() },
            "create_payment_investigation" => new { ticketId = $"PAY-{Guid.NewGuid():N}"[..12].ToUpperInvariant() },
            "create_human_handoff" => new { queue = call.RiskLevel == RiskLevel.L3 ? "urgent" : "specialist", slaMinutes = 5 },
            _ => null
        };

        logger.LogInformation("Tool {Tool} allowed={Allowed} trace={Trace}", call.ToolName, true, call.TraceId);
        return await AuditAsync(call, true, true, data, null, ct);
    }

    private async Task<ToolResult> AuditAsync(
        ToolCall call, bool allowed, bool success, object? data, string? denyReason, CancellationToken ct)
    {
        await store.SaveToolAuditAsync(new ToolAuditLog
        {
            TraceId = call.TraceId,
            ToolName = call.ToolName,
            Access = call.Access,
            Allowed = allowed,
            RequestJson = JsonSerializer.Serialize(call.Arguments),
            ResponseJson = JsonSerializer.Serialize(data ?? new { }),
            DenyReason = denyReason,
            CreatedAt = DateTimeOffset.UtcNow
        }, ct);

        return new ToolResult(allowed, success && allowed, call.ToolName, data, denyReason);
    }

    private static string ArgString(IDictionary<string, object?> args, string key) =>
        args.TryGetValue(key, out var value) ? Convert.ToString(value) ?? string.Empty : string.Empty;

    private static object? ArgObject(IDictionary<string, object?> args, string key) =>
        args.TryGetValue(key, out var value) ? value : null;
}

public sealed class AgentOrchestrator(
    IRefundDataStore store,
    IRulesEngine rules,
    IToolGateway tools,
    IConfirmationStore confirmationStore,
    ISessionStore sessionStore,
    ILogger<AgentOrchestrator> logger) : IAgentOrchestrator
{
    public async Task<AgentDecisionDto> HandleAsync(AgentMessageRequest request, CancellationToken ct = default)
    {
        if (request.ResetDemo)
        {
            await store.ResetDemoAsync(ct);
        }
        else
        {
            await store.EnsureSeededAsync(ct);
        }

        var scenario = ResolveScenario(request);
        var order = await store.GetOrderAsync(scenario.OrderId, ct)
                    ?? throw new InvalidOperationException($"order missing for scenario {scenario.Code}");
        var policy = await store.GetPolicyAsync(order.CancelPolicyCode, ct)
                     ?? new PolicySnapshot { PolicyId = "P-DEFAULT", Code = order.CancelPolicyCode, Title = "默认政策", Summary = "演示政策" };

        var traceId = $"trc_{Guid.NewGuid():N}"[..16];
        var runId = $"run_{Guid.NewGuid():N}"[..16];
        var caseId = $"case_{scenario.Code}_{order.OrderId}".ToLowerInvariant();
        var userId = request.UserId ?? order.UserId;

        var steps = new List<DecisionStepDto>();

        var intent = InferIntent(request.Message, scenario);
        steps.Add(new DecisionStepDto("意图识别", "success", intent.Name, intent.Confidence));

        var slots = new Dictionary<string, string>
        {
            ["order_id"] = order.OrderId,
            ["check_in"] = order.CheckIn.ToString("yyyy-MM-dd"),
            ["arrived"] = order.Arrived ? "yes" : "not_arrival",
            ["refund_reason"] = intent.Reason,
            ["free_cancel_window"] = policy.FreeCancel ? "yes" : "no",
            ["evidence"] = request.HasEvidence ? "uploaded" : "missing"
        };
        var missing = request.HasEvidence || !NeedsEvidence(scenario.Id) ? 0 : 1;
        steps.Add(new DecisionStepDto("槽位提取", missing > 0 ? "warning" : "success",
            missing > 0 ? $"缺失 {missing} 项必要信息" : "关键槽位完整"));

        var orderTool = await tools.InvokeAsync(new ToolCall(
            traceId, "get_order_detail", ToolAccess.Read, userId, order.OrderId, caseId, scenario.RiskLevel,
            new Dictionary<string, object?>()), ct);
        steps.Add(new DecisionStepDto("订单查询", orderTool.Allowed ? "success" : "error",
            $"status={order.Status}, arrived={order.Arrived}"));

        var policyTool = await tools.InvokeAsync(new ToolCall(
            traceId, "get_policy_snapshot", ToolAccess.Read, userId, order.OrderId, caseId, scenario.RiskLevel,
            new Dictionary<string, object?> { ["policyCode"] = order.CancelPolicyCode }), ct);
        steps.Add(new DecisionStepDto("政策检索", policyTool.Allowed ? "success" : "error",
            $"{policy.Code} · {policy.Title}"));

        var decision = rules.Evaluate(order, policy, scenario.Id, request.HasEvidence);
        steps.Add(new DecisionStepDto("规则校验",
            decision.NeedsEvidence ? "warning" : "success",
            decision.RuleCode));
        steps.Add(new DecisionStepDto("风险判断", "success",
            $"{decision.RiskLevel} · score={decision.RiskScore}"));

        await tools.InvokeAsync(new ToolCall(
            traceId, "calculate_refund_quote", ToolAccess.Write, userId, order.OrderId, caseId, decision.RiskLevel,
            new Dictionary<string, object?> { ["refund"] = decision.RefundAmount, ["fee"] = decision.FeeAmount }), ct);

        string? confirmationToken = null;
        if (decision.NeedsUserConfirm)
        {
            confirmationToken = await confirmationStore.IssueAsync(
                caseId, order.OrderId, order.Version, "submit_cancellation", TimeSpan.FromMinutes(10), ct);
        }

        // Side-effect tools for non-confirm paths
        if (decision.Action == AgentAction.NegotiateWithHotel && !decision.NeedsEvidence)
        {
            await tools.InvokeAsync(new ToolCall(
                traceId, "create_supplier_case", ToolAccess.Write, userId, order.OrderId, caseId, decision.RiskLevel,
                new Dictionary<string, object?> { ["reason"] = intent.Reason },
                ConfirmationToken: null), ct);
            // negotiate without confirm for demo when evidence already present - gateway may deny without token; issue+consume for demo
        }

        if (decision.Action == AgentAction.HumanHandoff)
        {
            await tools.InvokeAsync(new ToolCall(
                traceId, "create_human_handoff", ToolAccess.Write, userId, order.OrderId, caseId, decision.RiskLevel,
                new Dictionary<string, object?> { ["summary"] = decision.Conclusion }), ct);
        }

        if (decision.Action == AgentAction.ExplainProgress)
        {
            await tools.InvokeAsync(new ToolCall(
                traceId, "get_refund_status", ToolAccess.Read, userId, order.OrderId, caseId, decision.RiskLevel,
                new Dictionary<string, object?>()), ct);
        }

        steps.Add(new DecisionStepDto("处理动作", "active", decision.Action.ToString()));

        var status = decision.Action switch
        {
            AgentAction.RequestEvidence => CaseStatus.WaitingEvidence,
            AgentAction.NegotiateWithHotel => CaseStatus.WaitingSupplier,
            AgentAction.HumanHandoff => CaseStatus.Escalated,
            AgentAction.ExplainProgress => CaseStatus.WaitingPayment,
            AgentAction.ConfirmCancel => CaseStatus.WaitingUserConfirm,
            AgentAction.AutoRefund => CaseStatus.Completed,
            _ => CaseStatus.Open
        };

        var refundCase = new RefundCase
        {
            CaseId = caseId,
            OrderId = order.OrderId,
            UserId = userId,
            ScenarioId = scenario.Id,
            Status = status,
            RiskLevel = decision.RiskLevel,
            Intent = intent.Name,
            QuoteRefundAmount = decision.RefundAmount,
            QuoteFeeAmount = decision.FeeAmount,
            RecommendedAction = decision.Action,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await store.UpsertCaseAsync(refundCase, ct);
        await store.AppendEventAsync(caseId, "agent_decision", new
        {
            decision.Action,
            decision.RuleCode,
            decision.RiskScore,
            confirmationToken
        }, ct);

        var run = new WorkflowRun
        {
            RunId = runId,
            CaseId = caseId,
            ScenarioId = scenario.Id,
            Status = "COMPLETED",
            TraceJson = JsonSerializer.Serialize(steps),
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        };
        await store.SaveWorkflowRunAsync(run, ct);
        await sessionStore.SetAsync($"session:{userId}:{order.OrderId}", JsonSerializer.Serialize(new
        {
            caseId,
            runId,
            scenario.Code,
            confirmationToken
        }), TimeSpan.FromHours(6), ct);

        var reply = BuildReply(decision, order, request.HasEvidence);
        logger.LogInformation("Handled scenario {Scenario} action {Action} trace {Trace}", scenario.Code, decision.Action, traceId);

        return new AgentDecisionDto(
            traceId,
            runId,
            caseId,
            scenario.Code,
            intent.Name,
            intent.Confidence,
            decision.RiskLevel,
            decision.RiskScore,
            decision.Action,
            decision.Conclusion,
            decision.PlanTitle,
            decision.PlanCopy,
            decision.RefundAmount,
            decision.FeeAmount,
            reply,
            steps,
            slots,
            new HotelOrderDto(
                order.OrderId, order.HotelName, order.CheckIn, order.CheckOut,
                order.Amount, order.Status, order.Arrived, order.CancelPolicyCode, order.Version));
    }

    private ScenarioFixture ResolveScenario(AgentMessageRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ScenarioCode))
        {
            return store.GetScenario(request.ScenarioCode!);
        }

        var text = request.Message ?? string.Empty;
        if (text.Contains("到店", StringComparison.Ordinal) || text.Contains("没有我的房间", StringComparison.Ordinal))
            return store.GetScenario("no-room");
        if (text.Contains("航班", StringComparison.Ordinal))
            return store.GetScenario("flight-cancelled");
        if (text.Contains("不可取消", StringComparison.Ordinal) || text.Contains("不能退", StringComparison.Ordinal))
            return store.GetScenario("non-cancellable");
        if (text.Contains("扣了两次", StringComparison.Ordinal) || text.Contains("押金", StringComparison.Ordinal))
            return store.GetScenario("payment-anomaly");
        if (text.Contains("到账", StringComparison.Ordinal) || text.Contains("进度", StringComparison.Ordinal))
            return store.GetScenario("refund-progress");
        if (text.Contains("扣多少", StringComparison.Ordinal))
            return store.GetScenario("deducted-cancel");
        return store.GetScenario("free-cancellation");
    }

    private static bool NeedsEvidence(ScenarioId id) =>
        id is ScenarioId.FlightCancelled or ScenarioId.NonCancellable;

    private static (string Name, string Reason, double Confidence) InferIntent(string message, ScenarioFixture scenario)
    {
        return scenario.Id switch
        {
            ScenarioId.RefundProgress => ("查询退款进度", "refund_progress", 0.91),
            ScenarioId.NoRoomOnArrival => ("履约异常求助", "no_room", 0.96),
            ScenarioId.PaymentAnomaly => ("支付异常核查", "payment_anomaly", 0.9),
            ScenarioId.FlightCancelled => ("申请退款", "flight_cancelled", 0.93),
            _ => ("申请退款", string.IsNullOrWhiteSpace(message) ? scenario.Code : "user_request", 0.92)
        };
    }

    private static string BuildReply(RuleDecision decision, HotelOrder order, bool hasEvidence)
    {
        return decision.Action switch
        {
            AgentAction.RequestEvidence =>
                $"我已核对订单 {order.OrderId}（{order.HotelName}）。{decision.Conclusion}。请先上传相关证明，我再继续帮你推进。",
            AgentAction.ConfirmCancel =>
                $"{decision.Conclusion}。预计退回 ¥{decision.RefundAmount:0.##}，费用 ¥{decision.FeeAmount:0.##}。确认后我再提交取消。",
            AgentAction.NegotiateWithHotel =>
                hasEvidence
                    ? $"材料已收到。{decision.Conclusion}，我会为你创建酒店协商并持续跟踪。"
                    : decision.Conclusion,
            AgentAction.HumanHandoff =>
                $"{decision.Conclusion}。我已准备好转人工摘要，专员会带着订单事实与政策依据接手。",
            AgentAction.ExplainProgress =>
                $"{decision.Conclusion}。当前无需重复提交退款申请。",
            _ => decision.Conclusion
        };
    }
}
