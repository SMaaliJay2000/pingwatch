using PingWatch.Domain;

namespace PingWatch.Application.Checks;

public sealed class CheckResultProcessor(ICheckStore store, IDomainEventPublisher publisher, TimeProvider time)
{
    public async Task<CheckTransition> ProcessAsync(
        CheckJob job, CheckRunResult result, CancellationToken cancellationToken)
    {
        var events = new List<IDomainEvent>();

        var transition = await store.WithMonitorLockAsync(
            job.MonitorId,
            async monitor =>
            {
                // Deleted, paused or edited while the check was running: the result no longer applies.
                if (monitor is null || monitor.Status == MonitorStatus.Paused || job.IsStaleFor(monitor))
                {
                    return CheckTransition.Ignored;
                }

                var now = time.GetUtcNow();
                store.Add(new CheckResult
                {
                    AccountId = monitor.AccountId,
                    MonitorId = monitor.Id,
                    CheckedAt = now,
                    IsUp = result.IsUp,
                    StatusCode = result.StatusCode,
                    ResponseMs = result.ResponseMs,
                    Error = result.Error,
                });

                var openIncident = await store.FindOpenIncidentAsync(monitor.Id, cancellationToken);
                var change = monitor.RecordCheck(result.IsUp, now);

                if (result.IsUp && openIncident is not null)
                {
                    // Also closes an incident left over from a race, without announcing a recovery.
                    openIncident.Resolve(now);

                    if (change == CheckTransition.Recovered)
                    {
                        events.Add(new MonitorRecovered(
                            monitor.AccountId, monitor.Id, openIncident.Id, now, openIncident.DurationSec!.Value));
                    }
                }
                else if (change == CheckTransition.WentDown && openIncident is null)
                {
                    var incident = new Incident
                    {
                        AccountId = monitor.AccountId,
                        MonitorId = monitor.Id,
                        StartedAt = now,
                        Cause = result.Error ?? "Check failed",
                    };
                    store.Add(incident);
                    events.Add(new MonitorWentDown(
                        monitor.AccountId, monitor.Id, incident.Id, now, incident.Cause));
                }

                if (job.Type == MonitorType.Ssl && result.CertificateExpiresAt is { } expiresAt)
                {
                    var daysLeft = SslExpiry.DaysLeft(expiresAt, now);
                    if (SslExpiry.NeedsWarning(daysLeft))
                    {
                        events.Add(new SslExpiring(monitor.AccountId, monitor.Id, expiresAt, daysLeft));
                    }
                }

                await store.SaveAsync(cancellationToken);
                return change;
            },
            cancellationToken);

        // Only after the commit, so nobody is told about a change that was rolled back.
        foreach (var domainEvent in events)
        {
            await publisher.PublishAsync(domainEvent, cancellationToken);
        }

        return transition;
    }
}