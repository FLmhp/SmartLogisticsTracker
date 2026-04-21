using SmartLogisticsTracker.Api.Domain.Enums;

namespace SmartLogisticsTracker.Api.Domain.Entities;

public sealed class AppUser
{
    public Guid Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
