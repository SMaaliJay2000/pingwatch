using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingWatch.Application.Monitors;

namespace PingWatch.Api.Controllers;

// A monitor of another account is "not found", so its existence isn't revealed.
[ApiController]
[Route("api/v1/monitors")]
[Authorize]
public sealed class MonitorsController(MonitorService monitors, MonitorReportService reports) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<MonitorResponse>> List(CancellationToken cancellationToken) =>
        monitors.ListAsync(cancellationToken);

    [HttpPost]
    public async Task<ActionResult<MonitorResponse>> Create(
        MonitorRequest request, CancellationToken cancellationToken)
    {
        var monitor = await monitors.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = monitor.Id }, monitor);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MonitorResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        OrNotFound(await monitors.GetAsync(id, cancellationToken));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<MonitorResponse>> Update(
        Guid id, MonitorRequest request, CancellationToken cancellationToken) =>
        OrNotFound(await monitors.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        await monitors.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/pause")]
    public async Task<ActionResult<MonitorResponse>> Pause(Guid id, CancellationToken cancellationToken) =>
        OrNotFound(await monitors.PauseAsync(id, cancellationToken));

    [HttpPost("{id:guid}/resume")]
    public async Task<ActionResult<MonitorResponse>> Resume(Guid id, CancellationToken cancellationToken) =>
        OrNotFound(await monitors.ResumeAsync(id, cancellationToken));

    [HttpGet("{id:guid}/results")]
    public async Task<ActionResult<PagedResponse<CheckResultResponse>>> Results(
        Guid id, [FromQuery] ResultsQuery query, CancellationToken cancellationToken) =>
        OrNotFound(await reports.GetResultsAsync(id, query, cancellationToken));

    [HttpGet("{id:guid}/stats")]
    public async Task<ActionResult<StatsResponse>> Stats(
        Guid id, CancellationToken cancellationToken, [FromQuery] string range = "24h") =>
        OrNotFound(await reports.GetStatsAsync(id, range, cancellationToken));

    private ActionResult<T> OrNotFound<T>(T? value) where T : class => value is null ? NotFound() : value;
}