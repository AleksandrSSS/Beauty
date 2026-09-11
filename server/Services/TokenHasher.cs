using System.Security.Cryptography;
using System.Text;

namespace Beauty.Server.Services;

/// <summary>
/// Хешування OTP-кодів і refresh-токенів (S1-2, S1-6b). У БД зберігаємо лише хеш.
/// Для OTP додається salt із конфігу (Security:CodeSalt); refresh-токени вже
/// криптостійкі (128 біт з RNG), тож хешуються без солі.
/// </summary>
public class TokenHasher(IConfiguration cfg)
{
    private readonly string _codeSalt = cfg["Security:CodeSalt"] ?? "beauty-salon-default-otp-salt";

    /// <summary>SHA256(code + salt) у нижньому hex.</summary>
    public string HashCode(string code) => Sha256Hex(code + _codeSalt);

    /// <summary>SHA256(token) у нижньому hex.</summary>
    public static string HashToken(string token) => Sha256Hex(token);

    /// <summary>Криптостійкий refresh-токен (256 біт, url-safe base64).</summary>
    public static string NewRefreshToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string Sha256Hex(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
}
