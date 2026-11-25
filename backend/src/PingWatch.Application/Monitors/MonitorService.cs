using FluentValidation;
using PingWatch.Application.Accounts;
using PingWatch.Domain;

namespace PingWatch.Application.Monitors;

public sealed class MonitorService(
    IMonitorStore monitors,
    IMonitorHistory history,
    IAccountReader accounts,
    ITenantContext tenant)
{
    public async Task<IReadOnlyList<MonitorResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var all = await monitors.ListAsync(cancellationToken);
        var totals = await Last24HoursAsync([.. all.Select(m => m.Id)], cancellationToken);

        return [.. all.Select(m => ToResponse(m, totals.GetValueOrDefault(m.Id)))];
    }

    public async Task<MonitorResponse?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var monitor = await monitors.FindAsync(id, cancellationToken);
        return monitor is null ? null : await ToResponseAsync(monitor, cancellationToken);
    }

    public async Task<MonitorResponse> CreateAsync(MonitorRequest request, CancellationToken cancellationToken)
    {
        var limits = await GetLimitsAsync(cancellationToken);
        await new MonitorValidator(limits).ValidateAndThrowAsync(request, cancellationToken);

        var accountId = tenant.AccountId ?? throw new InvalidOperationException("There is no current account.");

        return await monitors.WithAccountLockAsync(
            async () =>
            {
                // Paused monitors count too.
                if (!limits.CanAddMonitor(await monitors.CountAsync(cancellationToken)))
                {
                    throw new PlanLimitException($"Your plan allows {limits.MaxMonitors} monitors.");
                }

                var monitor = new UptimeMonitor { AccountId = accountId, Name = "" };
                Apply(monitor, request);
                monitors.Add(monitor);
                await monitors.SaveAsync(cancellationToken);

                return ToResponse(monitor, null);
            },
            cancellationToken);
    }

    public async Task<MonitorResponse?> UpdateAsync(
        Guid id, MonitorRequest request, CancellationToken cancellationToken)
    {
        var limits = await GetLimitsAsync(cancellationToken);
        await new MonitorValidator(limits).ValidateAndThrowAsync(request, cancellationToken);

        var monitor = await monitors.FindAsync(id, cancellationToken);
        if (monitor is null)
        {
            return null;
        }

        var oldTarget = (monitor.Type, monitor.Url, monitor.Host, monitor.Port);
        Apply(monitor, request);

        if (monitor.Status != MonitorStatus.Paused)
        {
            // A new target is checked right away; otherwise a shorter interval just pulls the next check in.
            var now = DateTimeOffset.UtcNow;
            var latest = now.AddSeconds(monitor.IntervalSec);

            if (oldTarget != (monitor.Type, monitor.Url, monitor.Host, monitor.Port))
            {
                monitor.NextCheckAt = now;
            }
            else if (monitor.NextCheckAt > latest)
            {
                monitor.NextCheckAt = latest;
            }
        }

        await monitors.SaveAsync(cancellationToken);
        return await ToResponseAsync(monitor, cancellationToken);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        monitors.DeleteAsync(id, cancellationToken);

    public async Task<MonitorResponse?> PauseAsync(Guid id, CancellationToken cancellationToken)
    {
        var monitor = await monitors.FindAsync(id, cancellationToken);
        if (monitor is null)
        {
            return null;
        }

        if (monitor.Pause())
        {
            // Downtime can't be measured while paused, and a later first check would never close it.
            var incident = await monitors.FindOpenIncidentAsync(id, cancellationToken);
            incident?.Resolve(DateTimeOffset.UtcNow);
            await monitors.SaveAsync(cancellationToken);
        }

        return await ToResponseAsync(monitor, cancellationToken);
    }

    public async Task<MonitorResponse?> ResumeAsync(Guid id, CancellationToken cancellationToken)
    {
        var limits = await GetLimitsAsync(cancellationToken);

        var monitor = await monitors.WithAccountLockAsync(
            async () =>
            {
                var found = await monitors.FindAsync(id, cancellationToken);
                if (found is null || found.Status != MonitorStatus.Paused)
                {
                    return found;
                }

                // After a downgrade, resuming must not get around the new plan's limits.
                if (!limits.CanAddMonitor(await monitors.CountActiveAsync(cancellationToken)))
                {
                    throw new PlanLimitException($"Your plan allows {limits.MaxMonitors} active monitors.");
                }

                if (!limits.AllowsInterval(found.IntervalSec))
                {
                    throw new PlanLimitException(
                        $"Your plan allows one check every {limits.MinIntervalSec} seconds at the most. "
                        + "Change the interval first.");
                }

                found.Resume(DateTimeOffset.UtcNow);
                await monitors.SaveAsync(cancellationToken);
                return found;
            },
            cancellationToken);

        return monitor is null ? null : await ToResponseAsync(monitor, cancellationToken);
    }

    private async Task<PlanLimits> GetLimitsAsync(CancellationToken cancellationToken)
    {
        var account = await accounts.GetCurrentAsync(cancellationToken)
            ?? throw new InvalidOperationException("There is no current account.");

        return Plans.For(account.Plan);
    }

    private async Task<IReadOnlyDictionary<Guid, CheckTotals>> Last24HoursAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        ids.Count == 0
            ? new Dictionary<Guid, CheckTotals>()
            : await history.TotalsSinceAsync(ids, DateTimeOffset.UtcNow.AddHours(-24), cancellationToken);

    private async Task<MonitorResponse> ToResponseAsync(UptimeMonitor monitor, CancellationToken cancellationToken)
    {
        var totals = await Last24HoursAsync([monitor.Id], cancellationToken);
        return ToResponse(monitor, totals.GetValueOrDefault(monitor.Id));
    }

    private static MonitorResponse ToResponse(UptimeMonitor m, CheckTotals? last24Hours) => new(
        m.Id, m.Name, m.Type, m.Url, m.Host, m.Port, m.Method, m.ExpectedStatus, m.Keyword, m.KeywordMode,
        m.TimeoutMs, m.IntervalSec, m.Status, m.LastCheckedAt, m.CreatedAt,
        last24Hours is null ? null : Uptime.Percent(last24Hours.Checks, last24Hours.Failures));

    // The request is already validated. Fields that don't belong to the type are cleared.
    private static void Apply(UptimeMonitor monitor, MonitorRequest request)
    {
        var type = request.Type!.Value;
        var usesUrl = type is MonitorType.Http or MonitorType.Keyword;

        monitor.Name = request.Name!.Trim();
        monitor.Type = type;
        monitor.Url = usesUrl ? request.Url!.Trim() : null;
        monitor.Host = usesUrl ? null : request.Host;
        monitor.Port = type switch
        {
            MonitorType.Tcp => request.Port,
            MonitorType.Ssl => request.Port ?? 443,
            _ => null,
        };
        monitor.Method = usesUrl ? request.Method ?? CheckMethod.Get : null;
        monitor.ExpectedStatus = (usesUrl ? request.ExpectedStatus?.Trim() : null) ?? "200-399";
        monitor.Keyword = type == MonitorType.Keyword ? request.Keyword : null;
        monitor.KeywordMode = type == MonitorType.Keyword ? request.KeywordMode ?? KeywordMode.Contains : null;
        monitor.TimeoutMs = request.TimeoutMs ?? (type == MonitorType.Tcp ? 5_000 : 10_000);
        // Certificates are checked daily by default.
        monitor.IntervalSec = request.IntervalSec ?? (type == MonitorType.Ssl ? MonitorRules.MaxIntervalSec : 300);
    }
}