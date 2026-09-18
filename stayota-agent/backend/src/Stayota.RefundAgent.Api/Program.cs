using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Stayota.RefundAgent.Application.Contracts;
using Stayota.RefundAgent.Infrastructure;
using Stayota.RefundAgent.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

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
    await db.Database.EnsureCreatedAsync();
    var store = scope.ServiceProvider.GetRequiredService<Stayota.RefundAgent.Application.Services.IRefundDataStore>();
    await store.EnsureSeededAsync();
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.MapControllers();
app.MapGet("/health", async (AppDbContext db) =>
{
    var scenarios = await db.Scenarios.CountAsync();
    return Results.Ok(new { status = "ok", scenarios, database = "postgresql", cache = "redis" });
});

app.Run();

public partial class Program;
