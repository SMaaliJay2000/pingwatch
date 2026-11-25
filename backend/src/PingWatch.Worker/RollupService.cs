using PingWatch.Application.Checks;

namespace PingWatch.Worker;

/// <summary>Runs the rollup at start-up, to catch up, and then every night just after midnight UTC.</summary>
public sealed class RollupService(
    IServiceScopeFactory scopes, TimeProvider time, ILogger<RollupService> logger) : BackgroundService
{
    private static readonly TimeSpan RunAt = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromHours(1);

    public static TimeSpan DelayUntilNextRun(DateTimeOffset now)
    {
        var todaysRun = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero) + RunAt;
        return (todaysRun > now ? todaysRun : todaysRun.AddDays(1)) - now;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<RollupJob>().RunAsync(stoppingToken);
                delay = DelayUntilNextRun(time.GetUtcNow());
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Rollup failed; retrying in an hour");
                delay = RetryAfterFailure;
            }

            await Task.Delay(delay, time, stoppingToken);
        }
    }
}