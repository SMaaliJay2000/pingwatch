using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingWatch.Application.Monitors;

namespace PingWatch.Api.Controllers;

[ApiController]
[Route("api/v1/incidents")]
[Authorize]
public sealed class IncidentsController(MonitorReportService reports) : ControllerBase
{
    [HttpGet]
    public Task<PagedResponse<IncidentResponse>> List(
        [FromQuery] IncidentsQuery query, CancellationToken cancellationToken) =>
        reports.GetIncidentsAsync(query, cancellationToken);
}