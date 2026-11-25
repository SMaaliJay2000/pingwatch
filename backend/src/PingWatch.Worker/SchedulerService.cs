using System.Threading.Channels;
using Microsoft.Extensions.Options;
using PingWatch.Application.Checks;

namespace PingWatch.Worker;

internal sealed class SchedulerService(
    IServiceScopeFactory scopes,
    Channel<CheckJob> queue,
    SchedulerHealth health,
    IOptions<WorkerOptions> options,
    TimeProvider time,
    ILogger<SchedulerService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval, time);

        do
        {
            try
            {
                await RunCycleAsync(stoppingToken);
                health.RecordCycle();
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                // A database blip must not stop the scheduler for good.
                logger.LogError(exception, "Scheduler cycle failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        // Claim only what fits in the queue. The rest stays due, for the next cycle or another worker.
        var room = options.Value.Concurrency - queue.Reader.Count;
        if (room <= 0)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ICheckStore>();

        foreach (var job in await store.ClaimDueAsync(room, time.GetUtcNow(), cancellationToken))
        {
            await queue.Writer.WriteAsync(job, cancellationToken);
        }
    }
}