using AgroTrade.Domain.Enums;

namespace AgroTrade.Domain.Models;

public class AppUser
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? Street { get; set; }
    public string? Building { get; set; }
    public string? AddressNote { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.User;
    public bool IsBlocked { get; set; }
    public bool IsEmailVerified { get; set; }
    public string? EmailVerificationToken { get; set; }
    public DateTimeOffset? EmailVerificationTokenExpiresAt { get; set; }
    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
