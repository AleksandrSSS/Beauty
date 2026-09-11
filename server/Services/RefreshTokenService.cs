using Beauty.Server.Data;
using Beauty.Server.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Beauty.Server.Services;

/// <summary>
/// Керування refresh-токенами (S1-6b): видача в httpOnly-cookie, ротація при оновленні,
/// відкликання при logout. Сирий токен існує лише в cookie; у БД — його SHA256-хеш.
/// </summary>
public class RefreshTokenService(AppDbContext db, IConfiguration cfg)
{
    public const string CookieName = "beauty_rt";

    private int Days => cfg.GetValue("Jwt:RefreshDays", 30);

    /// <summary>Створює refresh-токен для користувача та кладе його у httpOnly-cookie.</summary>
    public async Task IssueAsync(HttpResponse response, User user, CancellationToken ct = default)
    {
        var raw = TokenHasher.NewRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = TokenHasher.HashToken(raw),
            ExpiresAt = DateTime.UtcNow.AddDays(Days)
        });
        await db.SaveChangesAsync(ct);
        SetCookie(response, raw);
    }

    /// <summary>
    /// Перевіряє refresh-cookie, і при валідності — відкликає старий токен, видає новий
    /// (ротація) і повертає користувача. null — cookie немає/недійсний.
    /// </summary>
    public async Task<User?> RotateAsync(HttpRequest request, HttpResponse response, CancellationToken ct = default)
    {
        var raw = request.Cookies[CookieName];
        if (string.IsNullOrEmpty(raw)) return null;

        var hash = TokenHasher.HashToken(raw);
        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored == null || stored.RevokedAt != null || stored.ExpiresAt <= DateTime.UtcNow)
            return null;

        stored.RevokedAt = DateTime.UtcNow;
        var raw2 = TokenHasher.NewRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = stored.UserId,
            TokenHash = TokenHasher.HashToken(raw2),
            ExpiresAt = DateTime.UtcNow.AddDays(Days)
        });
        await db.SaveChangesAsync(ct);
        SetCookie(response, raw2);
        return stored.User;
    }

    /// <summary>Відкликає токен із cookie (logout) і видаляє cookie.</summary>
    public async Task RevokeAsync(HttpRequest request, HttpResponse response, CancellationToken ct = default)
    {
        var raw = request.Cookies[CookieName];
        if (!string.IsNullOrEmpty(raw))
        {
            var hash = TokenHasher.HashToken(raw);
            var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
            if (stored != null && stored.RevokedAt == null)
            {
                stored.RevokedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }
        response.Cookies.Delete(CookieName);
    }

    private void SetCookie(HttpResponse response, string raw) =>
        response.Cookies.Append(CookieName, raw, new CookieOptions
        {
            HttpOnly = true,
            Secure = cfg.GetValue("Jwt:RefreshCookieSecure", false),
            SameSite = SameSiteMode.Lax,
            Path = "/api/auth",
            Expires = DateTimeOffset.UtcNow.AddDays(Days)
        });
}
