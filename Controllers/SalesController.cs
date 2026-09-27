using Medzo.SalesReporting.Contracts;
using Medzo.SalesReporting.Services;
using Microsoft.AspNetCore.Mvc;

namespace Medzo.SalesReporting.Controllers;

[ApiController]
[Route("api/sales")]
public sealed class SalesController(ISaleService sales) : ControllerBase
{
 [HttpPost]
 public async Task<ActionResult<SaleResponse>> Create(CreateSaleRequest request, CancellationToken ct)
 {
  try
  {
   var result = await sales.CreateAsync(request, ct);
   return result.AlreadyProcessed ? Ok(result) : Created($"/api/sales/{result.SaleId}", result);
  }
  catch (SaleValidationException e)
  {
   return BadRequest(new { error = e.Message });
  }
  catch (SaleConflictException e)
  {
   return Conflict(new { error = e.Message, retryable = true });
  }
 }
}
