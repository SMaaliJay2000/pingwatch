using System.Net;

namespace PingWatch.IntegrationTests;

[Collection(ApiCollection.Name)]
public class LogoutTests(ApiFactory factory)
{
    [Fact]
    public async Task Logout_ends_the_session_and_clears_the_cookie()
    {
        var client = factory.CreateApiClient(handleCookies: false);
        var cookie = (await client.SignUpAsync(factory, AuthTestHelpers.NewEmail())).RefreshCookiePair();

        var logout = await client.LogoutWithAsync(cookie);

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        logout.RefreshSetCookie().ToLowerInvariant().Should()
            .StartWith("pw_refresh=;").And.Contain("expires=thu, 01 jan 1970")
            .And.Contain("path=/api/v1/auth");
        (await client.RefreshWithAsync(cookie)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_works_without_an_access_token()
    {
        var client = factory.CreateApiClient(handleCookies: false);
        var cookie = (await client.SignUpAsync(factory, AuthTestHelpers.NewEmail())).RefreshCookiePair();
        client.DefaultRequestHeaders.Authorization = null;

        (await client.LogoutWithAsync(cookie)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.RefreshWithAsync(cookie)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_with_an_already_rotated_cookie_also_ends_the_newer_one()
    {
        var client = factory.CreateApiClient(handleCookies: false);
        var original = (await client.SignUpAsync(factory, AuthTestHelpers.NewEmail())).RefreshCookiePair();
        var newer = (await client.RefreshWithAsync(original)).RefreshCookiePair();

        await client.LogoutWithAsync(original);

        (await client.RefreshWithAsync(newer)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_leaves_other_devices_signed_in()
    {
        var client = factory.CreateApiClient(handleCookies: false);
        var email = AuthTestHelpers.NewEmail();
        var firstDevice = (await client.SignUpAsync(factory, email)).RefreshCookiePair();
        var secondDevice = (await client.LoginAsync(email)).RefreshCookiePair();

        await client.LogoutWithAsync(firstDevice);

        (await client.RefreshWithAsync(secondDevice)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("pw_refresh=not-a-real-token")]
    public async Task Logout_without_a_valid_session_still_succeeds(string? cookie)
    {
        var client = factory.CreateApiClient(handleCookies: false);

        var response = await client.LogoutWithAsync(cookie);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Logout_during_a_refresh_leaves_no_usable_token()
    {
        var email = AuthTestHelpers.NewEmail();
        await factory.CreateApiClient().SignUpAsync(factory, email);

        // A race, so try it repeatedly. Each login uses a new IP to stay under the rate limit.
        for (var attempt = 0; attempt < 25; attempt++)
        {
            var client = factory.CreateApiClient(handleCookies: false);
            var cookie = (await client.LoginAsync(email)).RefreshCookiePair();

            var refreshTask = client.RefreshWithAsync(cookie);
            // Vary the head start so logout lands at different points of the rotation.
            await Task.Delay(attempt % 5);
            var logoutTask = client.LogoutWithAsync(cookie);
            await Task.WhenAll(refreshTask, logoutTask);

            var refresh = await refreshTask;
            if (refresh.StatusCode == HttpStatusCode.OK)
            {
                (await client.RefreshWithAsync(refresh.RefreshCookiePair()))
                    .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "attempt {0} logged out", attempt);
            }
        }
    }
}
