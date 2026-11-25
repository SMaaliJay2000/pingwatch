using System.Net.Mail;
using FluentValidation;

namespace PingWatch.Application.Auth;

public static class PasswordRules
{
    public const int MinLength = 10;

    // Caps the work an attacker can force on the password hasher.
    public const int MaxLength = 128;
}

internal static class AuthRules
{
    public static void ValidEmail<T>(this IRuleBuilderInitial<T, string> rule) =>
        rule.Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(254)
            .Must(value => MailAddress.TryCreate(value, out var parsed) && parsed.Address == value)
            .WithMessage("Enter a valid email address.");

    public static void NewPassword<T>(this IRuleBuilderInitial<T, string> rule) =>
        rule.Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MinimumLength(PasswordRules.MinLength)
            .MaximumLength(PasswordRules.MaxLength);
}

internal sealed class RegisterValidator : AbstractValidator<RegisterRequest>
{
    public static readonly RegisterValidator Instance = new();

    private RegisterValidator()
    {
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Password).NewPassword();
        RuleFor(x => x.AccountName).MaximumLength(200);
    }
}

internal sealed class LoginValidator : AbstractValidator<LoginRequest>
{
    public static readonly LoginValidator Instance = new();

    private LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(PasswordRules.MaxLength);
    }
}

internal sealed class ForgotPasswordValidator : AbstractValidator<ForgotPasswordRequest>
{
    public static readonly ForgotPasswordValidator Instance = new();

    private ForgotPasswordValidator() => RuleFor(x => x.Email).ValidEmail();
}

internal sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordRequest>
{
    public static readonly ResetPasswordValidator Instance = new();

    private ResetPasswordValidator()
    {
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword).NewPassword();
    }
}
