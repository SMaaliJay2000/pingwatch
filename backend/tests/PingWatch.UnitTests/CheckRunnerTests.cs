using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PingWatch.Domain;
using static PingWatch.UnitTests.CheckTestSupport;

namespace PingWatch.UnitTests;

public class CheckRunnerTests
{
    private const int OneMegabyte = 1024 * 1024;

    [Theory]
    [InlineData(200, "200-399", true, null)]
    [InlineData(503, "200-399", false, "HTTP 503")]
    [InlineData(418, "418", true, null)]
    [InlineData(200, "201-204", false, "HTTP 200")]
    public async Task Http_status_decides_the_result(int status, string expected, bool up, string? error)
    {
        await using var server = new TestHttpServer(_ => new Reply(status));

        var result = await RunAsync(HttpJob(server.Url(), expected: expected));

        result.IsUp.Should().Be(up);
        result.StatusCode.Should().Be(status);
        result.Error.Should().Be(error);
        result.ResponseMs.Should().NotBeNull();
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public async Task At_most_five_redirects_are_followed(int redirects, bool up)
    {
        // /3 redirects to /2, and so on; /0 answers 200.
        await using var server = new TestHttpServer(path =>
        {
            var left = int.Parse(path.TrimStart('/'));
            return left == 0 ? new Reply() : new Reply(302, Location: $"/{left - 1}");
        });

        var result = await RunAsync(HttpJob(server.Url($"/{redirects}")));

        result.IsUp.Should().Be(up, result.Error);
        server.Requests.Should().HaveCount(6);
        if (!up)
        {
            result.Error.Should().Be("More than 5 redirects.");
        }
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/")]
    [InlineData("gopher://127.0.0.1:6379/_FLUSHALL")]
    public async Task Redirects_to_other_schemes_are_refused(string location)
    {
        await using var server = new TestHttpServer(_ => new Reply(302, Location: location));

        var result = await RunAsync(HttpJob(server.Url()));

        result.Error.Should().Be("Redirected to an address that isn't http or https.");
    }

    [Theory]
    [InlineData(CheckMethod.Post, 303, "POST /", "GET /next")]
    [InlineData(CheckMethod.Post, 302, "POST /", "GET /next")]
    [InlineData(CheckMethod.Post, 307, "POST /", "POST /next")]
    [InlineData(CheckMethod.Head, 303, "HEAD /", "HEAD /next")]
    public async Task Redirects_change_the_method_the_way_browsers_do(
        CheckMethod method, int status, string first, string second)
    {
        await using var server = new TestHttpServer(
            path => path == "/" ? new Reply(status, Location: "/next") : new Reply());

        await RunAsync(HttpJob(server.Url(), method));

        server.Requests.Should().Equal(first, second);
    }

    [Fact]
    public async Task A_slow_target_times_out()
    {
        await using var server = new TestHttpServer(_ => new Reply(DelayMs: 10_000));

        var result = await RunAsync(HttpJob(server.Url(), timeoutMs: 1_000));

        result.IsUp.Should().BeFalse();
        result.Error.Should().Be("Timed out after 1 s.");
        result.ResponseMs.Should().Be(1_000);
    }

    [Fact]
    public async Task Shutting_down_cancels_instead_of_reporting_downtime()
    {
        await using var server = new TestHttpServer(_ => new Reply(DelayMs: 10_000));
        using var shutdown = new CancellationTokenSource(300);

        var run = () => RunAsync(HttpJob(server.Url()), cancellationToken: shutdown.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task A_closed_port_is_reported_without_internal_details()
    {
        var port = ClosedPort();

        var http = await RunAsync(HttpJob($"http://127.0.0.1:{port}/"));
        var tcp = await RunAsync(SocketJob(MonitorType.Tcp, "127.0.0.1", port));

        foreach (var result in new[] { http, tcp })
        {
            result.IsUp.Should().BeFalse();
            result.Error.Should().Be("Connection refused.");
        }
    }

    [Fact]
    public async Task Tcp_is_up_when_the_port_opens()
    {
        await using var server = new TestHttpServer(_ => new Reply());

        var result = await RunAsync(SocketJob(MonitorType.Tcp, "127.0.0.1", server.Port));

        result.IsUp.Should().BeTrue(result.Error);
        result.ResponseMs.Should().NotBeNull();
    }

    [Theory]
    [InlineData("All systems go", KeywordMode.Contains, true, null)]
    [InlineData("Maintenance", KeywordMode.Contains, false, "Keyword not found.")]
    [InlineData("All systems go", KeywordMode.NotContains, false, "Keyword found, but it should be absent.")]
    [InlineData("Maintenance", KeywordMode.NotContains, true, null)]
    public async Task Keyword_check_reads_the_body(string keyword, KeywordMode mode, bool up, string? error)
    {
        await using var server = new TestHttpServer(_ => new Reply(Body: "<h1>All systems go</h1>"));

        var result = await RunAsync(KeywordJob(server.Url(), keyword, mode));

        result.IsUp.Should().Be(up);
        result.Error.Should().Be(error);
    }

    [Theory]
    [InlineData(OneMegabyte - 6, true)]
    [InlineData(OneMegabyte - 5, false)]
    [InlineData(3 * OneMegabyte, false)]
    public async Task Only_the_first_megabyte_is_searched(int keywordStartsAt, bool found)
    {
        await using var server = new TestHttpServer(
            _ => new Reply(Body: new string('a', keywordStartsAt) + "needle"));

        var result = await RunAsync(KeywordJob(server.Url(), "needle"));

        result.IsUp.Should().Be(found, result.Error);
    }

    [Fact]
    public async Task The_limit_applies_to_decompressed_bytes()
    {
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes("first-marker" + new string('a', 20 * OneMegabyte) + "last-marker"));
        }

        await using var server = new TestHttpServer(
            _ => new Reply(RawBody: compressed.ToArray(), Headers: ["Content-Encoding: gzip"]));

        (await RunAsync(KeywordJob(server.Url(), "first-marker"))).IsUp.Should().BeTrue();
        (await RunAsync(KeywordJob(server.Url(), "last-marker"))).IsUp.Should().BeFalse();
    }

    [Fact]
    public async Task The_body_is_decoded_with_the_declared_charset()
    {
        await using var server = new TestHttpServer(_ => new Reply(
            RawBody: Encoding.Latin1.GetBytes("café"),
            Headers: ["Content-Type: text/plain; charset=iso-8859-1"]));

        (await RunAsync(KeywordJob(server.Url(), "café"))).IsUp.Should().BeTrue();
    }

    [Fact]
    public async Task Keyword_check_still_requires_an_expected_status()
    {
        await using var server = new TestHttpServer(_ => new Reply(500, Body: "needle"));

        var result = await RunAsync(KeywordJob(server.Url(), "needle"));

        result.Error.Should().Be("HTTP 500");
    }

    [Theory]
    [InlineData("localhost", 100, "Certificate isn't trusted.")]
    [InlineData("localhost", -2, "Certificate expired on")]
    [InlineData("other.test", 100, "Certificate doesn't match the host name.")]
    public async Task Ssl_check_judges_the_servers_certificate(string certificateName, int daysLeft, string error)
    {
        var notAfter = DateTimeOffset.UtcNow.AddDays(daysLeft);
        using var certificate = Certificate(certificateName, notAfter);
        await using var server = new TestTlsServer(certificate);

        var result = await RunAsync(
            SocketJob(MonitorType.Ssl, "localhost", server.Port),
            new FakeDns().Answer("localhost", ["127.0.0.1"]));

        result.IsUp.Should().BeFalse();
        result.Error.Should().StartWith(error);
        result.CertificateExpiresAt.Should().BeCloseTo(notAfter, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Ssl_check_fails_cleanly_against_a_server_without_tls()
    {
        await using var server = new TestHttpServer(_ => new Reply());

        var result = await RunAsync(SocketJob(MonitorType.Ssl, "127.0.0.1", server.Port, timeoutMs: 3_000));

        result.IsUp.Should().BeFalse();
        result.CertificateExpiresAt.Should().BeNull();
    }

    private static int ClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
