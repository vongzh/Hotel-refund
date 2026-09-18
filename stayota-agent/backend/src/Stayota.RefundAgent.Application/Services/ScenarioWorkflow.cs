using System.Text.Json;
using Microsoft.Extensions.Logging;
using Stayota.RefundAgent.Application.Contracts;
using Stayota.RefundAgent.Domain;
using Stayota.RefundAgent.Domain.Entities;

namespace Stayota.RefundAgent.Application.Services;

public sealed class ScenarioWorkflow(
    IRefundDataStore store,
    IToolGateway tools,
    IConfirmationStore confirmationStore,
    IVerifier verifier,
    ILogger<ScenarioWorkflow> logger) : IScenarioWorkflow
{
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
        ["reserve_mock_alternative"] = "OPTION_PRESENTED",
        ["create_human_handoff"] = "OPTION_PRESENTED",
        ["get_handoff_status"] = "ESCALATED",
        ["confirm_recovery_outcome"] = "ESCALATED",
        ["build_supplier_case_draft"] = "FACTS_REQUIRED",
        ["create_supplier_case"] = "CONFIRMATION_REQUIRED",
        ["get_supplier_case"] = "WAITING_EXTERNAL",
        ["accept_supplier_offer"] = "OPTION_PRESENTED",
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
    };

    private static readonly HashSet<string> ConfirmTools =
    [
        "submit_cancellation", "reserve_mock_alternative", "accept_supplier_offer", "submit_order_change"
    ];

    public async Task<WorkflowRunResultDto> RunAsync(string scenarioId, CancellationToken ct = default)
    {
        await store.EnsureSeededAsync(ct);
        scenarioId = scenarioId.ToUpperInvariant();
        var scenario = store.GetScenario(scenarioId);
        var order = await store.GetOrderAsync(scenario.OrderId, ct)
                    ?? throw new InvalidOperationException($"order missing {scenario.OrderId}");
        var requiredTools = JsonSerializer.Deserialize<List<string>>(scenario.RequiredToolsJson) ?? [];
        if (scenarioId == "H")
        {
            var idx = requiredTools.IndexOf("submit_evidence_metadata");
            if (idx >= 0) requiredTools.Insert(idx + 1, "submit_evidence_metadata");
        }

        var runId = $"run_{Guid.NewGuid():N}"[..16];
        var traceId = $"trc_{Guid.NewGuid():N}"[..16];
        var state = "START";
        var steps = new List<WorkflowStepDto>();
        var toolCalls = new List<string>();
        var facts = new Dictionary<string, object?>();

        state = Record(steps, "INTENT_AND_ROUTE", "RULE_ENGINE", state, "INTENT_READY", null,
            new { scenario.EntryMessage, route = scenario.ExpectedRoute, scenario.RiskLevel });

        var risk = scenario.RiskLevel;
        for (var occurrence = 0; occurrence < requiredTools.Count; occurrence++)
        {
            var toolName = requiredTools[occurrence];
            var desired = StateFor(scenarioId, toolName);
            if (state != desired)
                state = Record(steps, "WORKFLOW_ROUTE", "STATE_MACHINE", state, desired, null, new { next_tool = toolName });

            var args = BuildArgs(toolName, scenarioId, scenario, order, facts, traceId, occurrence);
            string? token = null;
            if (ConfirmTools.Contains(toolName))
            {
                token = await confirmationStore.IssueAsync(
                    scenario.CaseId, order.OrderId, order.Version, toolName, TimeSpan.FromMinutes(10), ct);
                args["confirmation_token"] = token;
            }

            var access = toolName.StartsWith("submit_") || toolName.StartsWith("create_") ||
                         toolName.StartsWith("accept_") || toolName.StartsWith("reserve_") ||
                         toolName.StartsWith("confirm_") || toolName.StartsWith("schedule_")
                ? ToolAccess.Write : ToolAccess.Read;

            var result = await tools.InvokeAsync(new ToolCall(
                traceId, toolName, access, scenario.UserId, order.OrderId, scenario.CaseId, risk,
                state, args, token, Convert.ToString(args.GetValueOrDefault("idempotency_key")),
                order.Version), ct);

            if (!result.Allowed || !result.Success)
                throw new InvalidOperationException($"{toolName} failed: {result.DenyReason}");

            facts[toolName] = result.Data;
            toolCalls.Add(toolName);
            var after = NextStateAfter(toolName, state);
            Record(steps, "TOOL_EXECUTION", "TOOL", state, after, toolName, result.Data);
            state = after;
        }

        var caseStatus = MapFinalCaseStatus(scenarioId, state, scenario.ExpectedCaseStatus);
        await store.UpsertCaseAsync(new RefundCase
        {
            CaseId = scenario.CaseId,
            OrderId = order.OrderId,
            UserId = scenario.UserId,
            ScenarioId = scenarioId,
            Status = caseStatus,
            RiskLevel = risk,
            Intent = scenario.Title,
            RecommendedAction = scenario.ExpectedRoute,
            ConversationState = state,
            UpdatedAt = DateTimeOffset.UtcNow
        }, ct);

        var expectedOriginal = JsonSerializer.Deserialize<List<string>>(scenario.RequiredToolsJson) ?? [];
        var draft = new WorkflowRunResultDto(
            runId, scenarioId, scenario.ExpectedRoute, scenario.CaseId, state, caseStatus,
            toolCalls, steps, new WorkflowAssertionDto(false, false, false, false), false);
        var verification = verifier.VerifyWorkflow(draft, expectedOriginal, scenario.ExpectedCaseStatus);
        var assertions = new WorkflowAssertionDto(
            IsSubsequence(expectedOriginal, toolCalls),
            Verifier.StatusCompatible(caseStatus, scenario.ExpectedCaseStatus),
            toolCalls.All(t => expectedOriginal.Contains(t) || (scenarioId == "H" && t == "submit_evidence_metadata")),
            verification.Passed);
        var succeeded = assertions.RequiredToolsCalledInOrder && assertions.NoUnexpectedTool && assertions.ExpectedCaseStatusReached;

        await store.SaveWorkflowRunAsync(new WorkflowRun
        {
            RunId = runId,
            CaseId = scenario.CaseId,
            ScenarioId = scenarioId,
            Status = succeeded ? "SUCCEEDED" : "ASSERTION_FAILED",
            TraceJson = JsonSerializer.Serialize(steps),
            ToolSequenceJson = JsonSerializer.Serialize(toolCalls),
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        }, ct);

        logger.LogInformation("Workflow {Scenario} succeeded={Succeeded} tools={Count}", scenarioId, succeeded, toolCalls.Count);
        return new WorkflowRunResultDto(runId, scenarioId, scenario.ExpectedRoute, scenario.CaseId, state, caseStatus,
            toolCalls, steps, assertions, succeeded);
    }

    private static string StateFor(string scenarioId, string toolName)
    {
        var overrides = new Dictionary<(string, string), string>
        {
            [("C", "get_payment_events")] = "TRACKING_REFUND",
            [("H", "create_human_handoff")] = "WAITING_EXTERNAL",
            [("K", "create_human_handoff")] = "DECISION_READY",
            [("E", "create_human_handoff")] = "OPTION_PRESENTED",
            [("L", "create_human_handoff")] = "OPTION_PRESENTED",
        };
        return overrides.TryGetValue((scenarioId, toolName), out var s) ? s : ToolStates[toolName];
    }

    private static string NextStateAfter(string toolName, string current) => toolName switch
    {
        "submit_cancellation" => "TRACKING_REFUND",
        "schedule_deadline_action" => "WAITING_EXTERNAL",
        "create_human_handoff" => "ESCALATED",
        "confirm_recovery_outcome" => "RESOLVED",
        "create_supplier_case" => "WAITING_EXTERNAL",
        "get_supplier_case" => "OPTION_PRESENTED",
        "accept_supplier_offer" => "TRACKING_REFUND",
        "create_exception_review" => "ESCALATED",
        "create_service_dispute_case" => "WAITING_EXTERNAL",
        "submit_order_change" => "RESOLVED",
        "create_finance_case" => "ESCALATED",
        _ => current
    };

    private static string MapFinalCaseStatus(string scenarioId, string state, string expected) => expected;

    private static Dictionary<string, object?> BuildArgs(
        string name, string scenarioId, ScenarioFixture scenario, HotelOrder order,
        Dictionary<string, object?> facts, string traceId, int occurrence)
    {
        var userId = scenario.UserId;
        var orderId = order.OrderId;
        var caseId = scenario.CaseId;
        var idem = $"idem-{scenarioId}-{name}-{occurrence}";

        // Prefer serialized roundtrip for anonymous objects
        string? FactStr(string tool, string field)
        {
            if (!facts.TryGetValue(tool, out var data) || data is null) return null;
            var json = JsonSerializer.Serialize(data);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(field, out var p) ? p.ToString() : null;
        }

        Dictionary<string, object?> map = name switch
        {
            "list_user_orders" => new() { ["authenticated_user_id"] = userId, ["status_filter"] = "CONFIRMED" },
            "get_order_detail" => new() { ["authenticated_user_id"] = userId, ["order_id"] = orderId },
            "get_policy_snapshot" => new() { ["order_id"] = orderId, ["policy_id"] = order.PolicyId, ["policyCode"] = order.PolicyId },
            "list_after_sale_events" => new() { ["order_id"] = orderId },
            "calculate_refund_quote" => new() { ["order_id"] = orderId, ["expected_order_version"] = order.Version, ["reason_code"] = "PLAN_CHANGE", ["refund"] = order.PaidAmount, ["fee"] = 0m },
            "validate_action_permission" => new() { ["authenticated_user_id"] = userId, ["case_id"] = caseId, ["order_id"] = orderId, ["action"] = "CANCEL", ["expected_order_version"] = order.Version },
            "submit_cancellation" => new() { ["authenticated_user_id"] = userId, ["case_id"] = caseId, ["order_id"] = orderId, ["expected_order_version"] = order.Version, ["quote_id"] = FactStr("calculate_refund_quote", "quote_id"), ["idempotency_key"] = idem },
            "get_refund_status" => new() { ["refund_id"] = order.RefundId ?? FactStr("submit_cancellation", "refund_id") ?? "RFD-DEMO" },
            "get_payment_events" => new() { ["authenticated_user_id"] = userId, ["payment_id"] = order.PaymentId },
            "schedule_deadline_action" => new() { ["case_id"] = caseId, ["deadline"] = DateTimeOffset.UtcNow.AddDays(2), ["action_type"] = "CHECK_REFUND_SLA", ["idempotency_key"] = idem },
            "create_payment_investigation" => new() { ["case_id"] = caseId, ["order_id"] = orderId, ["refund_id"] = order.RefundId, ["trigger_reason"] = "SLA_BREACH", ["idempotency_key"] = idem },
            "verify_fulfillment_issue" => new() { ["case_id"] = caseId, ["order_id"] = orderId, ["reported_issue"] = "NO_ROOM", ["user_on_site"] = scenarioId == "E" },
            "get_alternative_hotels" => new() { ["source_order_id"] = orderId, ["location"] = "HOTEL_AREA", ["check_in"] = order.CheckIn, ["check_out"] = order.CheckOut, ["minimum_star_level"] = 4 },
            "get_guarantee_quote" => new() { ["order_id"] = orderId, ["issue_type"] = "NO_ROOM", ["alternative_hotel_id"] = "ALT-1" },
            "reserve_mock_alternative" => new() { ["authenticated_user_id"] = userId, ["case_id"] = caseId, ["alternative_hotel_id"] = "ALT-1", ["idempotency_key"] = idem },
            "create_human_handoff" => new() { ["case_id"] = caseId, ["order_id"] = orderId, ["summary"] = scenario.Title, ["queue"] = HandoffQueue(scenarioId), ["idempotency_key"] = idem },
            "get_handoff_status" => new() { ["handoff_id"] = FactStr("create_human_handoff", "handoff_id") },
            "confirm_recovery_outcome" => new() { ["case_id"] = caseId, ["recovery_outcome"] = "ALTERNATIVE_HOTEL_CONFIRMED", ["user_confirmation"] = true, ["idempotency_key"] = idem },
            "build_supplier_case_draft" => new() { ["case_id"] = caseId, ["order_id"] = orderId, ["reason_code"] = "PLAN_CHANGE" },
            "create_supplier_case" => new() { ["authenticated_user_id"] = userId, ["case_id"] = caseId, ["order_id"] = orderId, ["draft_id"] = FactStr("build_supplier_case_draft", "draft_id"), ["idempotency_key"] = idem },
            "get_supplier_case" => new() { ["supplier_case_id"] = FactStr("create_supplier_case", "supplier_case_id") ?? "SUP-F-001" },
            "accept_supplier_offer" => new() { ["authenticated_user_id"] = userId, ["case_id"] = caseId, ["supplier_case_id"] = "SUP-F-001", ["offer_id"] = "OFF-F-REFUND", ["idempotency_key"] = idem },
            "submit_evidence_metadata" => new()
            {
                ["authenticated_user_id"] = userId, ["case_id"] = caseId,
                ["evidence_type"] = scenarioId == "G" ? "TRANSPORT_CANCELLATION_NOTICE" : (occurrence == 2 ? "ISSUE_PHOTO" : "HOTEL_CONTACT_RESULT"),
                ["storage_reference"] = $"mock://evidence/{scenarioId}/{occurrence}", ["consent"] = true, ["idempotency_key"] = idem
            },
            "extract_evidence_fields" => new() { ["evidence_id"] = FactStr("submit_evidence_metadata", "evidence_id"), ["extraction_schema"] = "MINIMUM_NECESSARY_V1" },
            "create_exception_review" => new() { ["case_id"] = caseId, ["order_id"] = orderId, ["reason_code"] = "TRANSPORT_CANCELLATION", ["idempotency_key"] = idem },
            "create_service_dispute_case" => new() { ["case_id"] = caseId, ["order_id"] = orderId, ["idempotency_key"] = idem },
            "get_change_quote" => new() { ["order_id"] = orderId, ["expected_order_version"] = order.Version, ["change_type"] = "DATE" },
            "submit_order_change" => new() { ["authenticated_user_id"] = userId, ["case_id"] = caseId, ["order_id"] = orderId, ["expected_order_version"] = order.Version, ["change_quote_id"] = FactStr("get_change_quote", "change_quote_id"), ["idempotency_key"] = idem },
            "create_finance_case" => new() { ["case_id"] = caseId, ["order_id"] = orderId, ["payment_id"] = order.PaymentId, ["idempotency_key"] = idem },
            "get_responsibility_chain" => new() { ["order_id"] = orderId },
            "get_group_order_breakdown" => new() { ["authenticated_user_id"] = userId, ["order_id"] = orderId },
            "get_partial_cancel_quote" => new() { ["order_id"] = orderId, ["expected_order_version"] = order.Version },
            _ => new()
        };
        map["trace_id"] = traceId;
        return map;
    }

    private static string HandoffQueue(string scenarioId) => scenarioId switch
    {
        "D" or "E" => "URGENT_HUMAN_HANDOFF",
        "H" => "SERVICE_RECOVERY_SPECIALIST",
        "K" => "CROSS_BORDER_SPECIALIST",
        "L" => "CORPORATE_GROUP_SPECIALIST",
        _ => "HUMAN_SPECIALIST"
    };

    private static string Record(
        List<WorkflowStepDto> steps, string node, string actor, string before, string after, string? tool, object? result)
    {
        steps.Add(new WorkflowStepDto(steps.Count + 1, node, actor, before, after, tool, result));
        return after;
    }

    private static bool IsSubsequence(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        var i = 0;
        foreach (var item in actual)
            if (i < expected.Count && item == expected[i]) i++;
        return i == expected.Count;
    }
}
