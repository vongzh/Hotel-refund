using Stayota.RefundAgent.Domain;
using Stayota.RefundAgent.Domain.Entities;

namespace Stayota.RefundAgent.Application.Contracts;

public sealed record AgentMessageRequest(
    string Message,
    string? ScenarioCode = null,
    string? UserId = null,
    bool HasEvidence = false,
    bool ResetDemo = false);

public sealed record DecisionStepDto(
    string Step,
    string Status,
    string Detail,
    double? Score = null);

public sealed record AgentDecisionDto(
    string TraceId,
    string RunId,
    string CaseId,
    string ScenarioCode,
    string Intent,
    double IntentConfidence,
    RiskLevel RiskLevel,
    int RiskScore,
    AgentAction Action,
    string Conclusion,
    string PlanTitle,
    string PlanCopy,
    decimal? RefundAmount,
    decimal? FeeAmount,
    string Reply,
    IReadOnlyList<DecisionStepDto> Steps,
    IReadOnlyDictionary<string, string> Slots,
    HotelOrderDto Order);

public sealed record HotelOrderDto(
    string OrderId,
    string HotelName,
    DateOnly CheckIn,
    DateOnly CheckOut,
    decimal Amount,
    string Status,
    bool Arrived,
    string CancelPolicyCode,
    int Version);

public sealed record ScenarioDto(
    string Code,
    string Name,
    string Group,
    string Goal,
    string EntryMessage,
    RiskLevel RiskLevel);

public sealed record ConfirmActionRequest(
    string CaseId,
    string OrderId,
    int OrderVersion,
    string Action,
    string IdempotencyKey);

public sealed record ConfirmActionResponse(
    bool Success,
    string Message,
    string? ConfirmationToken = null);

public interface IAgentOrchestrator
{
    Task<AgentDecisionDto> HandleAsync(AgentMessageRequest request, CancellationToken ct = default);
}

public interface IScenarioCatalog
{
    IReadOnlyList<ScenarioDto> List();
    ScenarioFixture GetRequired(string code);
}

public interface IRulesEngine
{
    RuleDecision Evaluate(HotelOrder order, PolicySnapshot policy, ScenarioId scenario, bool hasEvidence);
}

public sealed record RuleDecision(
    AgentAction Action,
    RiskLevel RiskLevel,
    int RiskScore,
    decimal RefundAmount,
    decimal FeeAmount,
    string Conclusion,
    string PlanTitle,
    string PlanCopy,
    string RuleCode,
    bool NeedsUserConfirm,
    bool NeedsEvidence);

public interface IToolGateway
{
    Task<ToolResult> InvokeAsync(ToolCall call, CancellationToken ct = default);
}

public sealed record ToolCall(
    string TraceId,
    string ToolName,
    ToolAccess Access,
    string UserId,
    string? OrderId,
    string? CaseId,
    RiskLevel RiskLevel,
    IDictionary<string, object?> Arguments,
    string? ConfirmationToken = null,
    string? IdempotencyKey = null,
    int? ExpectedOrderVersion = null);

public sealed record ToolResult(
    bool Allowed,
    bool Success,
    string ToolName,
    object? Data,
    string? DenyReason = null);

public interface IConfirmationStore
{
    Task<string> IssueAsync(string caseId, string orderId, int version, string action, TimeSpan ttl, CancellationToken ct = default);
    Task<bool> ConsumeAsync(string token, string caseId, string orderId, int version, string action, CancellationToken ct = default);
}

public interface IIdempotencyStore
{
    Task<bool> TryBeginAsync(string key, TimeSpan ttl, CancellationToken ct = default);
}

public interface ISessionStore
{
    Task SetAsync(string sessionId, string json, TimeSpan ttl, CancellationToken ct = default);
    Task<string?> GetAsync(string sessionId, CancellationToken ct = default);
}
