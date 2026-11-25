using System.Net;
using System.Net.Sockets;

namespace PingWatch.Infrastructure.Checks;

public static class CheckHttpHandler
{
    // Every setting here is part of the SSRF protection or of measuring a fresh connection.
    public static SocketsHttpHandler Create(SsrfGuard guard) => new()
    {
        // Redirects are followed by hand, so each hop is validated and counted.
        AllowAutoRedirect = false,
        // With a proxy (even from HTTP_PROXY) the guard would only see the proxy's address.
        UseProxy = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All,
        // No connection reuse: every check resolves DNS and passes the guard again.
        PooledConnectionLifetime = TimeSpan.Zero,
        MaxResponseDrainSize = 0,
        ActivityHeadersPropagator = null,
        ConnectCallback = async (context, cancellationToken) => new NetworkStream(
            await guard.ConnectAsync(context.DnsEndPoint.Host, context.DnsEndPoint.Port, cancellationToken),
            ownsSocket: true),
    };
}