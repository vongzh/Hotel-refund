using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Stayota.RefundAgent.Application.Contracts;
using Stayota.RefundAgent.Application.Services;
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

        services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(pg));
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));

        services.AddScoped<IRefundDataStore, RefundDataStore>();
        services.AddScoped<IScenarioCatalog, ScenarioCatalog>();
        services.AddSingleton<IIntentService, IntentService>();
        services.AddSingleton<IPolicyRetrieval, PolicyRetrieval>();
        services.AddScoped<IRulesEngine, RulesEngine>();
        services.AddScoped<IToolGateway, ToolGateway>();
        services.AddScoped<IAgentOrchestrator, AgentOrchestrator>();
        services.AddScoped<IEvalRunner, EvalRunner>();
        services.AddSingleton<IConfirmationStore, RedisConfirmationStore>();
        services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
        services.AddSingleton<ISessionStore, RedisSessionStore>();
        return services;
    }
}
