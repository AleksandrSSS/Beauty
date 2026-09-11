using Beauty.Server.Data;
using Beauty.Server.Models;
using Beauty.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Beauty.Server.Controllers;

public record RequestCodeDto(string Phone, NotifyChannel Channel, string? Contact);
// RequestId додано (S1-5): verify прив'язаний до конкретного request-code.
public record VerifyCodeDto(string Phone, string Code, string? Name, string? Contact, Guid RequestId);
public record AdminLoginDto(string Phone, string Password);

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, JwtService jwt, INotificationService notifier,
    TokenHasher hasher, RefreshTokenService refresh) : ControllerBase
{
    /// <summary>Максимум невдалих спроб verify по одному коду (S1-3). Понад — код спалюється.</summary>
    private const int MaxVerifyAttempts = 5;

    /// <summary>Генерує 6-значний код із повного діапазону 0–9 (S1-1): 10⁶ комбінацій.</summary>
    private static string GenerateCode() =>
        Random.Shared.Next(0, 1_000_000).ToString("D6");

    [HttpPost("request-code")]
    [AllowAnonymous]
    [EnableRateLimiting("request-code")]
    public async Task<IActionResult> RequestCode(RequestCodeDto dto)
    {
        var phone = dto.Phone.Trim();
        if (phone.Length < 9) return BadRequest(new { error = "Некоректний номер телефону" });

        var code = GenerateCode();
        var requestId = Guid.NewGuid();
        db.VerificationCodes.Add(new VerificationCode
        {
            Phone = phone,
            CodeHash = hasher.HashCode(code),
            Purpose = "login",
            Channel = dto.Channel,
            RequestId = requestId,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });
        await db.SaveChangesAsync();

        var recipient = dto.Channel switch
        {
            NotifyChannel.Email => dto.Contact ?? phone,
            NotifyChannel.Telegram => dto.Contact ?? phone,
            _ => phone
        };
        var result = await notifier.SendAsync(dto.Channel, recipient,
            $"Код підтвердження для Beauty Salon: {code}. Дійсний 10 хвилин.");

        var isDev = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development" || HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Environment"] == "Development";
        return Ok(new { requestId, mock = result.IsMock, info = result.Info, devCode = (isDev && result.IsMock) ? code : null });
    }

    [HttpPost("verify-code")]
    [AllowAnonymous]
    [EnableRateLimiting("verify-code")]
    public async Task<IActionResult> VerifyCode(VerifyCodeDto dto)
    {
        var phone = dto.Phone.Trim();
        // Прив'язка до конкретного запиту (S1-5): без валідного RequestId verify не проходить.
        var vc = await db.VerificationCodes
            .Where(v => v.Phone == phone && v.RequestId == dto.RequestId
                     && v.Purpose == "login" && v.ConsumedAt == null && v.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(v => v.Id).FirstOrDefaultAsync();
        if (vc == null) return BadRequest(new { error = "Невірний або прострочений код" });

        // Ліміт спроб (S1-3): спалений код відхиляється навіть при правильному значенні.
        if (vc.AttemptCount >= MaxVerifyAttempts)
        {
            vc.ConsumedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return BadRequest(new { error = "Перевищено кількість спроб. Запросіть новий код." });
        }

        // Порівняння за хешем (S1-2): відкритого коду в БД немає.
        if (vc.CodeHash != hasher.HashCode(dto.Code.Trim()))
        {
            vc.AttemptCount++;
            await db.SaveChangesAsync();
            return BadRequest(new { error = "Невірний або прострочений код" });
        }

        vc.ConsumedAt = DateTime.UtcNow;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == phone);
        if (user == null)
        {
            user = new User
            {
                Phone = phone,
                Name = string.IsNullOrWhiteSpace(dto.Name) ? "Клієнт" : dto.Name.Trim(),
                PreferredChannel = vc.Channel.ToString(),
                ExternalContact = dto.Contact
            };
            db.Users.Add(user);
        }
        else
        {
            // Профіль оновлюється лише даними цього ж запиту (RequestId вже звірено) — чужий профіль не переписати.
            if (!string.IsNullOrWhiteSpace(dto.Name)) user.Name = dto.Name.Trim();
            if (!string.IsNullOrWhiteSpace(dto.Contact)) user.ExternalContact = dto.Contact;
            user.PreferredChannel = vc.Channel.ToString();
        }
        await db.SaveChangesAsync();
        await refresh.IssueAsync(Response, user);
        return Ok(new { token = jwt.CreateToken(user), user = new { user.Id, user.Phone, user.Name, user.Email, role = user.Role.ToString(), user.PreferredChannel } });
    }

    [HttpPost("admin-login")]
    [AllowAnonymous]
    [EnableRateLimiting("admin-login")]
    public async Task<IActionResult> AdminLogin(AdminLoginDto dto)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == dto.Phone.Trim() && u.Role == UserRole.Admin);
        if (user == null || user.PasswordHash == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return Unauthorized(new { error = "Невірний логін або пароль" });
        await refresh.IssueAsync(Response, user);
        return Ok(new { token = jwt.CreateToken(user), user = new { user.Id, user.Phone, user.Name, role = user.Role.ToString() } });
    }

    /// <summary>Оновлення access-токена за refresh-cookie (S1-6b). Ротує refresh-токен.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var user = await refresh.RotateAsync(Request, Response, ct);
        if (user == null) return Unauthorized(new { error = "Сесія недійсна. Увійдіть знову." });
        return Ok(new { token = jwt.CreateToken(user), user = new { user.Id, user.Phone, user.Name, user.Email, role = user.Role.ToString(), user.PreferredChannel } });
    }

    /// <summary>Вихід: відкликає refresh-токен і видаляє cookie.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await refresh.RevokeAsync(Request, Response, ct);
        return Ok(new { ok = true });
    }
}
