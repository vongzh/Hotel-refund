using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Stayota.RefundAgent.Application.Ai;
using Stayota.RefundAgent.Application.Contracts;
using Stayota.RefundAgent.Application.Services;
using Stayota.RefundAgent.Infrastructure.Ai;
using Stayota.RefundAgent.Infrastructure.Mcp;
using Stayota.RefundAgent.Infrastructure.Persistence;
using Stayota.RefundAgent.Infrastructure.Production;
using Stayota.RefundAgent.Infrastructure.Redis;

namespace Stayota.RefundAgent.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRefundAgentInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var pg = configuration.GetConnectionString("Postgres")
                 ?? "Host=127.0.0.1;Port=5432;Database=stayota_refund;Username=stayota;Password=stayota";
        var redis = configuration.GetConnectionString("Redis") ?? "127.0.0.1:6379";

        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.Configure<ProductionOptions>(configuration.GetSection(ProductionOptions.SectionName));

        services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(pg));
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));
        services.AddHttpClient("production", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<ProductionOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
                client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
            if (!string.IsNullOrWhiteSpace(opts.ApiKey))
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opts.ApiKey);
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddScoped<IRefundDataStore, RefundDataStore>();
        services.AddScoped<IScenarioCatalog, ScenarioCatalog>();
        services.AddSingleton<IIntentService, IntentService>();
        services.AddSingleton<IPolicyRetrieval, PolicyRetrieval>();
        services.AddScoped<IRulesEngine, RulesEngine>();
        services.AddScoped<IToolGateway, ToolGateway>();
        services.AddScoped<IRefundAiToolCatalog, RefundAiToolCatalog>();

        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
        services.AddSingleton<IChatClient>(sp =>
        {
            try
            {
                return sp.GetRequiredService<IChatClientFactory>().Create();
            }
            catch (Exception ex)
            {
                var logger = sp.GetService<ILoggerFactory>()?.CreateLogger("ChatClientRegistration");
                logger?.LogWarning(ex, "Falling back to DeterministicRefundChatClient");
                return new DeterministicRefundChatClient();
            }
        });
        services.AddScoped<IRefundAgentHost, RefundAgentHost>();
        services.AddScoped<IAgentConversationService, AgentConversationService>();
        services.AddSingleton<IAgentSessionStore, RedisAgentSessionStore>();

        services.AddScoped<MockProductionOrderClient>();
        services.AddScoped<HttpProductionOrderClient>();
        services.AddScoped<IExternalMcpToolSource, ExternalMcpToolSource>();
        services.AddScoped<IProductionOrderClient>(sp =>
        {
            var mode = sp.GetRequiredService<IOptions<ProductionOptions>>().Value.Mode;
            return mode.ToLowerInvariant() switch
            {
                "http" => sp.GetRequiredService<HttpProductionOrderClient>(),
                "mcp" => new LabeledProductionOrderClient("Mcp", sp.GetRequiredService<MockProductionOrderClient>()),
                _ => sp.GetRequiredService<MockProductionOrderClient>()
            };
        });

        services.AddScoped<IAgentOrchestrator, AgentOrchestrator>();
        services.AddScoped<IScenarioWorkflow, ScenarioWorkflow>();
        services.AddSingleton<IVerifier, Verifier>();
        services.AddScoped<IEvalRunner, EvalRunner>();
        services.AddSingleton<IConfirmationStore, RedisConfirmationStore>();
        services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
        services.AddSingleton<ISessionStore, RedisSessionStore>();

        services.AddMcpServer()
            .WithHttpTransport()
            .WithTools<RefundMcpTools>();

        return services;
    }
}

/// <summary>Wraps an order client while advertising a configured production mode label.</summary>
file sealed class LabeledProductionOrderClient(string mode, IProductionOrderClient inner) : IProductionOrderClient
{
    public string Mode => mode;
    public Task<object?> GetOrderDetailAsync(string orderId, string userId, CancellationToken ct = default)
        => inner.GetOrderDetailAsync(orderId, userId, ct);
    public Task<object?> ListUserOrdersAsync(string userId, CancellationToken ct = default)
        => inner.ListUserOrdersAsync(userId, ct);
    public Task<object?> GetPolicySnapshotAsync(string policyId, string orderId, CancellationToken ct = default)
        => inner.GetPolicySnapshotAsync(policyId, orderId, ct);
    public Task<object?> GetRefundStatusAsync(string refundId, CancellationToken ct = default)
        => inner.GetRefundStatusAsync(refundId, ct);
}
