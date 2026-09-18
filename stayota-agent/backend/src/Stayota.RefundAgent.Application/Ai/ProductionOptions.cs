using Stayota.RefundAgent.Domain;

namespace Stayota.RefundAgent.Application.Ai;

public sealed class ProductionOptions
{
    public const string SectionName = "Production";

    /// <summary>Mock | Http | Mcp</summary>
    public string Mode { get; set; } = "Mock";

    /// <summary>HTTP base URL for production-like order/payment APIs.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>External MCP endpoint (SSE/HTTP) to pull production tools from.</summary>
    public string McpEndpoint { get; set; } = "";

    public string ApiKey { get; set; } = "";
}

public interface IProductionOrderClient
{
    string Mode { get; }
    Task<object?> GetOrderDetailAsync(string orderId, string userId, CancellationToken ct = default);
    Task<object?> ListUserOrdersAsync(string userId, CancellationToken ct = default);
    Task<object?> GetPolicySnapshotAsync(string policyId, string orderId, CancellationToken ct = default);
    Task<object?> GetRefundStatusAsync(string refundId, CancellationToken ct = default);
}

public interface IExternalMcpToolSource
{
    Task<IReadOnlyList<Microsoft.Extensions.AI.AITool>> ListToolsAsync(CancellationToken ct = default);
}
