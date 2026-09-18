using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stayota.RefundAgent.Api.Security;
using Stayota.RefundAgent.Application.Ai;
using Stayota.RefundAgent.Application.Contracts;
using Stayota.RefundAgent.Infrastructure;
using Stayota.RefundAgent.Infrastructure.Persistence;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

var agentRoot = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "../../.."));
Environment.SetEnvironmentVariable("STAYOTA_AGENT_ROOT", agentRoot);

// Production profile defaults: demo off unless explicitly enabled.
if (builder.Environment.IsProduction())
{
    builder.Services.PostConfigure<HostingOptions>(o =>
    {
        if (!builder.Configuration.GetSection(HostingOptions.SectionName).Exists())
        {
            o.DemoEnabled = false;
            o.ResetDatabaseOnStartup = false;
            o.AllowDeterministicFallback = false;
            o.ExposeDetailedHealth = false;
        }
    });
}

builder.Services.AddRefundAgentInfrastructure(builder.Configuration);
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var hostingPreview = builder.Configuration.GetSection(HostingOptions.SectionName).Get<HostingOptions>() ?? new HostingOptions();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    p.AllowAnyHeader().AllowAnyMethod();
    if (hostingPreview.DemoEnabled && hostingPreview.AllowedOrigins.Length == 0)
        p.AllowAnyOrigin();
    else if (hostingPreview.AllowedOrigins.Length > 0)
        p.WithOrigins(hostingPreview.AllowedOrigins).AllowCredentials();
    else
        p.WithOrigins("http://127.0.0.1:5173", "http://localhost:5173");
}));

var app = builder.Build();
var hosting = app.Services.GetRequiredService<IOptions<HostingOptions>>().Value;

if (!string.IsNullOrWhiteSpace(hosting.PathBase))
{
    app.UsePathBase(hosting.PathBase.TrimEnd('/'));
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (hosting.ResetDatabaseOnStartup)
    {
        if (!hosting.DemoEnabled)
            throw new InvalidOperationException("Hosting:ResetDatabaseOnStartup requires DemoEnabled=true");
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }
    else
    {
        // Formal path: create schema if missing (migrate when migrations are added).
        await db.Database.EnsureCreatedAsync();
    }

    if (hosting.SeedOnStartup)
    {
        var store = scope.ServiceProvider.GetRequiredService<Stayota.RefundAgent.Application.Services.IRefundDataStore>();
        await store.EnsureSeededAsync();
    }
}

if (hosting.DemoEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseMiddleware<ApiKeyMiddleware>();
app.MapControllers();
app.MapMcp("/mcp");

app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));

app.MapGet("/health/ready", async (AppDbContext db, IConnectionMultiplexer redis) =>
{
    try
    {
        _ = await db.Scenarios.CountAsync();
        _ = await redis.GetDatabase().PingAsync();
        return Results.Ok(new { status = "ready" });
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = "not_ready", error = ex.Message }, statusCode: 503);
    }
});

app.MapGet("/health", async (
    AppDbContext db,
    IConnectionMultiplexer redis,
    IToolGateway tools,
    IRefundAiToolCatalog aiTools,
    IRefundAgentHost agentHost,
    Stayota.RefundAgent.Infrastructure.Ai.IChatClientFactory chatClientFactory,
    IOptions<AiOptions> aiOptions,
    IOptions<ProductionOptions> productionOptions,
    IOptions<HostingOptions> hostingOptions,
    IProductionOrderClient production) =>
{
    var opts = hostingOptions.Value;
    bool pgOk;
    bool redisOk;
    int scenarios = 0;
    try
    {
        scenarios = await db.Scenarios.CountAsync();
        pgOk = true;
    }
    catch
    {
        pgOk = false;
    }

    try
    {
        await redis.GetDatabase().PingAsync();
        redisOk = true;
    }
    catch
    {
        redisOk = false;
    }

    var ready = pgOk && redisOk;
    var payload = new Dictionary<string, object?>
    {
        ["status"] = ready ? "ok" : "degraded",
        ["postgres"] = pgOk,
        ["redis"] = redisOk,
        ["demoEnabled"] = opts.DemoEnabled,
        ["productionMode"] = production.Mode,
        ["aiProvider"] = agentHost.ProviderName,
        ["agent"] = agentHost.Agent.Name,
        ["mcpEndpoint"] = "/mcp",
        ["authRequired"] = !string.IsNullOrWhiteSpace(opts.ApiKey)
    };

    if (opts.ExposeDetailedHealth)
    {
        payload["scenarios"] = scenarios;
        payload["tools"] = tools.ListContracts().Count;
        payload["aiFunctions"] = aiTools.Functions.Count;
        payload["aiConfiguredProvider"] = aiOptions.Value.Provider;
        payload["aiResolvedProvider"] = chatClientFactory.ProviderName;
        payload["productionConfigured"] = productionOptions.Value.Mode;
        payload["stack"] = new
        {
            meai = "Microsoft.Extensions.AI",
            agentFramework = "Microsoft.Agents.AI",
            workflows = "Microsoft.Agents.AI.Workflows",
            openAi = "Microsoft.Extensions.AI.OpenAI",
            ollama = "OllamaSharp",
            mcp = "ModelContextProtocol.AspNetCore",
            functionApproval = "ApprovalRequiredAIFunction / ToolApprovalRequestContent"
        };
        payload["database"] = "postgresql";
        payload["cache"] = "redis";
        payload["agentRoot"] = Environment.GetEnvironmentVariable("STAYOTA_AGENT_ROOT");
    }

    return ready ? Results.Ok(payload) : Results.Json(payload, statusCode: 503);
});

app.Run();

public partial class Program;
