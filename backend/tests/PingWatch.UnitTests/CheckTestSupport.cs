using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using PingWatch.Application.Checks;
using PingWatch.Domain;
using PingWatch.Infrastructure.Checks;

namespace PingWatch.UnitTests;

internal sealed class FakeDns : IDnsResolver
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<IPAddress[]>> _answers = new();
    private int _calls;

    public int Calls => _calls;

    /// <summary>Each lookup of the host takes the next answer; the last one repeats.</summary>
    public FakeDns Answer(string host, params string[][] answers)
    {
        _answers[host] = new ConcurrentQueue<IPAddress[]>(
            answers.Select(a => a.Select(IPAddress.Parse).ToArray()));
        return this;
    }

    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        if (!_answers.TryGetValue(host, out var queue))
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }

        if (queue.Count > 1 && queue.TryDequeue(out var next))
        {
            return Task.FromResult(next);
        }

        queue.TryPeek(out var last);
        return Task.FromResult(last!);
    }
}

internal static class CheckTestSupport
{
    /// <summary>The real rules, except that the test servers on 127.0.0.1 are reachable.</summary>
    public static bool AllowTestServer(IPAddress address) =>
        !address.Equals(IPAddress.Loopback) && SsrfGuard.IsBlockedAddress(address);

    /// <summary>The production registrations with only the guard swapped.</summary>
    public static ServiceProvider Engine(SsrfGuard guard)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCheckEngine();
        services.AddSingleton(guard);
        return services.BuildServiceProvider();
    }

    public static async Task<CheckRunResult> RunAsync(
        CheckJob job, FakeDns? dns = null, Func<IPAddress, bool>? isBlocked = null,
        CancellationToken cancellationToken = default)
    {
        await using var engine = Engine(new SsrfGuard(dns ?? new FakeDns(), isBlocked ?? AllowTestServer));
        var runner = engine.GetServices<ICheckRunner>().Single(r => r.Type == job.Type);

        return await runner.RunAsync(job, cancellationToken);
    }

    public static CheckJob HttpJob(
        string url, CheckMethod method = CheckMethod.Get, string expected = "200-399", int timeoutMs = 5_000) =>
        new(Guid.NewGuid(), Guid.NewGuid(), MonitorType.Http, url, null, null, method, expected,
            null, null, timeoutMs);

    public static CheckJob KeywordJob(string url, string keyword, KeywordMode mode = KeywordMode.Contains) =>
        HttpJob(url) with { Type = MonitorType.Keyword, Keyword = keyword, KeywordMode = mode };

    public static CheckJob SocketJob(MonitorType type, string host, int port, int timeoutMs = 5_000) =>
        new(Guid.NewGuid(), Guid.NewGuid(), type, null, host, port, null, "200-399", null, null, timeoutMs);

    public static X509Certificate2 Certificate(string name, DateTimeOffset notAfter)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(name);
        request.CertificateExtensions.Add(names.Build());

        using var certificate = request.CreateSelfSigned(notAfter.AddDays(-400), notAfter);

        // Windows can't use an in-memory key for the server side of TLS, so reload it.
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);
    }
}

internal sealed record Reply(
    int Status = 200, string Body = "", string? Location = null, int DelayMs = 0, byte[]? RawBody = null,
    string[]? Headers = null);

/// <summary>A minimal HTTP server on 127.0.0.1 that records what it receives.</summary>
internal sealed class TestHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();

    public TestHttpServer(Func<string, Reply> handler)
    {
        _listener.Start();
        _ = AcceptAsync(handler);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public ConcurrentQueue<string> Requests { get; } = new();

    public string Url(string path = "/") => $"http://127.0.0.1:{Port}{path}";

    public ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        return ValueTask.CompletedTask;
    }

    private async Task AcceptAsync(Func<string, Reply> handler)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = ServeAsync(client, handler);
            }
        }
        catch (Exception) when (_stop.IsCancellationRequested)
        {
        }
    }

    private async Task ServeAsync(TcpClient client, Func<string, Reply> handler)
    {
        using var owned = client;
        try
        {
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(_stop.Token) ?? "";
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token)))
            {
            }

            var parts = requestLine.Split(' ');
            Requests.Enqueue($"{parts[0]} {parts[1]}");
            var reply = handler(parts[1]);
            await Task.Delay(reply.DelayMs, _stop.Token);

            var body = reply.RawBody ?? Encoding.UTF8.GetBytes(reply.Body);
            var head = new StringBuilder($"HTTP/1.1 {reply.Status} Test\r\nConnection: close\r\n")
                .Append($"Content-Length: {body.Length}\r\n");
            if (reply.Location is not null)
            {
                head.Append($"Location: {reply.Location}\r\n");
            }

            foreach (var header in reply.Headers ?? [])
            {
                head.Append(header).Append("\r\n");
            }

            await stream.WriteAsync(Encoding.ASCII.GetBytes(head.Append("\r\n").ToString()), _stop.Token);
            if (parts[0] != "HEAD")
            {
                await stream.WriteAsync(body, _stop.Token);
            }
        }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
        {
            // The client hung up, which some tests expect.
        }
    }
}

/// <summary>A TLS server on 127.0.0.1 presenting one certificate.</summary>
internal sealed class TestTlsServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public TestTlsServer(X509Certificate2 certificate)
    {
        _listener.Start();
        _ = Task.Run(async () =>
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync();
                await using var tls = new SslStream(client.GetStream());
                await tls.AuthenticateAsServerAsync(certificate);
                await Task.Delay(500);
            }
            catch (Exception exception) when (exception is IOException or SocketException
                or ObjectDisposedException or System.Security.Authentication.AuthenticationException)
            {
            }
        });
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public ValueTask DisposeAsync()
    {
        _listener.Stop();
        return ValueTask.CompletedTask;
    }
}
