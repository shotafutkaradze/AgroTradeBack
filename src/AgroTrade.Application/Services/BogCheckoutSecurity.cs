using System.Security.Cryptography;
using System.Text;

namespace AgroTrade.Application.Services;

public static class BogCheckoutSecurity
{
    public static string CreateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public static bool Verify(string? token, string? expectedHash) =>
        token?.Length == 64 && expectedHash?.Length == 64 &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Hash(token)), Encoding.UTF8.GetBytes(expectedHash));
}
