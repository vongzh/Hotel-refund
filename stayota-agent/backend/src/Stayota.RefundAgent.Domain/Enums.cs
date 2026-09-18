namespace Stayota.RefundAgent.Domain;

public enum ScenarioId
{
    FreeCancellation = 1,
    NonCancellable = 2,
    NoRoomOnArrival = 3,
    FlightCancelled = 4,
    RefundProgress = 5,
    DeductedCancel = 6,
    PaymentAnomaly = 7
}

public enum RiskLevel
{
    L1 = 1,
    L2 = 2,
    L3 = 3
}

public enum CaseStatus
{
    Open = 1,
    WaitingUserConfirm = 2,
    WaitingEvidence = 3,
    WaitingSupplier = 4,
    WaitingPayment = 5,
    Escalated = 6,
    Completed = 7,
    Rejected = 8
}

public enum ToolAccess
{
    Read = 1,
    Write = 2
}

public enum AgentAction
{
    AutoRefund = 1,
    RequestEvidence = 2,
    NegotiateWithHotel = 3,
    HumanHandoff = 4,
    ExplainProgress = 5,
    ConfirmCancel = 6
}
