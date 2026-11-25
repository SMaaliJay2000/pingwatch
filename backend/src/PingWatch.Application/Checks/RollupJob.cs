using Microsoft.Extensions.Logging;

namespace PingWatch.Application.Checks;

public sealed class RollupJob(IRollupStore store, TimeProvider time, ILogger<RollupJob> logger)
{
    private const int PartitionMonthsAhead = 3;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var thisMonth = new DateOnly(today.Year, today.Month, 1);

        for (var ahead = 0; ahead <= PartitionMonthsAhead; ahead++)
        {
            try
            {
                await store.EnsurePartitionAsync(thisMonth.AddMonths(ahead), cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Inserts still work through the default partition, so carry on with the rest.
                logger.LogError(
                    exception, "Could not create the check_results partition for {Month}", thisMonth.AddMonths(ahead));
            }
        }

        // Order matters: if aggregation throws, nothing is purged and no history is lost.
        var aggregated = await store.AggregateBeforeAsync(today, cancellationToken);
        var purged = await store.PurgeRawResultsAsync(today, cancellationToken);
        var tokens = await store.PurgeExpiredRefreshTokensAsync(now, cancellationToken);

        logger.LogInformation(
            "Rollup done: {Aggregated} daily rows written, {Purged} raw results and {Tokens} refresh tokens removed",
            aggregated, purged, tokens);
    }
}