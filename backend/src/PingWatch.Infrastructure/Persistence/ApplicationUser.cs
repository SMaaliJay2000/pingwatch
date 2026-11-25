using Microsoft.AspNetCore.Identity;
using PingWatch.Domain;

namespace PingWatch.Infrastructure.Persistence;

public class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser() => Id = Guid.CreateVersion7();

    public Guid AccountId { get; set; }
    public UserRole Role { get; set; } = UserRole.Owner;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
