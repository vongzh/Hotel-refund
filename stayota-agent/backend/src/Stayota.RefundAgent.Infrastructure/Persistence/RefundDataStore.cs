using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Stayota.RefundAgent.Application.Services;
using Stayota.RefundAgent.Domain;
using Stayota.RefundAgent.Domain.Entities;
using Stayota.RefundAgent.Infrastructure.Persistence;

namespace Stayota.RefundAgent.Infrastructure.Persistence;

public sealed class RefundDataStore(AppDbContext db) : IRefundDataStore
{
    private static readonly object Gate = new();
    private static bool _seeded;

    public async Task EnsureSeededAsync(CancellationToken ct = default)
    {
        if (_seeded && await db.Scenarios.AnyAsync(ct)) return;
        lock (Gate)
        {
            if (_seeded) return;
            SeedSync();
            _seeded = true;
        }
        await Task.CompletedTask;
    }

    public async Task ResetDemoAsync(CancellationToken ct = default)
    {
        db.ToolAudits.RemoveRange(db.ToolAudits);
        db.CaseEvents.RemoveRange(db.CaseEvents);
        db.WorkflowRuns.RemoveRange(db.WorkflowRuns);
        db.Cases.RemoveRange(db.Cases);
        db.Orders.RemoveRange(db.Orders);
        db.Policies.RemoveRange(db.Policies);
        db.Scenarios.RemoveRange(db.Scenarios);
        await db.SaveChangesAsync(ct);
        _seeded = false;
        await EnsureSeededAsync(ct);
    }

    public ScenarioFixture GetScenario(string code) =>
        db.Scenarios.AsNoTracking().First(x => x.Code == code);

    public IReadOnlyList<ScenarioFixture> ListScenarios() =>
        db.Scenarios.AsNoTracking().OrderBy(x => x.Id).ToList();

    public Task<HotelOrder?> GetOrderAsync(string orderId, CancellationToken ct = default) =>
        db.Orders.AsNoTracking().FirstOrDefaultAsync(x => x.OrderId == orderId, ct);

    public Task<PolicySnapshot?> GetPolicyAsync(string code, CancellationToken ct = default) =>
        db.Policies.AsNoTracking().FirstOrDefaultAsync(x => x.Code == code, ct);

    public async Task<RefundCase> UpsertCaseAsync(RefundCase refundCase, CancellationToken ct = default)
    {
        var existing = await db.Cases.FirstOrDefaultAsync(x => x.CaseId == refundCase.CaseId, ct);
        if (existing is null)
        {
            db.Cases.Add(refundCase);
        }
        else
        {
            existing.Status = refundCase.Status;
            existing.RiskLevel = refundCase.RiskLevel;
            existing.Intent = refundCase.Intent;
            existing.QuoteRefundAmount = refundCase.QuoteRefundAmount;
            existing.QuoteFeeAmount = refundCase.QuoteFeeAmount;
            existing.RecommendedAction = refundCase.RecommendedAction;
            existing.UpdatedAt = refundCase.UpdatedAt;
        }
        await db.SaveChangesAsync(ct);
        return refundCase;
    }

