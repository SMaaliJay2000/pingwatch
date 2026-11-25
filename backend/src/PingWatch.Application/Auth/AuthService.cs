using FluentValidation;
using Microsoft.Extensions.Logging;
using PingWatch.Application.Email;

namespace PingWatch.Application.Auth;

public sealed class AuthService(
    IUserAccounts users,
    IRefreshTokenStore refreshTokens,
    IAccessTokenIssuer accessTokens,
    IEmailSender emailSender,
    AuthLinkOptions links,
    ILogger<AuthService> logger)
{
    // Callers get the same outcome whether or not the email is already registered.
    public async Task RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        await RegisterValidator.Instance.ValidateAndThrowAsync(request, cancellationToken);

        var accountName = string.IsNullOrWhiteSpace(request.AccountName)
            ? request.Email.Split('@')[0]
            : request.AccountName.Trim();

        var user = await users.CreateOwnerAsync(request.Email, request.Password, accountName, cancellationToken);

        if (user is null)
        {
            await TrySendAsync(
                new EmailMessage(
                    request.Email,
                    "You already have a PingWatch account",
                    "Someone tried to sign up with this email. If it was you, log in, "
                    + $"or reset your password at {Link("forgot-password")}"),
                cancellationToken);
            return;
        }

        await SendVerificationAsync(user, cancellationToken);
    }

    public Task<bool> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken) =>
        users.VerifyEmailAsync(request.UserId, request.Token, cancellationToken);

    public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        await LoginValidator.Instance.ValidateAndThrowAsync(request, cancellationToken);

        var user = await users.FindByEmailAsync(request.Email, cancellationToken);
        if (user is null || !await users.CheckPasswordAsync(user.Id, request.Password, cancellationToken))
        {
            return new LoginResult(null, LoginFailure.InvalidCredentials);
        }

        if (!user.EmailVerified)
        {
            // Only reached with the correct password, so it also covers a lost verification email.
            await SendVerificationAsync(user, cancellationToken);
            return new LoginResult(null, LoginFailure.EmailNotVerified);
        }

        var refreshToken = await refreshTokens.IssueAsync(user, cancellationToken);
        return new LoginResult(new AuthSession(accessTokens.Issue(user), refreshToken), null);
    }

    public async Task<AuthSession?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var rotated = await refreshTokens.RotateAsync(refreshToken, cancellationToken);
        if (rotated is null)
        {
            return null;
        }

        var user = await users.FindByIdAsync(rotated.UserId, cancellationToken);
        return user is null ? null : new AuthSession(accessTokens.Issue(user), rotated.Next);
    }

    public Task LogoutAsync(string refreshToken, CancellationToken cancellationToken) =>
        refreshTokens.RevokeSessionAsync(refreshToken, cancellationToken);

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await ForgotPasswordValidator.Instance.ValidateAndThrowAsync(request, cancellationToken);

        var user = await users.FindByEmailAsync(request.Email, cancellationToken);
        if (user is null)
        {
            return;
        }

        var token = await users.CreatePasswordResetTokenAsync(user.Id, cancellationToken);
        var link = Link(
            $"reset-password?email={Uri.EscapeDataString(user.Email)}&token={Uri.EscapeDataString(token)}");

        await TrySendAsync(
            new EmailMessage(user.Email, "Reset your PingWatch password", $"Reset your password: {link}"),
            cancellationToken);
    }

    public async Task<bool> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await ResetPasswordValidator.Instance.ValidateAndThrowAsync(request, cancellationToken);

        var user = await users.FindByEmailAsync(request.Email, cancellationToken);
        return user is not null
            && await users.ResetPasswordAsync(user.Id, request.Token, request.NewPassword, cancellationToken);
    }

    private async Task SendVerificationAsync(AuthUser user, CancellationToken cancellationToken)
    {
        var token = await users.CreateEmailVerificationTokenAsync(user.Id, cancellationToken);
        var link = Link($"verify-email?userId={user.Id}&token={Uri.EscapeDataString(token)}");

        await TrySendAsync(
            new EmailMessage(user.Email, "Verify your PingWatch email", $"Verify your email: {link}"),
            cancellationToken);
    }

    private string Link(string pathAndQuery) => $"{links.AppBaseUrl.TrimEnd('/')}/{pathAndQuery}";

    // A mail outage must not turn into a 500 that reveals which addresses are registered.
    // ponytail: sent inline, so response time still differs; queue emails if that matters.
    private async Task TrySendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendAsync(message, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to send email: {Subject}", message.Subject);
        }
    }
}
