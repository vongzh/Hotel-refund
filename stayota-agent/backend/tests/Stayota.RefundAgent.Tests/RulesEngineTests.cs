using Stayota.RefundAgent.Application.Services;
using Stayota.RefundAgent.Domain;
using Xunit;

namespace Stayota.RefundAgent.Tests;

public class RulesEngineTests
{
    private readonly RulesEngine _rules = new();

    [Fact]
    public void FreeCancellation_ReturnsConfirmCancel()
    {
        var order = new Domain.Entities.HotelOrder { Amount = 688m, CancelPolicyCode = "FREE-CANCEL" };
        var policy = new Domain.Entities.PolicySnapshot { Code = "FREE-CANCEL", FreeCancel = true };
        var result = _rules.Evaluate(order, policy, ScenarioId.FreeCancellation, false);
        Assert.Equal(AgentAction.ConfirmCancel, result.Action);
        Assert.Equal(688m, result.RefundAmount);
        Assert.Equal(0m, result.FeeAmount);
    }

    [Fact]
    public void FlightCancelled_WithoutEvidence_RequestsEvidence()
    {
        var order = new Domain.Entities.HotelOrder { Amount = 488m };
        var policy = new Domain.Entities.PolicySnapshot { Code = "HTL-REFUND-006" };
        var result = _rules.Evaluate(order, policy, ScenarioId.FlightCancelled, false);
        Assert.Equal(AgentAction.RequestEvidence, result.Action);
        Assert.True(result.NeedsEvidence);
    }

    [Fact]
    public void NoRoom_EscalatesToHuman()
    {
        var order = new Domain.Entities.HotelOrder { Amount = 760m };
        var policy = new Domain.Entities.PolicySnapshot { Code = "NON-REFUNDABLE" };
        var result = _rules.Evaluate(order, policy, ScenarioId.NoRoomOnArrival, false);
        Assert.Equal(AgentAction.HumanHandoff, result.Action);
        Assert.Equal(RiskLevel.L3, result.RiskLevel);
    }
}
