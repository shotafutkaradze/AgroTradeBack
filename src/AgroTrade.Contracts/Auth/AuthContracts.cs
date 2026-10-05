namespace AgroTrade.Contracts.Auth;

public record RegisterRequest(string FirstName, string LastName, string Phone, string Email, string Password);
public record AdminCreateUserRequest(string FirstName, string LastName, string Phone, string Email, string Password, string Role);
public record UpdateProfileRequest(
    string FirstName,
    string LastName,
    string Phone,
    string Email,
    string? City,
    string? Region,
    string? Street,
    string? Building,
    string? AddressNote);
public record UpdateUserRoleRequest(string Role);
public record UpdateUserBlockRequest(bool IsBlocked);
public record LoginRequest(string Email, string Password);
public record VerifyEmailRequest(string Token);
public record ResendEmailVerificationRequest(string Email);
public record AuthUserDto(
    int Id,
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    string Role,
    bool IsBlocked,
    bool IsEmailVerified,
    string? City,
    string? Region,
    string? Street,
    string? Building,
    string? AddressNote,
    DateTimeOffset CreatedAt);
public record AuthResponse(AuthUserDto User, string Token);
