using Stayota.RefundAgent.Application.Contracts;
using Stayota.RefundAgent.Domain;
using Stayota.RefundAgent.Domain.Entities;

namespace Stayota.RefundAgent.Application.Services;

public sealed class RulesEngine : IRulesEngine
{
    public RuleDecision Evaluate(HotelOrder order, PolicySnapshot policy, ScenarioId scenario, bool hasEvidence)
    {
        return scenario switch
        {
            ScenarioId.FreeCancellation => FreeCancel(order, policy),
            ScenarioId.DeductedCancel => Deducted(order),
            ScenarioId.NonCancellable => NonCancel(order, hasEvidence),
            ScenarioId.FlightCancelled => Flight(order, hasEvidence),
            ScenarioId.NoRoomOnArrival => NoRoom(order),
            ScenarioId.RefundProgress => Progress(order),
            ScenarioId.PaymentAnomaly => Payment(order),
            _ => FreeCancel(order, policy)
        };
    }

    private static RuleDecision FreeCancel(HotelOrder order, PolicySnapshot policy) => new(
        AgentAction.ConfirmCancel,
        RiskLevel.L1,
        18,
        order.Amount,
        0m,
        "可以免费取消",
        "免费取消并原路退款",
        $"确认后立即取消订单，预计退回 ¥{order.Amount:0.##}，不收取消费。",
        policy.Code,
        NeedsUserConfirm: true,
        NeedsEvidence: false);

    private static RuleDecision Deducted(HotelOrder order)
    {
        var fee = Math.Round(order.Amount / 2m, 2);
        var refund = order.Amount - fee;
        return new(
            AgentAction.ConfirmCancel,
            RiskLevel.L1,
            28,
            refund,
            fee,
            "可以取消，但会扣除部分房费",
            $"接受 ¥{fee:0.##} 取消费并退款",
            $"确认后取消订单，扣除 ¥{fee:0.##}，预计退回 ¥{refund:0.##}。",
            "POLICY-DEDUCT-HALF",
            true,
            false);
    }

    private static RuleDecision NonCancel(HotelOrder order, bool hasEvidence)
    {
        if (!hasEvidence)
        {
            return new(
                AgentAction.RequestEvidence,
                RiskLevel.L2,
                55,
                0m,
                0m,
                "订单为不可取消，需要补充特殊原因材料后才能协商",
                "补充材料后发起例外协商",
                "请先提供无法入住的证明材料，平台再代为向酒店协商。",
                "POLICY-NON-REFUNDABLE",
                false,
                true);
        }

        return new(
            AgentAction.NegotiateWithHotel,
            RiskLevel.L2,
            62,
            0m,
            0m,
            "不能直接退款，可以替你发起酒店协商",
            "申请例外退款协商",
            "平台代你向酒店争取部分退款或改期，不承诺一定成功。",
            "POLICY-EXCEPTION-NEGOTIATE",
            true,
            false);
    }

    private static RuleDecision Flight(HotelOrder order, bool hasEvidence)
    {
        if (!hasEvidence)
        {
            return new(
                AgentAction.RequestEvidence,
                RiskLevel.L2,
                58,
                0m,
                0m,
                "航班取消可作为特殊原因，但需先上传取消证明",
                "补充航班取消证明",
                "材料齐全后将进入酒店协商路径，审核通过前不承诺全额退款。",
                "HTL-REFUND-006",
                false,
                true);
        }

        return new(
            AgentAction.NegotiateWithHotel,
            RiskLevel.L2,
            64,
            0m,
            0m,
            "材料已接收，将发起酒店协商工单",
            "提交航班取消例外协商",
            "已创建 P2 协商工单，预计下个工作日首次回复。",
            "HTL-REFUND-006",
            false,
            false);
    }

    private static RuleDecision NoRoom(HotelOrder order) => new(
        AgentAction.HumanHandoff,
        RiskLevel.L3,
        88,
        order.Amount,
        0m,
        "到店无房属于高优先级履约异常，先安排今晚住宿",
        "立即转紧急专员",
        "优先恢复住宿，退款与责任认定在安顿后继续处理。",
        "FULFILLMENT-NO-ROOM",
        false,
        false);

    private static RuleDecision Progress(HotelOrder order) => new(
        AgentAction.ExplainProgress,
        RiskLevel.L1,
        22,
        order.Amount,
        0m,
        "退款已发起，资金仍在渠道处理中",
        "继续等待原路退款",
        "无需重复申请，超时未到账将自动创建支付调查。",
        "REFUND-IN-FLIGHT",
        false,
        false);

    private static RuleDecision Payment(HotelOrder order) => new(
        AgentAction.HumanHandoff,
        RiskLevel.L3,
        76,
        0m,
        0m,
        "检测到支付异常，需财务专席核验实扣与预授权",
        "创建财务核验工单",
        "核验完成前不重复退款，预计下一工作日给出结论。",
        "PAYMENT-ANOMALY",
        false,
        false);
}
