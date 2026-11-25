using PingWatch.Domain;

namespace PingWatch.Application.Monitors;

/// <summary>Body of create and update. Everything optional is reset to its default when left out.</summary>
public sealed record MonitorRequest(
    string? Name,
    MonitorType? Type,
    string? Url = null,
    string? Host = null,
    int? Port = null,
    CheckMethod? Method = null,
    string? ExpectedStatus = null,
    string? Keyword = null,
    KeywordMode? KeywordMode = null,
    int? TimeoutMs = null,
    int? IntervalSec = null);

public sealed record MonitorResponse(
    Guid Id,
    string Name,
    MonitorType Type,
    string? Url,
    string? Host,
    int? Port,
    CheckMethod? Method,
    string ExpectedStatus,
    string? Keyword,
    KeywordMode? KeywordMode,
    int TimeoutMs,
    int IntervalSec,
    MonitorStatus Status,
    DateTimeOffset? LastCheckedAt,
    DateTimeOffset CreatedAt,
    decimal? Uptime24h);

public sealed record ResultsQuery(
    DateTimeOffset? From = null, DateTimeOffset? To = null, int Page = 1, int PageSize = 50);

/// <summary>Status is "open" or "resolved"; leave it out for both.</summary>
public sealed record IncidentsQuery(
    Guid? MonitorId = null, string? Status = null, int Page = 1, int PageSize = 50);

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, bool HasMore);

public sealed record CheckResultResponse(
    DateTimeOffset CheckedAt, bool IsUp, int? StatusCode, int? ResponseMs, string? Error, string Region);

public sealed record IncidentResponse(
    Guid Id,
    Guid MonitorId,
    string MonitorName,
    DateTimeOffset StartedAt,
    DateTimeOffset? ResolvedAt,
    string Cause,
    int? DurationSec);

public sealed record StatsPoint(DateTimeOffset At, int Checks, int Failures, decimal UptimePct, int? AvgMs);

public sealed record StatsResponse(
    string Range, decimal? UptimePct, int Checks, int Failures, int? AvgMs, IReadOnlyList<StatsPoint> Series);

/// <summary>The request is valid, but the account's plan doesn't allow it.</summary>
public sealed class PlanLimitException(string message) : Exception(message)
{
    /// <summary>Machine-readable code, so the web app can show an upgrade prompt.</summary>
    public const string Code = "plan_limit";
}