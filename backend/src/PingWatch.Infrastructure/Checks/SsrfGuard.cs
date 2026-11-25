using System.Net;
using System.Net.Sockets;

namespace PingWatch.Infrastructure.Checks;

public interface IDnsResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

internal sealed class SystemDnsResolver : IDnsResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}

public sealed class SsrfBlockedException() : Exception(SsrfGuard.BlockedMessage);

/// <summary>
/// The only way a check may open a connection. It resolves the name, rejects private and reserved
/// addresses, then connects to the exact address it checked, so a second DNS answer can't swap in
/// an internal one (DNS rebinding).
/// </summary>
public sealed class SsrfGuard(IDnsResolver dns, Func<IPAddress, bool> isBlocked)
{
    // Deliberately the same for "doesn't resolve" and "resolves to something internal",
    // so monitors can't be used to discover internal host names.
    public const string BlockedMessage = "The host could not be resolved to a public address.";

    private static readonly IPNetwork[] BlockedIPv4 =
    [
        IPNetwork.Parse("0.0.0.0/8"),       // "this network"; 0.0.0.0 reaches localhost on Linux
        IPNetwork.Parse("10.0.0.0/8"),      // private
        IPNetwork.Parse("100.64.0.0/10"),   // carrier-grade NAT, used by some cloud metadata services
        IPNetwork.Parse("127.0.0.0/8"),     // loopback
        IPNetwork.Parse("169.254.0.0/16"),  // link-local and cloud metadata
        IPNetwork.Parse("172.16.0.0/12"),   // private
        IPNetwork.Parse("192.0.0.0/24"),    // protocol assignments
        IPNetwork.Parse("192.0.2.0/24"),    // documentation
        IPNetwork.Parse("192.88.99.0/24"),  // 6to4 relay
        IPNetwork.Parse("192.168.0.0/16"),  // private
        IPNetwork.Parse("198.18.0.0/15"),   // benchmarking
        IPNetwork.Parse("198.51.100.0/24"), // documentation
        IPNetwork.Parse("203.0.113.0/24"),  // documentation
        IPNetwork.Parse("224.0.0.0/4"),     // multicast
        IPNetwork.Parse("240.0.0.0/4"),     // reserved and broadcast
        IPNetwork.Parse("168.63.129.16/32"), // Azure platform address
    ];

    // IPv6 is an allow-list: only global unicast is accepted. That already rejects ::1, ::,
    // fc00::/7, fe80::/10, multicast and NAT64.
    private static readonly IPNetwork GlobalUnicastIPv6 = IPNetwork.Parse("2000::/3");

    private static readonly IPNetwork[] BlockedGlobalIPv6 =
    [
        IPNetwork.Parse("2001::/23"),     // protocol assignments, including Teredo
        IPNetwork.Parse("2001:db8::/32"), // documentation
        IPNetwork.Parse("2002::/16"),     // 6to4, which embeds any IPv4 address
        IPNetwork.Parse("3fff::/20"),     // documentation
    ];

    public SsrfGuard(IDnsResolver dns)
        : this(dns, IsBlockedAddress)
    {
    }

    public static bool IsBlockedAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => BlockedIPv4.Any(network => network.Contains(address)),
            AddressFamily.InterNetworkV6 =>
                !GlobalUnicastIPv6.Contains(address) || BlockedGlobalIPv6.Any(network => network.Contains(address)),
            _ => true,
        };
    }

    public async Task<Socket> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        var addresses = await ResolveAsync(host, cancellationToken);

        // One bad address rejects the host: it must not matter which one would be picked.
        if (addresses.Length == 0 || addresses.Any(isBlocked))
        {
            throw new SsrfBlockedException();
        }

        for (var i = 0; ; i++)
        {
            var socket = new Socket(addresses[i].AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
            };

            try
            {
                await socket.ConnectAsync(new IPEndPoint(addresses[i], port), cancellationToken);
                return socket;
            }
            catch (SocketException) when (i < addresses.Length - 1)
            {
                socket.Dispose();
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }

    private async Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            return [literal];
        }

        try
        {
            return await dns.ResolveAsync(host, cancellationToken);
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException)
        {
            throw new SsrfBlockedException();
        }
    }
}