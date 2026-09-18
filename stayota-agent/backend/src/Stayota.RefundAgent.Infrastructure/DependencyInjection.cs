using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Stayota.RefundAgent.Application.Ai;
using Stayota.RefundAgent.Application.Contracts;
using Stayota.RefundAgent.Application.Services;
using Stayota.RefundAgent.Infrastructure.Ai;
using Stayota.RefundAgent.Infrastructure.Persistence;
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

        services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(pg));
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));

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
                // Fall back so API still boots if OpenAI/Ollama misconfigured.
                var logger = sp.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()
                    ?.CreateLogger("ChatClientRegistration");
                logger?.LogWarning(ex, "Falling back to DeterministicRefundChatClient");
                return new DeterministicRefundChatClient();
            }
        });
        services.AddScoped<IRefundAgentHost, RefundAgentHost>();

        services.AddScoped<IAgentOrchestrator, AgentOrchestrator>();
        services.AddScoped<IScenarioWorkflow, ScenarioWorkflow>();
        services.AddSingleton<IVerifier, Verifier>();
        services.AddScoped<IEvalRunner, EvalRunner>();
        services.AddSingleton<IConfirmationStore, RedisConfirmationStore>();
        services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
        services.AddSingleton<ISessionStore, RedisSessionStore>();
        return services;
    }
}
