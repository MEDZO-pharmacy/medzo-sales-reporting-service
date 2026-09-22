using Medzo.SalesReporting.Contracts;
using Medzo.SalesReporting.Services;
using Microsoft.AspNetCore.Mvc;

namespace Medzo.SalesReporting.Controllers;

[ApiController]
[Route("api/sale-items")]
public sealed class SaleItemsController(ISaleItemSearchService saleItems) : ControllerBase
{
 [HttpGet]
 public async Task<ActionResult<SaleItemSearchResponse>> Search([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
 {
  try
  {
   return Ok(await saleItems.SearchAsync(search, page, pageSize, ct));
  }
  catch (SaleItemSearchUnavailableException error)
  {
   return StatusCode(StatusCodes.Status502BadGateway, new { error = error.Message });
  }
 }
}
