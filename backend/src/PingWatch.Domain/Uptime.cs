namespace PingWatch.Domain;

public static class Uptime
{
    /// <summary>
    /// Percentage of successful checks, or null when there were none. Truncated to 3 decimals,
    /// so any failure keeps the result below 100.
    /// </summary>
    public static decimal? Percent(long checks, long failures) =>
        checks == 0 ? null : Math.Floor((checks - failures) * 100_000m / checks) / 1000m;
}