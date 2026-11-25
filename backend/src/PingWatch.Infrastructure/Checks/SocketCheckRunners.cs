using System.Diagnostics;
using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using PingWatch.Application.Checks;
using PingWatch.Domain;

namespace PingWatch.Infrastructure.Checks;

public sealed class TcpCheckRunner(SsrfGuard guard) : ICheckRunner
{
    public MonitorType Type => MonitorType.Tcp;

    public async Task<CheckRunResult> RunAsync(CheckJob job, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(job.TimeoutMs);
        var watch = Stopwatch.StartNew();

        try
        {
            using var socket = await guard.ConnectAsync(job.Host!, job.Port!.Value, timeout.Token);
            return new CheckRunResult(true, ResponseMs: (int)watch.ElapsedMilliseconds);
        }
        catch (Exception) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return CheckFailures.TimedOut(job);
        }
        catch (Exception exception) when (exception is SocketException or SsrfBlockedException)
        {
            return CheckRunResult.Down(CheckFailures.Describe(exception), (int)watch.ElapsedMilliseconds);
        }
    }
}

public sealed class SslCheckRunner(SsrfGuard guard, TimeProvider time) : ICheckRunner
{
    public MonitorType Type => MonitorType.Ssl;

    public async Task<CheckRunResult> RunAsync(CheckJob job, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(job.TimeoutMs);
        var watch = Stopwatch.StartNew();

        try
        {
            var socket = await guard.ConnectAsync(job.Host!, job.Port ?? 443, timeout.Token);
            await using var network = new NetworkStream(socket, ownsSocket: true);
            await using var tls = new SslStream(network);

            DateTimeOffset? expiresAt = null;
            var errors = SslPolicyErrors.RemoteCertificateNotAvailable;

            await tls.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions
                {
                    TargetHost = job.Host,
                    // Accept the handshake so the certificate can be examined; Evaluate judges it.
                    RemoteCertificateValidationCallback = (_, certificate, _, policyErrors) =>
                    {
                        errors = policyErrors;
                        if (certificate is X509Certificate2 x509)
                        {
                            expiresAt = new DateTimeOffset(x509.NotAfter.ToUniversalTime(), TimeSpan.Zero);
                        }

                        return true;
                    },
                },
                timeout.Token);

            return Evaluate(expiresAt, errors, time.GetUtcNow(), (int)watch.ElapsedMilliseconds);
        }
        catch (Exception) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return CheckFailures.TimedOut(job);
        }
        catch (Exception exception) when (exception is SocketException or SsrfBlockedException
            or AuthenticationException or IOException)
        {
            var message = exception is IOException
                ? "TLS handshake failed."
                : CheckFailures.Describe(exception);
            return CheckRunResult.Down(message, (int)watch.ElapsedMilliseconds);
        }
    }

    /// <summary>Up when the certificate is unexpired, matches the host name and chains to a trusted root.</summary>
    public static CheckRunResult Evaluate(
        DateTimeOffset? expiresAt, SslPolicyErrors errors, DateTimeOffset now, int responseMs)
    {
        string? error = null;

        if (expiresAt is not { } expiry || errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
        {
            error = "The server presented no certificate.";
        }
        else if (SslExpiry.IsExpired(expiry, now))
        {
            error = $"Certificate expired on {expiry.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.";
        }
        else if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
        {
            error = "Certificate doesn't match the host name.";
        }
        else if (errors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
        {
            error = "Certificate isn't trusted.";
        }

        return new CheckRunResult(
            error is null, ResponseMs: responseMs, Error: error, CertificateExpiresAt: expiresAt);
    }
}