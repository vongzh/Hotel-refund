namespace Stayota.RefundAgent.Domain.Entities;

public sealed class HotelOrder
{
    public string OrderId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string HotelName { get; set; } = string.Empty;
    public DateOnly CheckIn { get; set; }
    public DateOnly CheckOut { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "CNY";
    public string Status { get; set; } = "confirmed";
    public bool Arrived { get; set; }
    public string CancelPolicyCode { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PolicySnapshot
{
    public string PolicyId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public bool FreeCancel { get; set; }
    public decimal? DeductionAmount { get; set; }
    public string Authority { get; set; } = "platform";
    public DateTimeOffset EffectiveAt { get; set; }
}

public sealed class RefundCase
{
    public string CaseId { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public ScenarioId ScenarioId { get; set; }
    public CaseStatus Status { get; set; }
    public RiskLevel RiskLevel { get; set; }
    public string? Intent { get; set; }
    public decimal? QuoteRefundAmount { get; set; }
    public decimal? QuoteFeeAmount { get; set; }
    public AgentAction RecommendedAction { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<CaseEvent> Events { get; set; } = [];
}

public sealed class CaseEvent
{
    public long Id { get; set; }
    public string CaseId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class WorkflowRun
{
    public string RunId { get; set; } = string.Empty;
    public string CaseId { get; set; } = string.Empty;
    public ScenarioId ScenarioId { get; set; }
    public string Status { get; set; } = "RUNNING";
    public string TraceJson { get; set; } = "[]";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class ToolAuditLog
{
    public long Id { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public ToolAccess Access { get; set; }
    public bool Allowed { get; set; }
    public string RequestJson { get; set; } = "{}";
    public string ResponseJson { get; set; } = "{}";
    public string? DenyReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ScenarioFixture
{
    public ScenarioId Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string EntryMessage { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public RiskLevel RiskLevel { get; set; }
    public string Goal { get; set; } = string.Empty;
}
