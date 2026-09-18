using Microsoft.AspNetCore.Mvc;
using Stayota.RefundAgent.Application.Contracts;

namespace Stayota.RefundAgent.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class AgentController(
    IAgentOrchestrator orchestrator,
    IScenarioCatalog scenarios,
    IConfirmationStore confirmationStore) : ControllerBase
{
    [HttpGet("scenarios")]
    public ActionResult<IReadOnlyList<ScenarioDto>> ListScenarios() => Ok(scenarios.List());

    [HttpPost("agent/message")]
    public async Task<ActionResult<AgentDecisionDto>> Message([FromBody] AgentMessageRequest request, CancellationToken ct)
    {
        var result = await orchestrator.HandleAsync(request, ct);
        return Ok(result);
    }

    [HttpPost("confirmations")]
    public async Task<ActionResult<ConfirmActionResponse>> Confirm([FromBody] ConfirmActionRequest request, CancellationToken ct)
    {
        var token = await confirmationStore.IssueAsync(
            request.CaseId, request.OrderId, request.OrderVersion, request.Action, TimeSpan.FromMinutes(10), ct);
        return Ok(new ConfirmActionResponse(true, "confirmation issued", token));
    }
}
