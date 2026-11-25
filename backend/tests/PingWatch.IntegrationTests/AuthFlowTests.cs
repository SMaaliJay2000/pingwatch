using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using PingWatch.Application.Auth;
using PingWatch.Infrastructure.Persistence;

namespace PingWatch.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuthFlowTests(ApiFactory factory)
{
    private const string NewPassword = "a different long password";

    [Fact]
    public async Task Register_verify_login_refresh()
    {
        var client = factory.CreateApiClient();
        var email = AuthTestHelpers.NewEmail();

        (await client.RegisterAsync(email)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.LoginAsync(email)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.VerifyAsync(factory, email)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var login = await client.LoginAsync(email);

        login.StatusCode.Should().Be(HttpStatusCode.OK);
        login.Headers.CacheControl!.NoStore.Should().BeTrue();
        login.RefreshSetCookie().ToLowerInvariant().Should()
            .Contain("httponly").And.Contain("secure").And.Contain("samesite=strict")
            .And.Contain("path=/api/v1/auth").And.Contain("expires=");
        (await login.Content.ReadAsStringAsync()).Should().NotContain(
            login.RefreshCookiePair().Split('=')[1], "the refresh token must only travel in the cookie");

        var accessToken = (await login.Content.ReadFromJsonAsync<AccessTokenResponse>())!.AccessToken;
        var jwt = new JsonWebToken(accessToken);
        (jwt.ValidTo - jwt.IssuedAt).Should().Be(TimeSpan.FromMinutes(15));

        var refresh = await client.RefreshAsync();

        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
        refresh.RefreshCookiePair().Should().NotBe(login.RefreshCookiePair());

        await client.UseAccessTokenAsync(refresh);
        (await client.GetAsync("/api/v1/account")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reusing_a_rotated_refresh_token_ends_the_whole_session()
    {
        var client = factory.CreateApiClient(handleCookies: false);
        var email = AuthTestHelpers.NewEmail();
        var login = await client.SignUpAsync(factory, email);
        var original = login.RefreshCookiePair();

        var rotated = await client.RefreshWithAsync(original);
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.RefreshWithAsync(original)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.RefreshWithAsync(rotated.RefreshCookiePair()))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reuse_in_one_session_leaves_other_sessions_signed_in()
    {
        var client = factory.CreateApiClient(handleCookies: false);
        var email = AuthTestHelpers.NewEmail();
        var firstDevice = (await client.SignUpAsync(factory, email)).RefreshCookiePair();
        var secondDevice = (await client.LoginAsync(email)).RefreshCookiePair();

        await client.RefreshWithAsync(firstDevice);
        await client.RefreshWithAsync(firstDevice);

        (await client.RefreshWithAsync(secondDevice)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Only_one_of_several_simultaneous_refreshes_succeeds()
    {
        var client = factory.CreateApiClient(handleCookies: false);
        var cookie = (await client.SignUpAsync(factory, AuthTestHelpers.NewEmail())).RefreshCookiePair();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.RefreshWithAsync(cookie)));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized).Should().Be(4);
    }

    [Fact]
    public async Task Expired_refresh_token_is_rejected()
    {
        var client = factory.CreateApiClient();
        var email = AuthTestHelpers.NewEmail();
        await client.SignUpAsync(factory, email);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PingWatchDbContext>();
            var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
            await db.RefreshTokens.Where(t => t.UserId == userId).ExecuteUpdateAsync(
                s => s.SetProperty(t => t.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }

        (await client.RefreshAsync()).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("pw_refresh=not-a-real-token")]
    public async Task Refresh_without_a_valid_cookie_is_rejected(string? cookie)
    {
        var client = factory.CreateApiClient(handleCookies: false);

        var response = cookie is null ? await client.RefreshAsync() : await client.RefreshWithAsync(cookie);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_tokens_are_stored_only_as_hashes()
    {
        var client = factory.CreateApiClient();
        var login = await client.SignUpAsync(factory, AuthTestHelpers.NewEmail());
        var token = Uri.UnescapeDataString(login.RefreshCookiePair().Split('=')[1]);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PingWatchDbContext>();

        (await db.RefreshTokens.AnyAsync(t => t.TokenHash == token)).Should().BeFalse();
    }

    [Fact]
    public async Task Registering_creates_one_free_account_with_an_owner()
    {
        var email = AuthTestHelpers.NewEmail();

        await factory.CreateApiClient().RegisterAsync(email, accountName: "Acme Ltd");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PingWatchDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(a => a.Id == user.AccountId);
        account.Name.Should().Be("Acme Ltd");
        account.Plan.Should().Be(Domain.PlanTier.Free);
        user.Role.Should().Be(Domain.UserRole.Owner);
        user.PasswordHash.Should().NotBeNullOrEmpty().And.NotContain(AuthTestHelpers.Password);
    }

    [Fact]
    public async Task Registering_an_existing_email_looks_the_same_and_creates_nothing()
    {
        var client = factory.CreateApiClient();
        var email = AuthTestHelpers.NewEmail();
        var first = await client.RegisterAsync(email);
        var accountName = $"orphan-check-{Guid.NewGuid():N}";

        var second = await client.RegisterAsync(email.ToUpperInvariant(), accountName: accountName);

        second.StatusCode.Should().Be(first.StatusCode);
        (await second.Content.ReadAsStringAsync()).Should().Be(await first.Content.ReadAsStringAsync());
        factory.Email.SentTo(email).Last().Subject.Should().Contain("already");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PingWatchDbContext>();
        (await db.Users.CountAsync(u => u.NormalizedEmail == email.ToUpperInvariant())).Should().Be(1);
        (await db.Accounts.IgnoreQueryFilters().AnyAsync(a => a.Name == accountName)).Should().BeFalse();
    }

    [Theory]
    [InlineData("not-an-email", AuthTestHelpers.Password, "email")]
    [InlineData("Name <a@example.com>", AuthTestHelpers.Password, "email")]
    [InlineData("valid@example.com", "short", "password")]
    public async Task Invalid_registration_returns_validation_problem(string email, string password, string field)
    {
        var response = await factory.CreateApiClient().RegisterAsync(email, password);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Should().ContainKey(field);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_are_indistinguishable()
    {
        var client = factory.CreateApiClient();
        var email = AuthTestHelpers.NewEmail();
        await client.SignUpAsync(factory, email);

        var wrongPassword = await client.LoginAsync(email, "definitely not the password");
        var unknownEmail = await client.LoginAsync(AuthTestHelpers.NewEmail());

        wrongPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknownEmail.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Title(wrongPassword)).Should().Be(await Title(unknownEmail));

        static async Task<string?> Title(HttpResponseMessage response) =>
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString();
    }

    [Fact]
    public async Task Logging_in_unverified_sends_a_new_verification_link()
    {
        var client = factory.CreateApiClient();
        var email = AuthTestHelpers.NewEmail();
        await client.RegisterAsync(email);

        await client.LoginAsync(email);

        factory.Email.SentTo(email).Count(m => m.Subject.Contains("Verify")).Should().Be(2);
        (await client.VerifyAsync(factory, email)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Bad_verification_token_is_rejected()
    {
        var client = factory.CreateApiClient();
        var email = AuthTestHelpers.NewEmail();
        await client.RegisterAsync(email);
        var userId = Guid.Parse(factory.LinkQuery(email, "Verify")["userId"].ToString());

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/verify-email", new VerifyEmailRequest(userId, "wrong"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.LoginAsync(email)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Password_reset_changes_the_password_ends_sessions_and_works_once()
    {
        var client = factory.CreateApiClient();
        var email = AuthTestHelpers.NewEmail();
        await client.SignUpAsync(factory, email);

        (await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest(email)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        var reset = new ResetPasswordRequest(
            email, factory.LinkQuery(email, "Reset your")["token"].ToString(), NewPassword);

        (await client.PostAsJsonAsync("/api/v1/auth/reset-password", reset))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.RefreshAsync()).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.LoginAsync(email)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.LoginAsync(email, NewPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/v1/auth/reset-password", reset))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Password_reset_also_verifies_an_unverified_email()
    {
        var client = factory.CreateApiClient();
        var email = AuthTestHelpers.NewEmail();
        await client.RegisterAsync(email);
        await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest(email));

        await client.PostAsJsonAsync(
            "/api/v1/auth/reset-password",
            new ResetPasswordRequest(
                email, factory.LinkQuery(email, "Reset your")["token"].ToString(), NewPassword));

        (await client.LoginAsync(email, NewPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Forgot_password_for_an_unknown_email_sends_nothing()
    {
        var email = AuthTestHelpers.NewEmail();

        var response = await factory.CreateApiClient()
            .PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest(email));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        factory.Email.SentTo(email).Should().BeEmpty();
    }

    [Fact]
    public async Task Sixth_login_in_a_minute_from_one_ip_is_blocked()
    {
        var ip = ApiFactory.NewIp();
        var client = factory.CreateApiClient(ip);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            (await client.LoginAsync(AuthTestHelpers.NewEmail()))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "attempt {0} is within the limit", attempt);
        }

        var blocked = await client.LoginAsync(AuthTestHelpers.NewEmail());

        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        blocked.Headers.RetryAfter.Should().NotBeNull();
        blocked.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await factory.CreateApiClient().LoginAsync(AuthTestHelpers.NewEmail()))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "other IPs have their own limit");
    }

    [Fact]
    public async Task Email_sending_endpoints_are_rate_limited_per_ip()
    {
        var client = factory.CreateApiClient();
        var request = new ForgotPasswordRequest(AuthTestHelpers.NewEmail());

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            (await client.PostAsJsonAsync("/api/v1/auth/forgot-password", request))
                .StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        (await client.PostAsJsonAsync("/api/v1/auth/forgot-password", request))
            .StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
