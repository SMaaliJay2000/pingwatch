using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PingWatch.Worker;

/// <summary>Remembers when the scheduler last completed a cycle.</summary>
public sealed class SchedulerHealth(TimeProvider time)
{
    public static readonly TimeSpan StallThreshold = TimeSpan.FromSeconds(60);

    private readonly long _startedAt = time.GetUtcNow().UtcTicks;
    private long _lastCycleAt;
    private long _lastReportAt;

    public void RecordCycle() => Interlocked.Exchange(ref _lastCycleAt, time.GetUtcNow().UtcTicks);

    public TimeSpan SinceLastCycle
    {
        get
        {
            var last = Interlocked.Read(ref _lastCycleAt);
            return TimeSpan.FromTicks(time.GetUtcNow().UtcTicks - (last == 0 ? _startedAt : last));
        }
    }

    public bool IsReady => Interlocked.Read(ref _lastCycleAt) != 0 && SinceLastCycle <= StallThreshold;

    /// <summary>True when a stall should be logged: once it passes 60 s, then once a minute.</summary>
    public bool ShouldReportStall()
    {
        if (SinceLastCycle <= StallThreshold)
        {
            _lastReportAt = 0;
            return false;
        }

        var now = time.GetUtcNow().UtcTicks;
        if (_lastReportAt != 0 && now - _lastReportAt < StallThreshold.Ticks)
        {
            return false;
        }

        _lastReportAt = now;
        return true;
    }
}

public sealed class SchedulerHealthCheck(SchedulerHealth health) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(health.IsReady
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The scheduler has not completed a cycle recently."));
}

// Separate from the scheduler, so a hung scheduler can't silence its own alarm.
// M5 or M9 can turn this log into an alert to the founder.
internal sealed class SchedulerWatchdog(
    SchedulerHealth health, TimeProvider time, ILogger<SchedulerWatchdog> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10), time);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (health.ShouldReportStall())
            {
                logger.LogCritical(
                    "Heartbeat missed: the scheduler has not completed a cycle for {Seconds} s",
                    (int)health.SinceLastCycle.TotalSeconds);
            }
        }
    }
}