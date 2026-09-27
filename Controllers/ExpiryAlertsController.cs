using Medzo.SalesReporting.Contracts;
using Medzo.SalesReporting.Services;
using Microsoft.AspNetCore.Mvc;

namespace Medzo.SalesReporting.Controllers;

[ApiController]
[Route("api/expiry-alerts")]
public sealed class ExpiryAlertsController(IExpiryAlertService alerts) : ControllerBase
{
    [HttpGet]
    public Task<NearExpiryAlertResponse> Get([FromQuery] int withinDays = 30, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => alerts.GetNearExpiryAsync(withinDays, page, pageSize, ct);
}