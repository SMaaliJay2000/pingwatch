namespace PingWatch.Domain;

/// <summary>What a check result did to its monitor.</summary>
public enum CheckTransition { Ignored, NoChange, RetryScheduled, WentDown, Recovered }

public static class CheckRules
{
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(20);

    public const int FailuresToGoDown = 2;
}

public static class KeywordMatcher
{
    public static bool IsSatisfied(string body, string keyword, KeywordMode mode) =>
        body.Contains(keyword, StringComparison.Ordinal) == (mode == KeywordMode.Contains);
}

public static class SslExpiry
{
    public const int WarningWindowDays = 30;

    /// <summary>Whole days remaining. Negative once the certificate has expired.</summary>
    public static int DaysLeft(DateTimeOffset expiresAt, DateTimeOffset now) =>
        (int)Math.Floor((expiresAt - now).TotalDays);

    public static bool IsExpired(DateTimeOffset expiresAt, DateTimeOffset now) => expiresAt <= now;

    public static bool NeedsWarning(int daysLeft) => daysLeft is >= 0 and <= WarningWindowDays;
}