    public async Task AppendEventAsync(string caseId, string eventType, object payload, CancellationToken ct = default)
    {
        db.CaseEvents.Add(new CaseEvent
        {
            CaseId = caseId,
            EventType = eventType,
            PayloadJson = JsonSerializer.Serialize(payload),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task SaveWorkflowRunAsync(WorkflowRun run, CancellationToken ct = default)
    {
        db.WorkflowRuns.Add(run);
        await db.SaveChangesAsync(ct);
    }

    public async Task SaveToolAuditAsync(ToolAuditLog log, CancellationToken ct = default)
    {
        db.ToolAudits.Add(log);
        await db.SaveChangesAsync(ct);
    }

    private void SeedSync()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        db.Policies.AddRange(
            new PolicySnapshot
            {
                PolicyId = "P1", Code = "FREE-CANCEL", Title = "入住前免费取消",
                Summary = "入住日 18:00 前可免费取消", FreeCancel = true,
                Authority = "platform", EffectiveAt = DateTimeOffset.UtcNow.AddYears(-1)
            },
            new PolicySnapshot
            {
                PolicyId = "P2", Code = "DEDUCT-HALF", Title = "阶梯扣费取消",
                Summary = "取消扣除约 50% 房费", FreeCancel = false, DeductionAmount = null,
                Authority = "platform", EffectiveAt = DateTimeOffset.UtcNow.AddYears(-1)
            },
            new PolicySnapshot
            {
                PolicyId = "P3", Code = "NON-REFUNDABLE", Title = "不可取消",
                Summary = "售出不可取消，特殊原因可协商", FreeCancel = false,
                Authority = "hotel", EffectiveAt = DateTimeOffset.UtcNow.AddYears(-1)
            },
            new PolicySnapshot
            {
                PolicyId = "P4", Code = "HTL-REFUND-006", Title = "航班取消例外",
                Summary = "航班取消需证明后进入协商", FreeCancel = false,
                Authority = "platform", EffectiveAt = DateTimeOffset.UtcNow.AddYears(-1)
            });

        db.Orders.AddRange(
            new HotelOrder
            {
                OrderId = "SO20260918001", UserId = "u_demo", HotelName = "杭州湖畔演示酒店",
                CheckIn = today.AddDays(1), CheckOut = today.AddDays(2), Amount = 688m,
                Status = "confirmed", Arrived = false, CancelPolicyCode = "FREE-CANCEL", Version = 1,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new HotelOrder
            {
                OrderId = "SO20260918002", UserId = "u_demo", HotelName = "上海外滩演示酒店",
                CheckIn = today, CheckOut = today.AddDays(1), Amount = 1200m,
                Status = "confirmed", Arrived = false, CancelPolicyCode = "DEDUCT-HALF", Version = 1,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new HotelOrder
            {
                OrderId = "SO20260918003", UserId = "u_demo", HotelName = "北京国贸演示酒店",
                CheckIn = today, CheckOut = today.AddDays(2), Amount = 1280m,
                Status = "confirmed", Arrived = false, CancelPolicyCode = "NON-REFUNDABLE", Version = 1,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new HotelOrder
            {
                OrderId = "SO20260918004", UserId = "u_demo", HotelName = "南城悦居酒店（演示）",
                CheckIn = today, CheckOut = today.AddDays(1), Amount = 488m,
                Status = "confirmed", Arrived = false, CancelPolicyCode = "HTL-REFUND-006", Version = 1,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new HotelOrder
            {
                OrderId = "SO20260918005", UserId = "u_demo", HotelName = "广州塔景演示酒店",
                CheckIn = today, CheckOut = today.AddDays(1), Amount = 760m,
                Status = "confirmed", Arrived = true, CancelPolicyCode = "NON-REFUNDABLE", Version = 1,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new HotelOrder
            {
                OrderId = "SO20260918006", UserId = "u_demo", HotelName = "成都宽窄演示酒店",
                CheckIn = today.AddDays(-3), CheckOut = today.AddDays(-2), Amount = 860m,
                Status = "refunding", Arrived = false, CancelPolicyCode = "FREE-CANCEL", Version = 2,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new HotelOrder
            {
                OrderId = "SO20260918007", UserId = "u_demo", HotelName = "深圳湾演示酒店",
                CheckIn = today.AddDays(2), CheckOut = today.AddDays(3), Amount = 1600m,
                Status = "confirmed", Arrived = false, CancelPolicyCode = "DEDUCT-HALF", Version = 1,
                CreatedAt = DateTimeOffset.UtcNow
            });

        db.Scenarios.AddRange(
            new ScenarioFixture
            {
                Id = ScenarioId.FreeCancellation, Code = "free-cancellation", Name = "免费取消",
                Group = "取消与变更", OrderId = "SO20260918001", RiskLevel = RiskLevel.L1,
                EntryMessage = "帮我取消明天去杭州的酒店。", Goal = "验证确定性退款"
            },
            new ScenarioFixture
            {
                Id = ScenarioId.DeductedCancel, Code = "deducted-cancel", Name = "扣费取消",
                Group = "取消与变更", OrderId = "SO20260918002", RiskLevel = RiskLevel.L1,
                EntryMessage = "今天不去了，取消要扣多少钱？", Goal = "验证损失透明"
            },
            new ScenarioFixture
            {
                Id = ScenarioId.NonCancellable, Code = "non-cancellable", Name = "不可取消",
                Group = "取消与变更", OrderId = "SO20260918003", RiskLevel = RiskLevel.L2,
                EntryMessage = "临时有事去不了了，酒店说不能退，能帮我争取吗？", Goal = "验证例外协商"
            },
            new ScenarioFixture
            {
                Id = ScenarioId.FlightCancelled, Code = "flight-cancelled", Name = "航班取消",
                Group = "特殊审核", OrderId = "SO20260918004", RiskLevel = RiskLevel.L2,
                EntryMessage = "航班突然取消了，今晚肯定赶不到酒店，我想申请退款。", Goal = "验证材料与协商"
            },
            new ScenarioFixture
            {
                Id = ScenarioId.NoRoomOnArrival, Code = "no-room", Name = "到店无房",
                Group = "履约异常", OrderId = "SO20260918005", RiskLevel = RiskLevel.L3,
                EntryMessage = "我已经在前台了，他们说没有我的房间。", Goal = "验证紧急人工"
            },
            new ScenarioFixture
            {
                Id = ScenarioId.RefundProgress, Code = "refund-progress", Name = "退款进度",
                Group = "退款与支付", OrderId = "SO20260918006", RiskLevel = RiskLevel.L1,
                EntryMessage = "三天前说退了，怎么还没收到？", Goal = "验证预期管理"
            },
            new ScenarioFixture
            {
                Id = ScenarioId.PaymentAnomaly, Code = "payment-anomaly", Name = "支付异常",
                Group = "退款与支付", OrderId = "SO20260918007", RiskLevel = RiskLevel.L3,
                EntryMessage = "同一笔房费扣了两次，押金也没退。", Goal = "验证支付语义"
            });

        db.SaveChanges();
    }
}
