using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PingWatch.Application.Auth;
using PingWatch.Domain;
using PingWatch.Infrastructure.Persistence;

namespace PingWatch.Infrastructure.Auth;

// UserManager takes no CancellationToken, so the tokens stop at this class.
internal sealed class UserAccounts(UserManager<ApplicationUser> userManager, PingWatchDbContext db) : IUserAccounts
{
    public async Task<AuthUser?> CreateOwnerAsync(
        string email, string password, string accountName, CancellationToken cancellationToken)
    {
        var account = new Account { Name = accountName };
        var user = new ApplicationUser { UserName = email, Email = email, AccountId = account.Id };

        // UserManager's SaveChanges stores both rows together, or neither.
        db.Accounts.Add(account);

        IdentityResult result;
        try
        {
            result = await userManager.CreateAsync(user, password);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two registrations for the same email raced each other.
            db.ChangeTracker.Clear();
            return null;
        }

        if (result.Succeeded)
        {
            return ToAuthUser(user);
        }

        // Drop the unsaved account so no later SaveChanges can store it without an owner.
        db.ChangeTracker.Clear();

        if (result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail)
            or nameof(IdentityErrorDescriber.DuplicateUserName)))
        {
            return null;
        }

        throw new InvalidOperationException(
            $"Could not create user: {string.Join(", ", result.Errors.Select(e => e.Code))}");
    }

    public async Task<AuthUser?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        return user is null ? null : ToAuthUser(user);
    }

    public async Task<AuthUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is null ? null : ToAuthUser(user);
    }

    public async Task<bool> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is not null && await userManager.CheckPasswordAsync(user, password);
    }

    public async Task<string> CreateEmailVerificationTokenAsync(Guid userId, CancellationToken cancellationToken) =>
        await userManager.GenerateEmailConfirmationTokenAsync(await GetRequiredAsync(userId));

    public async Task<bool> VerifyEmailAsync(Guid userId, string token, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is not null && (await userManager.ConfirmEmailAsync(user, token)).Succeeded;
    }

    public async Task<string> CreatePasswordResetTokenAsync(Guid userId, CancellationToken cancellationToken) =>
        await userManager.GeneratePasswordResetTokenAsync(await GetRequiredAsync(userId));

    public async Task<bool> ResetPasswordAsync(
        Guid userId, string token, string newPassword, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || !(await userManager.ResetPasswordAsync(user, token, newPassword)).Succeeded)
        {
            return false;
        }

        // Receiving the reset link proves the user owns the address.
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await userManager.UpdateAsync(user);
        }

        return true;
    }

    private async Task<ApplicationUser> GetRequiredAsync(Guid userId) =>
        await userManager.FindByIdAsync(userId.ToString())
        ?? throw new InvalidOperationException($"User {userId} does not exist.");

    private static AuthUser ToAuthUser(ApplicationUser user) =>
        new(user.Id, user.AccountId, user.Email!, user.EmailConfirmed, user.SecurityStamp!);
}
