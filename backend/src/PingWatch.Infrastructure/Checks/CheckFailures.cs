using System.Net.Sockets;
using System.Security.Authentication;
using PingWatch.Application.Checks;

namespace PingWatch.Infrastructure.Checks;

// Users see these messages, so they never include exception text: it can contain internal addresses.
internal static class CheckFailures
{
    public static CheckRunResult TimedOut(CheckJob job) =>
        CheckRunResult.Down($"Timed out after {job.TimeoutMs / 1000.0:0.#} s.", job.TimeoutMs);

    public static string Describe(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case SsrfBlockedException:
                    return SsrfGuard.BlockedMessage;
                case AuthenticationException:
                    return "TLS handshake failed. Check the certificate.";
                case SocketException socket:
                    return socket.SocketErrorCode == SocketError.ConnectionRefused
                        ? "Connection refused."
                        : "Connection failed.";
            }
        }

        return exception is HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError }
            ? "TLS handshake failed. Check the certificate."
            : "The request failed.";
    }
}