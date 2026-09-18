using Microsoft.AspNetCore.Mvc;
using Stayota.RefundAgent.Application.Contracts;

namespace Stayota.RefundAgent.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class AgentController(
    IAgentOrchestrator orchestrator,
    IScenarioCatalog scenarios,
    IConfirmationStore confirmationStore,
    IToolGateway tools,
    IEvalRunner evalRunner,
    IScenarioWorkflow workflow) : ControllerBase
{
    [HttpGet("scenarios")]
    public ActionResult<IReadOnlyList<ScenarioDto>> ListScenarios() => Ok(scenarios.List());

    [HttpGet("tools")]
    public ActionResult<IReadOnlyList<ToolContractDto>> ListTools() => Ok(tools.ListContracts());

    [HttpPost("agent/message")]
    public async Task<ActionResult<AgentDecisionDto>> Message([FromBody] AgentMessageRequest request, CancellationToken ct)
    {
        try
        {
            var result = await orchestrator.HandleAsync(request, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(503, new { message = ex.Message });
        }
    }

    [HttpPost("workflows/{scenarioId}/run")]
    public async Task<ActionResult<WorkflowRunResultDto>> RunWorkflow(string scenarioId, CancellationToken ct)
    {
        try
        {
            var result = await workflow.RunAsync(scenarioId, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("workflows/run-all")]
    public async Task<ActionResult<object>> RunAllWorkflows(CancellationToken ct)
    {
        var results = new List<WorkflowRunResultDto>();
        foreach (var id in new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L" })
        {
            results.Add(await workflow.RunAsync(id, ct));
        }
        return Ok(new
        {
            total = results.Count,
            succeeded = results.Count(r => r.Succeeded),
            failed = results.Count(r => !r.Succeeded),
            results
        });
    }

    [HttpPost("confirmations")]
    public async Task<ActionResult<ConfirmActionResponse>> Confirm([FromBody] ConfirmActionRequest request, CancellationToken ct)
    {
        var token = await confirmationStore.IssueAsync(
            request.CaseId, request.OrderId, request.OrderVersion, request.Action, TimeSpan.FromMinutes(10), ct);
        return Ok(new ConfirmActionResponse(true, "confirmation issued", token));
    }

    [HttpGet("eval/cases")]
    public ActionResult<IReadOnlyList<EvalCaseDto>> EvalCases() => Ok(evalRunner.ListCases());

    [HttpPost("eval/run")]
    public async Task<ActionResult<object>> RunEval(CancellationToken ct)
    {
        var results = await evalRunner.RunAllAsync(ct);
        return Ok(new
        {
            total = results.Count,
            passed = results.Count(r => r.Passed),
            failed = results.Count(r => !r.Passed),
            results
        });
    }
}
