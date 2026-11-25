using PingWatch.Domain;

namespace PingWatch.Application.Checks;

/// <summary>A snapshot of what to check, taken when the scheduler claims the monitor.</summary>
public sealed record CheckJob(
    Guid MonitorId,
    Guid AccountId,
    MonitorType Type,
    string? Url,
    string? Host,
    int? Port,
    CheckMethod? Method,
    string ExpectedStatus,
    string? Keyword,
    KeywordMode? KeywordMode,
    int TimeoutMs)
{
    public static CheckJob From(UptimeMonitor m) => new(
        m.Id, m.AccountId, m.Type, m.Url, m.Host, m.Port, m.Method, m.ExpectedStatus,
        m.Keyword, m.KeywordMode, m.TimeoutMs);

    /// <summary>True when the monitor was edited so that this job's result no longer describes it.</summary>
    public bool IsStaleFor(UptimeMonitor monitor) => From(monitor) with { TimeoutMs = TimeoutMs } != this;
}

public sealed record CheckRunResult(
    bool IsUp,
    int? StatusCode = null,
    int? ResponseMs = null,
    string? Error = null,
    DateTimeOffset? CertificateExpiresAt = null)
{
    public static CheckRunResult Down(string error, int? responseMs = null) =>
        new(false, ResponseMs: responseMs, Error: error);
}

public interface ICheckRunner
{
    MonitorType Type { get; }

    /// <summary>
    /// A target that fails or times out is a down result, not an exception.
    /// Only cancellation of <paramref name="cancellationToken"/> throws.
    /// </summary>
    Task<CheckRunResult> RunAsync(CheckJob job, CancellationToken cancellationToken);
}