namespace AgroTrade.Contracts.Auth;

public record RegisterRequest(string FirstName, string LastName, string Phone, string Email, string Password);
public record LoginRequest(string Email, string Password);
public record AuthUserDto(int Id, string FirstName, string LastName, string Email, string Phone, string Role, DateTimeOffset CreatedAt);
public record AuthResponse(AuthUserDto User, string Token);
