using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using PingWatch.Application.Auth;

namespace PingWatch.IntegrationTests;

internal static partial class AuthTestHelpers
{
    public const string Password = "correct horse battery";

    public static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    public static Task<HttpResponseMessage> RegisterAsync(
        this HttpClient client, string email, string password = Password, string? accountName = null) =>
        client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, password, accountName));

    public static Task<HttpResponseMessage> LoginAsync(
        this HttpClient client, string email, string password = Password) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));

    public static Task<HttpResponseMessage> RefreshAsync(this HttpClient client) =>
        client.PostAsync("/api/v1/auth/refresh", null);

    /// <summary>Sends exactly this cookie. Use with a client that doesn't manage cookies itself.</summary>
    public static Task<HttpResponseMessage> RefreshWithAsync(this HttpClient client, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("Cookie", cookie);
        return client.SendAsync(request);
    }

    /// <summary>The "name=value" part of the refresh cookie.</summary>
    public static string RefreshCookiePair(this HttpResponseMessage response) =>
        response.RefreshSetCookie().Split(';')[0];

    public static string RefreshSetCookie(this HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("pw_refresh="));

    /// <summary>Query values of the link in the newest email to this address with this subject.</summary>
    public static Dictionary<string, StringValues> LinkQuery(this ApiFactory factory, string email, string subject)
    {
        var message = factory.Email.SentTo(email).Last(m => m.Subject.Contains(subject));
        var link = new Uri(LinkPattern().Match(message.TextBody).Value);

        return QueryHelpers.ParseQuery(link.Query);
    }

    public static Task<HttpResponseMessage> VerifyAsync(this HttpClient client, ApiFactory factory, string email)
    {
        var query = factory.LinkQuery(email, "Verify");

        return client.PostAsJsonAsync(
            "/api/v1/auth/verify-email",
            new VerifyEmailRequest(Guid.Parse(query["userId"].ToString()), query["token"].ToString()));
    }

    /// <summary>Registers, verifies and logs in, leaving the bearer token and refresh cookie on the client.</summary>
    public static async Task<HttpResponseMessage> SignUpAsync(
        this HttpClient client, ApiFactory factory, string email, string? accountName = null)
    {
        (await client.RegisterAsync(email, accountName: accountName)).EnsureSuccessStatusCode();
        (await client.VerifyAsync(factory, email)).EnsureSuccessStatusCode();

        var login = await client.LoginAsync(email);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        await client.UseAccessTokenAsync(login);

        return login;
    }

    public static async Task UseAccessTokenAsync(this HttpClient client, HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<AccessTokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    [GeneratedRegex(@"https://app\.test\S+")]
    private static partial Regex LinkPattern();
}
