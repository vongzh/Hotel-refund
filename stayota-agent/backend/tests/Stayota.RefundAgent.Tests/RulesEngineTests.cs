using Stayota.RefundAgent.Application.Contracts;
using Stayota.RefundAgent.Application.Services;
using Stayota.RefundAgent.Domain;
using Stayota.RefundAgent.Domain.Entities;
using Xunit;

namespace Stayota.RefundAgent.Tests;

public class RulesEngineTests
{
    private readonly RulesEngine _rules = new();

    private static ScenarioFixture Sc(string id, RiskLevel risk = RiskLevel.L1) => new()
    {
        ScenarioId = id,
        RiskLevel = risk,
        OrderId = $"ORD-{id}-001",
        CaseId = $"CASE-{id}-001",
        Title = id
    };

    [Fact]
    public void A_FreeCancel()
    {
        var r = _rules.Evaluate(new HotelOrder { PaidAmount = 688 }, new PolicySnapshot { RuleCode = "POL-A" }, Sc("A"), new AgentSignals(false, false, false, false, false));
        Assert.Equal("ConfirmCancel", r.Action);
        Assert.Equal(688, r.RefundAmount);
    }

    [Fact]
    public void G_WithoutEvidence_RequestsEvidence()
    {
        var r = _rules.Evaluate(new HotelOrder { PaidAmount = 520 }, new PolicySnapshot { RuleCode = "POL-G" }, Sc("G", RiskLevel.L2), new AgentSignals(false, false, false, false, false));
        Assert.Equal("RequestEvidence", r.Action);
    }

    [Fact]
    public void E_HumanHandoff()
    {
        var r = _rules.Evaluate(new HotelOrder { PaidAmount = 1180 }, new PolicySnapshot { RuleCode = "POL-E" }, Sc("E", RiskLevel.L3), new AgentSignals(false, false, false, false, false));
        Assert.Equal("HumanHandoff", r.Action);
        Assert.Equal(RiskLevel.L3, r.RiskLevel);
    }

    [Fact]
    public void L_BlocksAutoWritePath()
    {
        var r = _rules.Evaluate(new HotelOrder { PaidAmount = 8800, RoomCount = 8 }, new PolicySnapshot { RuleCode = "POL-L" }, Sc("L", RiskLevel.L3), new AgentSignals(false, false, false, false, false));
        Assert.Equal("HumanHandoff", r.Action);
        Assert.Equal("AWAITING_SPECIALIST", r.CaseStatus);
    }
}

public class ScenarioRouterTests
{
    private readonly ScenarioRouter _router = new();

    [Theory]
    [InlineData("帮我把明天去杭州的酒店免费取消。", "A")]
    [InlineData("今天不去了，现在取消要扣多少？", "B")]
    [InlineData("退款已经提交三天了，怎么还没有到账？", "C")]
    [InlineData("我已经到前台了，但是酒店说没有房间。", "E")]
    [InlineData("航班取消了，可以凭证明申请退款吗？", "G")]
    [InlineData("公司订了八间房，只想部分取消并处理发票。", "L")]
    public void RoutesExpected(string message, string expected) =>
        Assert.Equal(expected, _router.Route(message, null));
}
