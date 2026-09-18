using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Stayota.RefundAgent.Application.Contracts;
using Stayota.RefundAgent.Infrastructure;
using Stayota.RefundAgent.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

var agentRoot = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "../../.."));
Environment.SetEnvironmentVariable("STAYOTA_AGENT_ROOT", agentRoot);

builder.Services.AddRefundAgentInfrastructure(builder.Configuration);
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureDeletedAsync();
    await db.Database.EnsureCreatedAsync();
    var store = scope.ServiceProvider.GetRequiredService<Stayota.RefundAgent.Application.Services.IRefundDataStore>();
    await store.EnsureSeededAsync();
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.MapControllers();
app.MapGet("/health", async (
    AppDbContext db,
    IToolGateway tools,
    Stayota.RefundAgent.Application.Ai.IRefundAiToolCatalog aiTools,
    Stayota.RefundAgent.Application.Ai.IRefundAgentHost agentHost,
    Stayota.RefundAgent.Infrastructure.Ai.IChatClientFactory chatClientFactory,
    Microsoft.Extensions.Options.IOptions<Stayota.RefundAgent.Application.Ai.AiOptions> aiOptions) =>
{
    var scenarios = await db.Scenarios.CountAsync();
    return Results.Ok(new
    {
        status = "ok",
        scenarios,
        tools = tools.ListContracts().Count,
        aiFunctions = aiTools.Functions.Count,
        aiProvider = agentHost.ProviderName,
        aiConfiguredProvider = aiOptions.Value.Provider,
        aiResolvedProvider = chatClientFactory.ProviderName,
        agent = agentHost.Agent.Name,
        stack = new
        {
            meai = "Microsoft.Extensions.AI",
            agentFramework = "Microsoft.Agents.AI",
            workflows = "Microsoft.Agents.AI.Workflows",
            openAi = "Microsoft.Extensions.AI.OpenAI",
            ollama = "OllamaSharp"
        },
        database = "postgresql",
        cache = "redis",
        agentRoot = Environment.GetEnvironmentVariable("STAYOTA_AGENT_ROOT")
    });
});

app.Run();

public partial class Program;
