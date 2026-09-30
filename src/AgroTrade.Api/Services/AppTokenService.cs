using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgroTrade.Api.Models;

namespace AgroTrade.Api.Services;

public class AppTokenService(IConfiguration configuration)
{
    private readonly string secret = configuration["Auth:TokenSecret"] ?? "agrotrade-local-development-token-secret";

    public string CreateToken(AppUser user)
    {
        var payload = new AppTokenPayload(
            user.Id,
            user.Email,
            user.Role,
            DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds());

        var payloadJson = JsonSerializer.Serialize(payload);
        var payloadPart = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signaturePart = Sign(payloadPart);

        return $"{payloadPart}.{signaturePart}";
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        var parts = token.Split('.', 2);
        if (parts.Length != 2 || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(Sign(parts[0])),
                Encoding.UTF8.GetBytes(parts[1])))
        {
            return null;
        }

        AppTokenPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<AppTokenPayload>(Base64UrlDecode(parts[0]));
        }
        catch
        {
            return null;
        }

        if (payload is null || DateTimeOffset.UtcNow.ToUnixTimeSeconds() > payload.ExpiresAt)
        {
            return null;
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, payload.UserId.ToString()),
            new Claim(ClaimTypes.Email, payload.Email),
            new Claim(ClaimTypes.Role, payload.Role),
            new Claim(ClaimTypes.Name, payload.Email)
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    private string Sign(string value)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }

    private record AppTokenPayload(int UserId, string Email, string Role, long ExpiresAt);
}
