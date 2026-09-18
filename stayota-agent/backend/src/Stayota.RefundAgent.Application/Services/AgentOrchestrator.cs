using System.Text.Json;
using Microsoft.Extensions.Logging;
using Stayota.RefundAgent.Application.Ai;
using Stayota.RefundAgent.Application.Contracts;
using Stayota.RefundAgent.Domain;
using Stayota.RefundAgent.Domain.Entities;

namespace Stayota.RefundAgent.Application.Services;

public sealed class AgentOrchestrator(
    IRefundDataStore store,
    IIntentService intentService,
    IPolicyRetrieval retrieval,
    IRulesEngine rules,
    IRefundAiToolCatalog tools,
    IRefundAgentHost agentHost,
    IConfirmationStore confirmationStore,
    ISessionStore sessionStore,
    IVerifier verifier,
    ILogger<AgentOrchestrator> logger) : IAgentOrchestrator
{
    private readonly ScenarioRouter _router = new();

    public async Task<AgentDecisionDto> HandleAsync(AgentMessageRequest request, CancellationToken ct = default)
    {
        if (request.ServiceError)
            throw new InvalidOperationException("模拟订单服务响应超时");

        if (request.ResetDemo) await store.ResetDemoAsync(ct);
        else await store.EnsureSeededAsync(ct);

        var scenarioId = _router.Route(request.Message, request.ScenarioId);
        var scenario = store.GetScenario(scenarioId);
        var order = await store.GetOrderAsync(scenario.OrderId, ct)
                    ?? throw new InvalidOperationException($"missing order {scenario.OrderId}");
        var policy = await store.GetPolicyAsync(order.PolicyId, ct)
                     ?? new PolicySnapshot { PolicyId = order.PolicyId, Title = "默认政策", Summary = "演示", RuleCode = "DEFAULT" };

        var signals = new AgentSignals(request.HasEvidence, request.HasNegotiationReason, request.LowConfidence, request.ServiceError, request.ConfirmWrite);
        var traceId = $"trc_{Guid.NewGuid():N}"[..16];
        var runId = $"run_{Guid.NewGuid():N}"[..16];
        var userId = request.UserId ?? scenario.UserId;
        var steps = new List<DecisionStepDto>();

        var analyzed = intentService.Analyze(request.Message, scenario, request.LowConfidence);
        steps.Add(new("意图识别", analyzed.Confidence < 0.5 ? "warning" : "success", analyzed.Intent, analyzed.Confidence));

        if (request.HasEvidence) analyzed.Slots["evidence"] = "uploaded";
        if (request.HasNegotiationReason) analyzed.Slots["negotiation_reason"] = "provided";
        var missing = (signals.HasEvidence || !NeedsEvidence(scenario.ScenarioId, signals)) ? 0 : 1;
        steps.Add(new("槽位提取", missing > 0 ? "warning" : "success", missing > 0 ? $"缺失 {missing} 项" : "槽位完整"));

        await tools.InvokeAsync(Read(traceId, "list_user_orders", userId, order, scenario, "INTENT_READY"), ct);
        var orderTool = await tools.InvokeAsync(Read(traceId, "get_order_detail", userId, order, scenario, "ORDER_CONFIRMED"), ct);
        steps.Add(new("订单查询", orderTool.Allowed ? "success" : "error", $"status={order.Status}, on_site={order.UserOnSite}"));

        var matches = retrieval.Retrieve(order, policy, analyzed.Reason);
        await tools.InvokeAsync(Read(traceId, "get_policy_snapshot", userId, order, scenario, "ORDER_CONFIRMED"), ct);
        steps.Add(new("政策检索", "success", $"{matches[0].PolicyId} · score={matches[0].Score:0.00}"));

        var decision = rules.Evaluate(order, policy, scenario, signals);
        steps.Add(new("规则校验", decision.NeedsEvidence ? "warning" : "success", decision.RuleCode));
        steps.Add(new("风险判断", "success", $"{decision.RiskLevel} · {decision.RiskScore}"));

        var requiredTools = JsonSerializer.Deserialize<List<string>>(scenario.RequiredToolsJson) ?? [];
        var executed = new List<string>();
        string? confirmationToken = null;

        if (decision.NeedsUserConfirm)
        {
            confirmationToken = await confirmationStore.IssueAsync(
                scenario.CaseId, order.OrderId, order.Version,
                scenario.ScenarioId == "I" ? "submit_order_change" : "submit_cancellation",
                TimeSpan.FromMinutes(10), ct);
        }

        foreach (var toolName in requiredTools.Distinct())
        {
            // Skip heavy writes unless confirmed or non-confirm tools
            var access = ToolGatewayWrite(toolName) ? ToolAccess.Write : ToolAccess.Read;
            var args = new Dictionary<string, object?>
            {
                ["refund"] = decision.RefundAmount,
                ["fee"] = decision.FeeAmount,
                ["summary"] = decision.Conclusion,
                ["reason"] = analyzed.Reason
            };

            string? token = null;
            string? idem = null;
            int? version = null;
            if (toolName is "submit_cancellation" or "submit_order_change" or "accept_supplier_offer" or "reserve_mock_alternative")
            {
                if (!(request.ConfirmWrite && confirmationToken is not null))
                {
                    continue;
                }
                token = request.ConfirmationToken ?? confirmationToken;
                idem = request.IdempotencyKey ?? $"idem-{scenario.ScenarioId}-{order.OrderId}-{toolName}";
                version = order.Version;
            }

            // For supplier/evidence/handoff tools, execute when path reaches them
            if (toolName is "submit_evidence_metadata" or "extract_evidence_fields" or "create_exception_review")
            {
                if (!signals.HasEvidence) continue;
            }
            if (toolName is "create_supplier_case" or "get_supplier_case" or "accept_supplier_offer")
            {
                if (decision.Action is "RequestInformation" or "RequestEvidence") continue;
            }

            var toolState = ToolStates.GetValueOrDefault(toolName, "DECISION_READY");
            // scenario-specific overrides aligned with Hotel-refund workflow
            if (scenario.ScenarioId == "H" && toolName == "create_human_handoff") toolState = "WAITING_EXTERNAL";
            if (scenario.ScenarioId == "K" && toolName == "create_human_handoff") toolState = "DECISION_READY";
            if ((scenario.ScenarioId is "E" or "L" or "D") && toolName == "create_human_handoff") toolState = "OPTION_PRESENTED";

            var result = await tools.InvokeAsync(new ToolCall(
                traceId, toolName, access, userId, order.OrderId, scenario.CaseId, decision.RiskLevel,
                toolState, args, token, idem, version), ct);
            if (result.Allowed) executed.Add(toolName);
        }

        // Ensure quote + permission always attempted
        await tools.InvokeAsync(new ToolCall(traceId, "calculate_refund_quote", ToolAccess.Read, userId, order.OrderId, scenario.CaseId, decision.RiskLevel, "DECISION_READY",
            new Dictionary<string, object?> { ["refund"] = decision.RefundAmount, ["fee"] = decision.FeeAmount }), ct);
        await tools.InvokeAsync(new ToolCall(traceId, "validate_action_permission", ToolAccess.Read, userId, order.OrderId, scenario.CaseId, decision.RiskLevel, "DECISION_READY",
            new Dictionary<string, object?> { ["action"] = decision.Action }), ct);

        // Side effects for key actions
        if (decision.Action is "HumanHandoff" or "Recovery" or "FinanceReview" or "ServiceDispute" or "SpecialReview")
        {
            var handoff = await tools.InvokeAsync(new ToolCall(traceId, "create_human_handoff", ToolAccess.Write, userId, order.OrderId, scenario.CaseId, decision.RiskLevel, scenario.ScenarioId switch { "H" => "WAITING_EXTERNAL", "K" => "DECISION_READY", _ => "OPTION_PRESENTED" },
                new Dictionary<string, object?> { ["summary"] = decision.Conclusion }, IdempotencyKey: $"ho-{scenario.ScenarioId}-{runId}"), ct);
            if (handoff.Allowed) executed.Add("create_human_handoff");
        }
        if (decision.Action == "NegotiateWithHotel")
        {
            await tools.InvokeAsync(Read(traceId, "build_supplier_case_draft", userId, order, scenario, "FACTS_REQUIRED"), ct);
            await tools.InvokeAsync(new ToolCall(traceId, "create_supplier_case", ToolAccess.Write, userId, order.OrderId, scenario.CaseId, decision.RiskLevel, "CONFIRMATION_REQUIRED",
                new Dictionary<string, object?>(), IdempotencyKey: $"sup-{scenario.ScenarioId}-{runId}"), ct);
            executed.Add("create_supplier_case");
        }
        if (decision.Action == "ExplainProgress")
        {
            await tools.InvokeAsync(Read(traceId, "get_refund_status", userId, order, scenario, "TRACKING_REFUND"), ct);
            await tools.InvokeAsync(new ToolCall(traceId, "schedule_deadline_action", ToolAccess.Write, userId, order.OrderId, scenario.CaseId, decision.RiskLevel, "TRACKING_REFUND",
                new Dictionary<string, object?>(), IdempotencyKey: $"sch-{scenario.ScenarioId}-{runId}"), ct);
        }
        if ((decision.Action is "Recovery" or "HumanHandoff") && scenario.ScenarioId is "D" or "E")
        {
            await tools.InvokeAsync(Read(traceId, "verify_fulfillment_issue", userId, order, scenario, "DECISION_READY"), ct);
            await tools.InvokeAsync(Read(traceId, "get_alternative_hotels", userId, order, scenario, "DECISION_READY"), ct);
        }
        if (signals.HasEvidence && scenario.ScenarioId is "G" or "F" or "H")
        {
            await tools.InvokeAsync(new ToolCall(traceId, "submit_evidence_metadata", ToolAccess.Write, userId, order.OrderId, scenario.CaseId, decision.RiskLevel, "FACTS_REQUIRED",
                new Dictionary<string, object?> { ["evidence_type"] = "flight_cancel" }, IdempotencyKey: $"ev-{runId}"), ct);
        }

        steps.Add(new("处理动作", "active", decision.Action));

        TicketDto? ticket = null;
        if (decision.Action is "HumanHandoff" or "NegotiateWithHotel" or "Recovery" or "FinanceReview" or "SpecialReview" or "ServiceDispute")
        {
            ticket = new TicketDto(
                $"TKT-{scenario.ScenarioId}-{DateTime.UtcNow:HHmmss}",
                decision.RiskLevel == RiskLevel.L3 ? "P1" : "P2",
                decision.RiskLevel == RiskLevel.L3 ? "urgent" : "specialist",
                decision.PlanTitle,
                [
                    $"订单 {order.OrderId} / {order.HotelName}",
                    $"政策 {policy.PolicyId}",
                    $"结论 {decision.Conclusion}",
                    $"风险 {decision.RiskLevel}/{decision.RiskScore}"
                ],
                BuildTicketLifecycle(decision.Action, decision.RiskLevel, decision.CaseStatus));
        }

        HitlStateDto? hitl = null;
        if (decision.NeedsUserConfirm)
        {
            hitl = new HitlStateDto(
                true,
                scenario.ScenarioId == "I" ? "submit_order_change" : "submit_cancellation",
                confirmationToken);
        }

        var refundCase = new RefundCase
        {
            CaseId = scenario.CaseId,
            OrderId = order.OrderId,
            UserId = userId,
            ScenarioId = scenario.ScenarioId,
            Status = decision.CaseStatus,
            RiskLevel = decision.RiskLevel,
            Intent = analyzed.Intent,
            RecommendedAction = decision.Action,
            QuoteRefundAmount = decision.RefundAmount,
            QuoteFeeAmount = decision.FeeAmount,
            ConversationState = decision.ConversationState,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await store.UpsertCaseAsync(refundCase, ct);
        await store.AppendEventAsync(scenario.CaseId, "agent_decision", new { decision.Action, decision.RuleCode, executed, confirmationToken }, ct);

        var run = new WorkflowRun
        {
            RunId = runId,
            CaseId = scenario.CaseId,
            ScenarioId = scenario.ScenarioId,
            Status = "COMPLETED",
            TraceJson = JsonSerializer.Serialize(steps),
            ToolSequenceJson = JsonSerializer.Serialize(executed.Distinct()),
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        };
        await store.SaveWorkflowRunAsync(run, ct);
        await sessionStore.SetAsync($"session:{userId}:{order.OrderId}", JsonSerializer.Serialize(new { scenario.CaseId, runId, confirmationToken }), TimeSpan.FromHours(6), ct);

        logger.LogInformation(
            "Scenario {Scenario} action {Action} aiProvider={Provider} agent={Agent}",
            scenario.ScenarioId, decision.Action, agentHost.ProviderName, agentHost.Agent.Name);

        var dto = new AgentDecisionDto(
            traceId, runId, scenario.CaseId, scenario.ScenarioId,
            analyzed.Intent, analyzed.Confidence, decision.RiskLevel, decision.RiskScore,
            decision.Action, decision.Conclusion, decision.PlanTitle, decision.PlanCopy,
            decision.RefundAmount, decision.FeeAmount,
            BuildReply(decision, order),
            decision.ConversationState, decision.CaseStatus,
            steps, analyzed.Slots, matches, executed.Distinct().ToList(), ticket,
            new HotelOrderDto(order.OrderId, order.HotelName, order.CheckIn, order.CheckOut, order.PaidAmount, order.Currency,
                order.Status, order.UserOnSite, order.PolicyId, order.Version, order.RoomType, order.RoomCount),
            false, Array.Empty<string>(), hitl, agentHost.ProviderName);
        var verification = verifier.VerifyDecision(dto);
        return dto with { VerificationPassed = verification.Passed, VerificationViolations = verification.Violations };
    }

    private static IReadOnlyList<TicketLifecycleStepDto> BuildTicketLifecycle(string action, RiskLevel risk, string caseStatus)
    {
        var queue = risk == RiskLevel.L3 ? "紧急专席" : "专项队列";
        return
        [
            new("受理建单", "done", $"已创建 {queue} 工单上下文"),
            new("事实汇总", "done", "订单 / 政策 / 风险已写入工单摘要"),
            new("专席认领", caseStatus is "ESCALATED" or "WAITING_EXTERNAL" or "SPECIAL_REVIEW" ? "active" : "pending",
                action is "NegotiateWithHotel" ? "等待供应商回执" : "等待专员接手"),
            new("用户可见更新", "pending", "公开进度文案待专席确认后同步"),
            new("结案回写", "pending", "恢复会话与审计 Trace 待闭环")
        ];
    }

    private static readonly Dictionary<string, string> ToolStates = new()
    {
        ["list_user_orders"] = "INTENT_READY",
        ["get_order_detail"] = "ORDER_CONFIRMED",
        ["get_policy_snapshot"] = "ORDER_CONFIRMED",
        ["list_after_sale_events"] = "FACTS_REQUIRED",
        ["calculate_refund_quote"] = "DECISION_READY",
        ["validate_action_permission"] = "DECISION_READY",
        ["submit_cancellation"] = "CONFIRMATION_REQUIRED",
        ["get_refund_status"] = "TRACKING_REFUND",
        ["get_payment_events"] = "TRACKING_REFUND",
        ["schedule_deadline_action"] = "TRACKING_REFUND",
        ["create_payment_investigation"] = "WAITING_EXTERNAL",
        ["verify_fulfillment_issue"] = "ORDER_CONFIRMED",
        ["get_alternative_hotels"] = "DECISION_READY",
        ["get_guarantee_quote"] = "DECISION_READY",
        ["create_human_handoff"] = "OPTION_PRESENTED",
        ["build_supplier_case_draft"] = "FACTS_REQUIRED",
        ["create_supplier_case"] = "CONFIRMATION_REQUIRED",
        ["submit_evidence_metadata"] = "FACTS_REQUIRED",
        ["extract_evidence_fields"] = "FACTS_REQUIRED",
        ["create_exception_review"] = "DECISION_READY",
        ["create_service_dispute_case"] = "DECISION_READY",
        ["get_change_quote"] = "DECISION_READY",
        ["submit_order_change"] = "CONFIRMATION_REQUIRED",
        ["create_finance_case"] = "DECISION_READY",
        ["get_responsibility_chain"] = "DECISION_READY",
        ["get_group_order_breakdown"] = "FACTS_REQUIRED",
        ["get_partial_cancel_quote"] = "DECISION_READY",
        ["get_supplier_case"] = "WAITING_EXTERNAL",
        ["accept_supplier_offer"] = "OPTION_PRESENTED",
        ["get_handoff_status"] = "ESCALATED",
        ["confirm_recovery_outcome"] = "ESCALATED",
        ["reserve_mock_alternative"] = "OPTION_PRESENTED",
    };


    private static bool NeedsEvidence(string scenarioId, AgentSignals signals) =>
        scenarioId is "G" || (scenarioId is "F" && !signals.HasNegotiationReason);

    private static bool ToolGatewayWrite(string name) =>
        name.StartsWith("submit_") || name.StartsWith("create_") || name.StartsWith("accept_") ||
        name.StartsWith("reserve_") || name.StartsWith("confirm_") || name.StartsWith("schedule_");

    private static ToolCall Read(string traceId, string tool, string userId, HotelOrder order, ScenarioFixture scenario, string state) =>
        new(traceId, tool, ToolAccess.Read, userId, order.OrderId, scenario.CaseId, scenario.RiskLevel, state, new Dictionary<string, object?>());

    private static string BuildReply(RuleDecision d, HotelOrder order) => d.Action switch
    {
        "RequestEvidence" => $"已核对订单 {order.OrderId}（{order.HotelName}）。{d.Conclusion}。请先上传相关证明。",
        "RequestInformation" => $"{d.Conclusion}。请补充无法入住的具体原因后，我再发起协商。",
        "ConfirmCancel" => $"{d.Conclusion}。预计退回 {order.Currency} {d.RefundAmount:0.##}，费用 {d.FeeAmount:0.##}。确认后提交。",
        "Clarify" => d.PlanCopy,
        _ => $"{d.Conclusion}。{d.PlanCopy}"
    };
}

public sealed class EvalRunner(IRefundDataStore store, IAgentOrchestrator orchestrator) : IEvalRunner
{
    private readonly ScenarioRouter _router = new();

    public IReadOnlyList<EvalCaseDto> ListCases()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../eval/agent-eval-cases.json"));
        if (!File.Exists(path))
            path = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../eval/agent-eval-cases.json"));
        // fallback relative to stayota-agent root via env
        var root = Environment.GetEnvironmentVariable("STAYOTA_AGENT_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
            path = Path.Combine(root, "eval/agent-eval-cases.json");

        if (!File.Exists(path))
        {
            // generate from known set if file missing at runtime
            return ScenarioCodes.All.SelectMany(s => Enumerable.Range(1, 3).Select(i =>
                new EvalCaseDto($"EVAL-{s}-{i:00}", $"scenario {s} sample {i}", s, "L1"))).ToList();
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("cases").EnumerateArray().Select(c =>
            new EvalCaseDto(
                c.GetProperty("id").GetString()!,
                c.GetProperty("message").GetString()!,
                c.GetProperty("expected_scenario").GetString()!,
                c.GetProperty("risk_level").GetString()!)).ToList();
    }

    public async Task<IReadOnlyList<EvalResultDto>> RunAllAsync(CancellationToken ct = default)
    {
        await store.EnsureSeededAsync(ct);
        var results = new List<EvalResultDto>();
        foreach (var c in ListCases())
        {
            var routed = _router.Route(c.Message, null);
            var passed = routed == c.ExpectedScenario;
            string? detail = null;
            if (passed)
            {
                try
                {
                    var decision = await orchestrator.HandleAsync(new AgentMessageRequest(c.Message, c.ExpectedScenario, ResetDemo: false), ct);
                    detail = $"{decision.Action}/{decision.CaseStatus}";
                    passed = decision.ScenarioId == c.ExpectedScenario;
                }
                catch (Exception ex)
                {
                    passed = false;
                    detail = ex.Message;
                }
            }
            else detail = $"routed={routed}";
            results.Add(new EvalResultDto(c.Id, c.Message, c.ExpectedScenario, routed, passed, detail));
        }
        return results;
    }
}
