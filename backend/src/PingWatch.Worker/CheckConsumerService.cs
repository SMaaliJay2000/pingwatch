using System.Threading.Channels;
using Microsoft.Extensions.Options;
using PingWatch.Application.Checks;

namespace PingWatch.Worker;

internal sealed class CheckConsumerService(
    IServiceScopeFactory scopes,
    Channel<CheckJob> queue,
    IOptions<WorkerOptions> options,
    ILogger<CheckConsumerService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, options.Value.Concurrency).Select(_ => ConsumeAsync(stoppingToken)));

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();

        await foreach (var job in queue.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var runner = scope.ServiceProvider.GetServices<ICheckRunner>().Single(r => r.Type == job.Type);
                var processor = scope.ServiceProvider.GetRequiredService<CheckResultProcessor>();

                var result = await runner.RunAsync(job, cancellationToken);
                await processor.ProcessAsync(job, result, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // An unexpected error is our fault, so it is logged rather than recorded as downtime.
                logger.LogError(exception, "Check for monitor {MonitorId} failed unexpectedly", job.MonitorId);
            }
        }
    }
}