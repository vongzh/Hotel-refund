using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stayota.RefundAgent.Application.Ai;
using Stayota.RefundAgent.Application.Services;

namespace Stayota.RefundAgent.Infrastructure.Production;

/// <summary>
/// Reads orders/policies from local mock store (demo default).
/// </summary>
public sealed class MockProductionOrderClient(IRefundDataStore store) : IProductionOrderClient
{
    public string Mode => "Mock";

    public async Task<object?> GetOrderDetailAsync(string orderId, string userId, CancellationToken ct = default)
        => await store.GetOrderAsync(orderId, ct);

    public async Task<object?> ListUserOrdersAsync(string userId, CancellationToken ct = default)
        => new { orders = await store.ListOrdersAsync(userId, ct), source = "mock" };

    public async Task<object?> GetPolicySnapshotAsync(string policyId, string orderId, CancellationToken ct = default)
        => await store.GetPolicyAsync(policyId, ct);

    public Task<object?> GetRefundStatusAsync(string refundId, CancellationToken ct = default)
        => Task.FromResult<object?>(new
        {
            refund_id = refundId,
            status = "CHANNEL_PROCESSING",
            source = "mock",
            waiting_for = "PAYMENT_CHANNEL"
        });
}

/// <summary>
/// Calls an external HTTP production-like API when Production:Mode=Http.
/// Expected endpoints:
/// GET {BaseUrl}/orders/{orderId}?userId=
/// GET {BaseUrl}/users/{userId}/orders
/// GET {BaseUrl}/policies/{policyId}?orderId=
/// GET {BaseUrl}/refunds/{refundId}
/// </summary>
public sealed class HttpProductionOrderClient(
    IHttpClientFactory httpClientFactory,
    IOptions<ProductionOptions> options,
    IRefundDataStore fallback,
    ILogger<HttpProductionOrderClient> logger) : IProductionOrderClient
{
    public string Mode => "Http";

    public async Task<object?> GetOrderDetailAsync(string orderId, string userId, CancellationToken ct = default)
    {
        var result = await TryGetAsync($"orders/{Uri.EscapeDataString(orderId)}?userId={Uri.EscapeDataString(userId)}", ct);
        if (result is not null) return result;
        logger.LogWarning("Production HTTP get_order failed; falling back to mock for {OrderId}", orderId);
        return await fallback.GetOrderAsync(orderId, ct);
    }

    public async Task<object?> ListUserOrdersAsync(string userId, CancellationToken ct = default)
    {
        var result = await TryGetAsync($"users/{Uri.EscapeDataString(userId)}/orders", ct);
        if (result is not null) return result;
        return new { orders = await fallback.ListOrdersAsync(userId, ct), source = "mock-fallback" };
    }

    public async Task<object?> GetPolicySnapshotAsync(string policyId, string orderId, CancellationToken ct = default)
    {
        var result = await TryGetAsync(
            $"policies/{Uri.EscapeDataString(policyId)}?orderId={Uri.EscapeDataString(orderId)}", ct);
        if (result is not null) return result;
        return await fallback.GetPolicyAsync(policyId, ct);
    }

    public async Task<object?> GetRefundStatusAsync(string refundId, CancellationToken ct = default)
    {
        var result = await TryGetAsync($"refunds/{Uri.EscapeDataString(refundId)}", ct);
        if (result is not null) return result;
        return new { refund_id = refundId, status = "UNKNOWN", source = "mock-fallback" };
    }

    private async Task<object?> TryGetAsync(string path, CancellationToken ct)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.BaseUrl)) return null;
        try
        {
            var client = httpClientFactory.CreateClient("production");
            using var response = await client.GetAsync(path, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<object>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Production HTTP call failed for {Path}", path);
            return null;
        }
    }
}
