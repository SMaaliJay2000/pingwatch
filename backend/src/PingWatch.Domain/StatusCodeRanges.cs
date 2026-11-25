using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace PingWatch.Domain;

/// <summary>The HTTP status codes a monitor treats as up, such as "200-399" or "200,301-302".</summary>
public sealed class StatusCodeRanges
{
    private readonly (int Min, int Max)[] _ranges;

    private StatusCodeRanges((int Min, int Max)[] ranges) => _ranges = ranges;

    public bool Contains(int statusCode) => _ranges.Any(r => statusCode >= r.Min && statusCode <= r.Max);

    public static bool TryParse(string? text, [NotNullWhen(true)] out StatusCodeRanges? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        var ranges = new (int Min, int Max)[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            var bounds = parts[i].Split('-', StringSplitOptions.TrimEntries);
            if (bounds.Length > 2
                || !TryParseCode(bounds[0], out var min)
                || !TryParseCode(bounds[^1], out var max)
                || min > max)
            {
                return false;
            }

            ranges[i] = (min, max);
        }

        result = new StatusCodeRanges(ranges);
        return true;
    }

    private static bool TryParseCode(string text, out int code) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out code) && code is >= 100 and <= 599;
}