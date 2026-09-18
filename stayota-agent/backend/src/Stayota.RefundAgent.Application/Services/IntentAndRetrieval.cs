using Stayota.RefundAgent.Application.Contracts;
using Stayota.RefundAgent.Domain.Entities;

namespace Stayota.RefundAgent.Application.Services;

public sealed class IntentService : IIntentService
{
    public (string Intent, string Reason, double Confidence, Dictionary<string, string> Slots) Analyze(
        string message, ScenarioFixture scenario, bool lowConfidence)
    {
        if (lowConfidence)
        {
            return ("意图不明确", "unclear", 0.42, new Dictionary<string, string>
            {
                ["order_id"] = scenario.OrderId,
                ["missing"] = "具体诉求"
            });
        }

        var (intent, reason, confidence) = scenario.ScenarioId switch
        {
            "A" => ("申请退款", "free_cancel", 0.94),
            "B" => ("查询扣费并取消", "deducted_cancel", 0.93),
            "C" => ("查询退款进度", "refund_progress", 0.95),
            "D" => ("履约异常求助", "prearrival_no_room", 0.96),
            "E" => ("到店无房紧急求助", "onsite_no_room", 0.97),
            "F" => ("申请例外协商", "non_refundable", 0.92),
            "G" => ("特殊原因退款", "flight_cancelled", 0.93),
            "H" => ("服务争议", "service_dispute", 0.91),
            "I" => ("订单变更", "order_change", 0.92),
            "J" => ("支付异常核查", "payment_anomaly", 0.9),
            "K" => ("责任主体查询", "cross_border", 0.9),
            "L" => ("团体部分取消", "group_partial", 0.91),
            _ => ("申请退款", "general", 0.8)
        };

        return (intent, reason, confidence, new Dictionary<string, string>
        {
            ["order_id"] = scenario.OrderId,
            ["scenario"] = scenario.ScenarioId,
            ["refund_reason"] = reason,
            ["message_preview"] = message.Length > 40 ? message[..40] : message
        });
    }
}

public sealed class PolicyRetrieval : IPolicyRetrieval
{
    public IReadOnlyList<PolicyMatchDto> Retrieve(HotelOrder order, PolicySnapshot policy, string reason)
    {
        var primary = new PolicyMatchDto(policy.PolicyId, policy.Title, reason.Contains("flight") ? 0.9 : 0.86, policy.Summary);
        var secondary = new PolicyMatchDto("POL-GENERIC-AUTHORITY", "成交政策快照优先于聊天记忆", 0.71,
            "金额与权限以订单成交时政策快照为准，不得由模型改写。");
        var tertiary = new PolicyMatchDto("POL-HITL-BOUNDARY", "高风险必须人工接管", 0.66,
            "L3 场景禁止自动写资金动作，需携带上下文升级。");
        return [primary, secondary, tertiary];
    }
}

public sealed class ScenarioRouter
{
    public string Route(string message, string? explicitScenario)
    {
        if (!string.IsNullOrWhiteSpace(explicitScenario))
            return explicitScenario!.Trim().ToUpperInvariant();

        var text = message ?? "";
        if (ContainsAny(text, "八间", "团体", "企业多房", "部分取消", "发票")) return "L";
        if (ContainsAny(text, "海外", "跨境", "代理又让", "谁负责")) return "K";
        if (ContainsAny(text, "扣了两次", "预授权", "押金", "重复扣款")) return "J";
        if (ContainsAny(text, "订错", "改日期", "改房型", "改名")) return "I";
        if (ContainsAny(text, "图片不一样", "很脏", "描述不符", "卫生")) return "H";
        if (ContainsAny(text, "航班取消", "疾病", "灾害", "证明免费退", "医院证明")) return "G";
        if (ContainsAny(text, "不能退", "不可取消", "争取")) return "F";
        if (ContainsAny(text, "已经在前台", "已经到前台", "到店无房", "无法入住，今晚")) return "E";
        if (ContainsAny(text, "没房", "加价", "加 300", "加300", "加价三百")) return "D";
        if (ContainsAny(text, "到账", "退款进度", "还没收到", "钱没到账")) return "C";
        if (ContainsAny(text, "扣多少", "首晚", "取消费")) return "B";
        if (ContainsAny(text, "免费取消", "取消明天", "杭州")) return "A";
        return "A";
    }

    private static bool ContainsAny(string text, params string[] keys) =>
        keys.Any(k => text.Contains(k, StringComparison.Ordinal));
}
