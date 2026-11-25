using Microsoft.EntityFrameworkCore;
using Npgsql;
using PingWatch.Application.Checks;
using PingWatch.Domain;

namespace PingWatch.IntegrationTests;

[Collection(EngineCollection.Name)]
public class SchedulerClaimTests(EngineFixture engine) : IAsyncLifetime
{
    public Task InitializeAsync() => engine.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Only_due_active_monitors_are_claimed_and_rescheduled()
    {
        var account = await engine.AddAccountAsync();
        var due = await engine.AddMonitorAsync(account, m => m.IntervalSec = 600);
        var down = await engine.AddMonitorAsync(account, m => m.Status = MonitorStatus.Down);
        await engine.AddMonitorAsync(account, m => m.NextCheckAt = engine.Time.Now.AddSeconds(1));
        await engine.AddMonitorAsync(account, m => m.Status = MonitorStatus.Paused);
        await engine.AddMonitorAsync(account, m => m.Type = MonitorType.Heartbeat);

        var jobs = await ClaimAsync(100);

        jobs.Select(j => j.MonitorId).Should().BeEquivalentTo([due.Id, down.Id]);
        (await engine.ReloadAsync(due.Id)).NextCheckAt.Should().Be(engine.Time.Now.AddSeconds(600));
        (await ClaimAsync(100)).Should().BeEmpty("claimed monitors are no longer due");
    }

    [Fact]
    public async Task A_claimed_job_carries_every_check_setting()
    {
        var account = await engine.AddAccountAsync();
        var monitor = await engine.AddMonitorAsync(account, m =>
        {
            m.Type = MonitorType.Keyword;
            m.Method = CheckMethod.Post;
            m.ExpectedStatus = "201";
            m.Keyword = "ok";
            m.KeywordMode = KeywordMode.NotContains;
            m.TimeoutMs = 7_000;
        });

        var job = (await ClaimAsync(1)).Single();

        job.Should().Be(CheckJob.From(monitor));
    }

    [Theory]
    [InlineData(PlanTier.Free, 60, 300)]
    [InlineData(PlanTier.Starter, 30, 60)]
    [InlineData(PlanTier.Pro, 30, 30)]
    [InlineData(PlanTier.Starter, 900, 900)]
    public async Task The_scheduler_enforces_the_plans_minimum_interval(PlanTier plan, int interval, int applied)
    {
        var account = await engine.AddAccountAsync(plan);
        var monitor = await engine.AddMonitorAsync(account, m => m.IntervalSec = interval);

        await ClaimAsync(10);

        (await engine.ReloadAsync(monitor.Id)).NextCheckAt.Should().Be(engine.Time.Now.AddSeconds(applied));
    }

    [Fact]
    public async Task The_most_overdue_monitors_are_claimed_first_up_to_the_limit()
    {
        var account = await engine.AddAccountAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var minutes = 10 - i;
            ids.Add((await engine.AddMonitorAsync(account, m => m.NextCheckAt = engine.Time.Now.AddMinutes(-minutes))).Id);
        }

        var first = await ClaimAsync(2);
        var rest = await ClaimAsync(10);

        first.Select(j => j.MonitorId).Should().BeEquivalentTo(ids.Take(2));
        rest.Select(j => j.MonitorId).Should().BeEquivalentTo(ids.Skip(2));
    }

    [Fact]
    public async Task Rows_another_worker_has_locked_are_skipped_without_waiting()
    {
        var account = await engine.AddAccountAsync();
        var locked = await engine.AddMonitorAsync(account);
        var free = await engine.AddMonitorAsync(account);
        await using var other = await LockAsync("SELECT id FROM monitors WHERE id = @id FOR UPDATE", locked.Id);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var jobs = await ClaimAsync(10, timeout.Token);

        jobs.Select(j => j.MonitorId).Should().Equal(free.Id);
    }

    [Fact]
    public async Task A_locked_account_does_not_hide_its_monitors()
    {
        var account = await engine.AddAccountAsync();
        var monitor = await engine.AddMonitorAsync(account);
        // The API holds such a lock while it enforces plan limits.
        await using var other = await LockAsync(
            "SELECT id FROM accounts WHERE id = @id FOR NO KEY UPDATE", account.Id);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var jobs = await ClaimAsync(10, timeout.Token);

        jobs.Select(j => j.MonitorId).Should().Equal(monitor.Id);
    }

    [Fact]
    public async Task Simultaneous_workers_never_claim_the_same_monitor()
    {
        var account = await engine.AddAccountAsync(PlanTier.Pro);
        await engine.SaveAsync([.. Enumerable.Range(0, 300).Select(i => new UptimeMonitor
        {
            AccountId = account.Id,
            Name = $"m{i}",
            Url = "https://example.com",
            NextCheckAt = engine.Time.Now.AddSeconds(-1),
        })]);

        var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            var mine = new List<Guid>();
            IReadOnlyList<CheckJob> batch;
            while ((batch = await ClaimAsync(7)).Count > 0)
            {
                mine.AddRange(batch.Select(j => j.MonitorId));
            }

            return mine;
        }));

        var all = claims.SelectMany(c => c).ToList();
        all.Should().OnlyHaveUniqueItems().And.HaveCount(300);
    }

    private Task<IReadOnlyList<CheckJob>> ClaimAsync(int limit, CancellationToken cancellationToken = default) =>
        engine.WithScopeAsync<ICheckStore, IReadOnlyList<CheckJob>>(
            store => store.ClaimDueAsync(limit, engine.Time.Now, cancellationToken));

    // An open transaction holding a row lock until disposed.
    private async Task<IAsyncDisposable> LockAsync(string sql, Guid id)
    {
        var connection = new NpgsqlConnection(engine.ConnectionString);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteScalarAsync();
        return connection;
    }
}
