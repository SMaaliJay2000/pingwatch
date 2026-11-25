using System.Net.Security;
using PingWatch.Domain;
using PingWatch.Infrastructure.Checks;

namespace PingWatch.UnitTests;

public class KeywordAndSslTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Welcome home", "Welcome", KeywordMode.Contains, true)]
    [InlineData("Welcome home", "welcome", KeywordMode.Contains, false)]
    [InlineData("Welcome home", "error", KeywordMode.Contains, false)]
    [InlineData("Welcome home", "error", KeywordMode.NotContains, true)]
    [InlineData("Fatal error", "error", KeywordMode.NotContains, false)]
    [InlineData("", "x", KeywordMode.Contains, false)]
    [InlineData("", "x", KeywordMode.NotContains, true)]
    public void Keyword_matching_is_exact(string body, string keyword, KeywordMode mode, bool satisfied)
    {
        KeywordMatcher.IsSatisfied(body, keyword, mode).Should().Be(satisfied);
    }

    [Theory]
    [InlineData(31 * 86_400, 31, false, false)]
    [InlineData(30 * 86_400 + 1, 30, false, true)]
    [InlineData(7 * 86_400, 7, false, true)]
    [InlineData(86_400 - 1, 0, false, true)]
    [InlineData(1, 0, false, true)]
    [InlineData(0, 0, true, true)]
    [InlineData(-1, -1, true, false)]
    [InlineData(-3 * 86_400, -3, true, false)]
    public void Ssl_days_are_whole_days_left(int secondsLeft, int days, bool expired, bool warn)
    {
        var expiresAt = Now.AddSeconds(secondsLeft);

        SslExpiry.DaysLeft(expiresAt, Now).Should().Be(days);
        SslExpiry.IsExpired(expiresAt, Now).Should().Be(expired);
        SslExpiry.NeedsWarning(SslExpiry.DaysLeft(expiresAt, Now)).Should().Be(warn);
    }

    [Theory]
    [InlineData(10, SslPolicyErrors.None, null)]
    [InlineData(0.5, SslPolicyErrors.None, null)]
    [InlineData(-1, SslPolicyErrors.RemoteCertificateChainErrors, "Certificate expired on 2026-11-24.")]
    [InlineData(10, SslPolicyErrors.RemoteCertificateNameMismatch, "Certificate doesn't match the host name.")]
    [InlineData(10, SslPolicyErrors.RemoteCertificateChainErrors, "Certificate isn't trusted.")]
    [InlineData(10, SslPolicyErrors.RemoteCertificateNotAvailable, "The server presented no certificate.")]
    public void Ssl_check_is_up_only_for_a_valid_certificate(double days, SslPolicyErrors errors, string? error)
    {
        var result = SslCheckRunner.Evaluate(Now.AddDays(days), errors, Now, 12);

        result.IsUp.Should().Be(error is null);
        result.Error.Should().Be(error);
        result.CertificateExpiresAt.Should().Be(Now.AddDays(days));
    }
}
