using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PingWatch.Api.Auth;
using PingWatch.Application.Auth;

namespace PingWatch.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController(AuthService auth) : ControllerBase
{
    private const string RefreshCookie = "pw_refresh";

    [HttpPost("register")]
    [EnableRateLimiting(RateLimitPolicies.AuthEmail)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        await auth.RegisterAsync(request, cancellationToken);
        return Accepted(new MessageResponse("Check your email to continue."));
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest request, CancellationToken cancellationToken) =>
        await auth.VerifyEmailAsync(request, cancellationToken) ? NoContent() : InvalidLink();

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<ActionResult<AccessTokenResponse>> Login(
        LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await auth.LoginAsync(request, cancellationToken);

        if (result.Session is { } session)
        {
            return SignedIn(session);
        }

        return result.Failure == LoginFailure.EmailNotVerified
            ? Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Email not verified",
                detail: "We sent you a new verification link.")
            : Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password");
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AccessTokenResponse>> Refresh(CancellationToken cancellationToken)
    {
        var session = Request.Cookies[RefreshCookie] is { Length: > 0 } token
            ? await auth.RefreshAsync(token, cancellationToken)
            : null;

        if (session is not null)
        {
            return SignedIn(session);
        }

        Response.Cookies.Delete(RefreshCookie, CookieSettings());
        return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Session expired");
    }

    // Anonymous on purpose: signing out must work after the access token has expired.
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (Request.Cookies[RefreshCookie] is { Length: > 0 } token)
        {
            await auth.LogoutAsync(token, cancellationToken);
        }

        Response.Cookies.Delete(RefreshCookie, CookieSettings());
        return NoContent();
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimitPolicies.AuthEmail)]
    public async Task<IActionResult> ForgotPassword(
        ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await auth.ForgotPasswordAsync(request, cancellationToken);
        return Accepted(new MessageResponse("If that address has an account, we sent a reset link."));
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordRequest request, CancellationToken cancellationToken) =>
        await auth.ResetPasswordAsync(request, cancellationToken) ? NoContent() : InvalidLink();

    private ObjectResult InvalidLink() =>
        Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid or expired link");

    private ActionResult<AccessTokenResponse> SignedIn(AuthSession session)
    {
        var settings = CookieSettings();
        settings.Expires = session.RefreshToken.ExpiresAt;
        Response.Cookies.Append(RefreshCookie, session.RefreshToken.Token, settings);

        return session.AccessToken;
    }

    // SameSite=Strict is the CSRF defence; the web app and API must share a site (e.g. app. and api.).
    private static CookieOptions CookieSettings() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/api/v1/auth",
    };
}
