using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Text;
using PingWatch.Application.Checks;
using PingWatch.Domain;

namespace PingWatch.Infrastructure.Checks;

public class HttpCheckRunner(IHttpClientFactory clients) : ICheckRunner
{
    public const string ClientName = "checks";
    public const int MaxRedirects = 5;
    private const string UserAgent = "PingWatch/1.0";

    public virtual MonitorType Type => MonitorType.Http;

    public async Task<CheckRunResult> RunAsync(CheckJob job, CancellationToken cancellationToken)
    {
        if (!StatusCodeRanges.TryParse(job.ExpectedStatus, out var expected))
        {
            return CheckRunResult.Down("The expected status setting is invalid.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(job.TimeoutMs);
        var watch = Stopwatch.StartNew();

        try
        {
            using var response = await SendFollowingRedirectsAsync(job, timeout.Token);
            var responseMs = (int)watch.ElapsedMilliseconds;
            var status = (int)response.StatusCode;

            if (!expected.Contains(status))
            {
                return new CheckRunResult(false, status, responseMs, $"HTTP {status}");
            }

            var bodyError = await CheckBodyAsync(job, response, timeout.Token);
            return new CheckRunResult(bodyError is null, status, responseMs, bodyError);
        }
        catch (Exception) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return CheckFailures.TimedOut(job);
        }
        catch (RedirectException exception)
        {
            return CheckRunResult.Down(exception.Message, (int)watch.ElapsedMilliseconds);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UriFormatException
            or InvalidDataException)
        {
            return CheckRunResult.Down(CheckFailures.Describe(exception), (int)watch.ElapsedMilliseconds);
        }
    }

    /// <summary>Returns why the body makes the target down, or null if it's fine.</summary>
    protected virtual Task<string?> CheckBodyAsync(
        CheckJob job, HttpResponseMessage response, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);

    private async Task<HttpResponseMessage> SendFollowingRedirectsAsync(
        CheckJob job, CancellationToken cancellationToken)
    {
        var client = clients.CreateClient(ClientName);
        var uri = new Uri(job.Url!);
        var method = job.Method switch
        {
            CheckMethod.Head => HttpMethod.Head,
            CheckMethod.Post => HttpMethod.Post,
            _ => HttpMethod.Get,
        };

        for (var redirects = 0; ; redirects++)
        {
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            request.Headers.Accept.ParseAdd("*/*");

            var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            var status = response.StatusCode;
            if (status is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
                    or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                || response.Headers.Location is not { } location)
            {
                return response;
            }

            response.Dispose();

            if (redirects == MaxRedirects)
            {
                throw new RedirectException($"More than {MaxRedirects} redirects.");
            }

            uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new RedirectException("Redirected to an address that isn't http or https.");
            }

            // The same method changes browsers make.
            if ((status == HttpStatusCode.SeeOther && method != HttpMethod.Head)
                || (status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found && method == HttpMethod.Post))
            {
                method = HttpMethod.Get;
            }
        }
    }

    private sealed class RedirectException(string message) : Exception(message);
}

public sealed class KeywordCheckRunner(IHttpClientFactory clients) : HttpCheckRunner(clients)
{
    public const int MaxBodyBytes = 1024 * 1024;

    public override MonitorType Type => MonitorType.Keyword;

    protected override async Task<string?> CheckBodyAsync(
        CheckJob job, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(MaxBodyBytes);
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            // Counted after decompression, so a small "zip bomb" can't exceed the limit either.
            var length = await stream.ReadAtLeastAsync(
                buffer.AsMemory(0, MaxBodyBytes), MaxBodyBytes, throwOnEndOfStream: false, cancellationToken);
            var body = GetEncoding(response.Content.Headers.ContentType?.CharSet).GetString(buffer, 0, length);

            var mode = job.KeywordMode ?? KeywordMode.Contains;
            return KeywordMatcher.IsSatisfied(body, job.Keyword!, mode)
                ? null
                : mode == KeywordMode.Contains ? "Keyword not found." : "Keyword found, but it should be absent.";
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static Encoding GetEncoding(string? charset)
    {
        try
        {
            return string.IsNullOrWhiteSpace(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset.Trim('"'));
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }
}