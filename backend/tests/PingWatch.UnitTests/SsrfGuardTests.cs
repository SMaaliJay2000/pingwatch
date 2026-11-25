using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PingWatch.Application.Checks;
using PingWatch.Domain;
using PingWatch.Infrastructure.Checks;
using static PingWatch.UnitTests.CheckTestSupport;

namespace PingWatch.UnitTests;

public class SsrfGuardTests
{
    [Theory]
    // IPv4: private, loopback, link-local and metadata, with range edges.
    [InlineData("10.0.0.0")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.0")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.0.0")]
    [InlineData("192.168.255.255")]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.254")]
    [InlineData("169.254.0.1")]
    [InlineData("169.254.169.254")]
    // Further reserved ranges that can also reach internal services.
    [InlineData("0.0.0.0")]
    [InlineData("100.64.0.1")]
    [InlineData("100.100.100.200")]
    [InlineData("168.63.129.16")]
    [InlineData("192.0.0.8")]
    [InlineData("198.18.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    // IPv6.
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fd00:ec2::254")]
    [InlineData("fdff:ffff::1")]
    [InlineData("fe80::1")]
    [InlineData("fec0::1")]
    [InlineData("ff02::1")]
    // IPv4 hidden inside IPv6.
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::127.0.0.1")]
    [InlineData("64:ff9b::a9fe:a9fe")]
    [InlineData("2002:7f00:1::")]
    [InlineData("2001::1")]
    [InlineData("2001:db8::1")]
    public void Internal_and_reserved_addresses_are_blocked(string address)
    {
        SsrfGuard.IsBlockedAddress(IPAddress.Parse(address)).Should().BeTrue();
    }

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("9.255.255.255")]
    [InlineData("11.0.0.0")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.0")]
    [InlineData("126.255.255.255")]
    [InlineData("128.0.0.1")]
    [InlineData("169.253.255.255")]
    [InlineData("169.255.0.0")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.0")]
    [InlineData("192.167.255.255")]
    [InlineData("192.169.0.0")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2a00:1450:4001::200e")]
    public void Public_addresses_are_allowed(string address)
    {
        SsrfGuard.IsBlockedAddress(IPAddress.Parse(address)).Should().BeFalse();
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("8.8.8.8", "127.0.0.1")]
    [InlineData("2606:4700:4700::1111", "fd00::1")]
    public async Task A_name_with_any_internal_address_is_rejected(params string[] answer)
    {
        var guard = new SsrfGuard(new FakeDns().Answer("target.test", answer));

        var connect = () => guard.ConnectAsync("target.test", 80, CancellationToken.None);

        await connect.Should().ThrowAsync<SsrfBlockedException>();
    }

    [Fact]
    public async Task Unknown_and_empty_names_look_the_same_as_blocked_ones()
    {
        var guard = new SsrfGuard(new FakeDns().Answer("empty.test", [[]]));

        foreach (var host in new[] { "empty.test", "missing.test" })
        {
            var connect = () => guard.ConnectAsync(host, 80, CancellationToken.None);

            (await connect.Should().ThrowAsync<SsrfBlockedException>())
                .WithMessage(SsrfGuard.BlockedMessage);
        }
    }

    /// <summary>Real DNS, real sockets and the production rules.</summary>
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("localhost.")]
    [InlineData("127.1")]
    [InlineData("2130706433")]
    [InlineData("0x7f000001")]
    [InlineData("0177.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("[::1]")]
    [InlineData("[::ffff:127.0.0.1]")]
    public async Task No_spelling_of_loopback_reaches_a_local_server(string host)
    {
        await using var server = new TestHttpServer(_ => new Reply(Body: "secret"));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCheckEngine();
        await using var engine = services.BuildServiceProvider();
        var runner = engine.GetServices<ICheckRunner>().Single(r => r.Type == MonitorType.Keyword);

        var result = await runner.RunAsync(
            KeywordJob($"http://{host}:{server.Port}/", "secret"), CancellationToken.None);

        result.IsUp.Should().BeFalse();
        result.Error.Should().Be(SsrfGuard.BlockedMessage);
        server.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://127.0.0.2:{port}/internal")]
    [InlineData("http://[::1]:{port}/internal")]
    [InlineData("http://internal.test:{port}/internal")]
    public async Task A_redirect_to_an_internal_address_is_blocked(string location)
    {
        TestHttpServer? server = null;
        server = new TestHttpServer(_ => new Reply(302, Location: location.Replace("{port}", $"{server!.Port}")));
        await using var owned = server;

        var result = await RunAsync(
            HttpJob(server.Url()), new FakeDns().Answer("internal.test", ["192.168.1.10"]));

        result.IsUp.Should().BeFalse();
        result.Error.Should().Be(SsrfGuard.BlockedMessage);
        server.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task Dns_is_resolved_once_per_connection_and_again_for_every_check()
    {
        // A rebinding name: a permitted address first, an internal one afterwards.
        await using var server = new TestHttpServer(_ => new Reply());
        var dns = new FakeDns().Answer("rebind.test", ["127.0.0.1"], ["10.0.0.1"]);
        await using var engine = Engine(new SsrfGuard(dns, AllowTestServer));
        var runner = engine.GetServices<ICheckRunner>().Single(r => r.Type == MonitorType.Http);
        var job = HttpJob($"http://rebind.test:{server.Port}/");

        var first = await runner.RunAsync(job, CancellationToken.None);
        var callsAfterFirst = dns.Calls;
        var second = await runner.RunAsync(job, CancellationToken.None);

        first.IsUp.Should().BeTrue(first.Error);
        callsAfterFirst.Should().Be(1, "the socket must use the address that was checked");
        second.Error.Should().Be(SsrfGuard.BlockedMessage, "a pooled connection would skip the check");
    }

    [Fact]
    public void The_check_handler_cannot_be_routed_around_the_guard()
    {
        using var handler = CheckHttpHandler.Create(new SsrfGuard(new FakeDns()));

        handler.UseProxy.Should().BeFalse();
        handler.AllowAutoRedirect.Should().BeFalse();
        handler.UseCookies.Should().BeFalse();
        handler.PooledConnectionLifetime.Should().Be(TimeSpan.Zero);
        handler.ConnectCallback.Should().NotBeNull();
    }

    [Theory]
    [InlineData(MonitorType.Tcp)]
    [InlineData(MonitorType.Ssl)]
    public async Task Tcp_and_ssl_checks_use_the_guard_too(MonitorType type)
    {
        await using var server = new TestHttpServer(_ => new Reply());

        var result = await RunAsync(
            SocketJob(type, "127.0.0.1", server.Port), isBlocked: SsrfGuard.IsBlockedAddress);

        result.Error.Should().Be(SsrfGuard.BlockedMessage);
    }
}
