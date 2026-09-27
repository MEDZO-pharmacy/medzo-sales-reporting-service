using Medzo.SalesReporting.Contracts;
using Medzo.SalesReporting.Services;
using Microsoft.AspNetCore.Mvc;

namespace Medzo.SalesReporting.Controllers;

[ApiController]
[Route("api/batch-removals")]
public sealed class BatchRemovalsController(IBatchRemovalService removals) : ControllerBase
{
    [HttpGet("candidates")]
    public Task<BatchRemovalCandidatesResponse> GetCandidates([FromQuery] int withinDays = 30, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        removals.GetCandidatesAsync(withinDays, page, pageSize, ct);

    [HttpPost("{batchId:guid}")]
    public async Task<ActionResult<BatchRemovalResponse>> Remove(Guid batchId, RemoveBatchRequest request, CancellationToken ct)
    {
        try
        {
            var result = await removals.RemoveAsync(batchId, request, ct);
            return result.AlreadyRemoved ? Ok(result) : Created($"/api/batch-removals/{batchId}", result);
        }
        catch (BatchRemovalValidationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}