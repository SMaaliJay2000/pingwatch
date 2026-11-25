using Microsoft.EntityFrameworkCore;
using PingWatch.Application.Checks;
using PingWatch.Domain;

namespace PingWatch.IntegrationTests;

[Collection(EngineCollection.Name)]
public class CheckProcessingTests(EngineFixture engine) : IAsyncLifetime
{
    private static readonly CheckRunResult Passed = new(true, 200, 120);
    private static readonly CheckRunResult Failed = new(false, 503, 80, "HTTP 503");

    public Task InitializeAsync() => engine.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_whole_outage_from_first_pass_to_recovery()
    {
        var monitor = await engine.AddMonitorAsync(await engine.AddAccountAsync());
        var job = CheckJob.From(monitor);

        (await ProcessAsync(job, Passed)).Should().Be(CheckTransition.NoChange);
        (await engine.ReloadAsync(monitor.Id)).Status.Should().Be(MonitorStatus.Up);

        (await ProcessAsync(job, Failed)).Should().Be(CheckTransition.RetryScheduled);
        var suspect = await engine.ReloadAsync(monitor.Id);
        suspect.Status.Should().Be(MonitorStatus.Up);
        suspect.NextCheckAt.Should().Be(engine.Time.Now.AddSeconds(20));
        (await IncidentsAsync(monitor.Id)).Should().BeEmpty();
        engine.Events.All.Should().BeEmpty();

        engine.Time.Now = engine.Time.Now.AddSeconds(20);
        var wentDownAt = engine.Time.Now;
        (await ProcessAsync(job, Failed)).Should().Be(CheckTransition.WentDown);
        (await ProcessAsync(job, Failed)).Should().Be(CheckTransition.NoChange);

        engine.Time.Now = wentDownAt.AddSeconds(90);
        (await ProcessAsync(job, Passed)).Should().Be(CheckTransition.Recovered);

        (await engine.ReloadAsync(monitor.Id)).Status.Should().Be(MonitorStatus.Up);
        var incident = (await IncidentsAsync(monitor.Id)).Single();
        incident.StartedAt.Should().Be(wentDownAt);
        incident.ResolvedAt.Should().Be(wentDownAt.AddSeconds(90));
        incident.DurationSec.Should().Be(90);
        incident.Cause.Should().Be("HTTP 503");
        engine.Events.All.Should().Equal(
            new MonitorWentDown(monitor.AccountId, monitor.Id, incident.Id, wentDownAt, "HTTP 503"),
            new MonitorRecovered(monitor.AccountId, monitor.Id, incident.Id, wentDownAt.AddSeconds(90), 90));

        var results = await engine.WithDbAsync(db => db.CheckResults.IgnoreQueryFilters()
            .Where(r => r.MonitorId == monitor.Id).OrderBy(r => r.Id).ToListAsync());
        results.Select(r => r.IsUp).Should().Equal(true, false, false, false, true);
        results.Should().OnlyContain(r => r.AccountId == monitor.AccountId && r.Region == "default");
        results[1].Error.Should().Be("HTTP 503");
        results[1].StatusCode.Should().Be(503);
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("deleted")]
    [InlineData("retargeted")]
    public async Task A_result_that_no_longer_applies_is_discarded(string change)
    {
        var monitor = await engine.AddMonitorAsync(
            await engine.AddAccountAsync(), m => m.ConsecutiveFailures = 1);
        var job = CheckJob.From(monitor);
        await engine.WithDbAsync(db =>
        {
            var rows = db.Monitors.IgnoreQueryFilters().Where(m => m.Id == monitor.Id);
            return change switch
            {
                "paused" => rows.ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MonitorStatus.Paused)),
                "deleted" => rows.ExecuteDeleteAsync(),
                _ => rows.ExecuteUpdateAsync(s => s.SetProperty(m => m.Url, "https://example.org")),
            };
        });

        (await ProcessAsync(job, Failed)).Should().Be(CheckTransition.Ignored);

        (await IncidentsAsync(monitor.Id)).Should().BeEmpty();
        engine.Events.All.Should().BeEmpty();
        (await engine.WithDbAsync(db => db.CheckResults.IgnoreQueryFilters().CountAsync())).Should().Be(0);
    }

    [Fact]
    public async Task An_incident_left_open_by_mistake_is_closed_without_an_alert()
    {
        var monitor = await engine.AddMonitorAsync(
            await engine.AddAccountAsync(), m => m.Status = MonitorStatus.Pending);
        await engine.SaveAsync(new Incident
        {
            AccountId = monitor.AccountId,
            MonitorId = monitor.Id,
            StartedAt = engine.Time.Now.AddHours(-1),
            Cause = "old",
        });

        await ProcessAsync(CheckJob.From(monitor), Passed);

        (await IncidentsAsync(monitor.Id)).Single().ResolvedAt.Should().Be(engine.Time.Now);
        engine.Events.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Simultaneous_results_for_one_monitor_open_exactly_one_incident()
    {
        var monitor = await engine.AddMonitorAsync(
            await engine.AddAccountAsync(), m => m.Status = MonitorStatus.Up);
        var job = CheckJob.From(monitor);

        var transitions = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => ProcessAsync(job, Failed)));

        transitions.Count(t => t == CheckTransition.WentDown).Should().Be(1);
        (await IncidentsAsync(monitor.Id)).Should().ContainSingle();
        engine.Events.All.OfType<MonitorWentDown>().Should().ContainSingle();
        (await engine.ReloadAsync(monitor.Id)).ConsecutiveFailures.Should().Be(10, "no update was lost");
    }

    [Theory]
    [InlineData(MonitorType.Ssl, 31.5, false)]
    [InlineData(MonitorType.Ssl, 30.5, true)]
    [InlineData(MonitorType.Ssl, 0.5, true)]
    [InlineData(MonitorType.Ssl, -1, false)]
    [InlineData(MonitorType.Http, 5, false)]
    public async Task Ssl_monitors_report_certificates_that_expire_soon(MonitorType type, double days, bool warns)
    {
        var monitor = await engine.AddMonitorAsync(await engine.AddAccountAsync(), m =>
        {
            m.Type = type;
            m.Host = "example.com";
        });
        var expiresAt = engine.Time.Now.AddDays(days);

        await ProcessAsync(CheckJob.From(monitor), Passed with { CertificateExpiresAt = expiresAt });

        engine.Events.All.OfType<SslExpiring>().Should().HaveCount(warns ? 1 : 0);
    }

    private Task<CheckTransition> ProcessAsync(CheckJob job, CheckRunResult result) =>
        engine.WithScopeAsync<CheckResultProcessor, CheckTransition>(
            processor => processor.ProcessAsync(job, result, CancellationToken.None));

    private Task<List<Incident>> IncidentsAsync(Guid monitorId) => engine.WithDbAsync(
        db => db.Incidents.IgnoreQueryFilters().Where(i => i.MonitorId == monitorId).ToListAsync());
}
