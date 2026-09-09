using Beauty.Server.Data;
using Beauty.Server.Models;
using Beauty.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Beauty.Server.Controllers;

public record RequestCodeDto(string Phone, NotifyChannel Channel, string? Contact);
public record VerifyCodeDto(string Phone, string Code, string? Name, string? Contact);
public record AdminLoginDto(string Phone, string Password);

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, JwtService jwt, INotificationService notifier, IConfiguration cfg) : ControllerBase
{
    public const string CodeChars = "123456789";

    [HttpPost("request-code")]
    [AllowAnonymous]
    public async Task<IActionResult> RequestCode(RequestCodeDto dto)
    {
        var phone = dto.Phone.Trim();
        if (phone.Length < 9) return BadRequest(new { error = "Некоректний номер телефону" });

        var code = new string(Enumerable.Range(0, 4).Select(_ => CodeChars[Random.Shared.Next(CodeChars.Length)]).ToArray());
        db.VerificationCodes.Add(new VerificationCode
        {
            Phone = phone,
            Code = code,
            Purpose = "login",
            Channel = dto.Channel,
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
        return Ok(new { mock = result.IsMock, info = result.Info, devCode = (isDev && result.IsMock) ? code : null });
    }

    [HttpPost("verify-code")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyCode(VerifyCodeDto dto)
    {
        var vc = await db.VerificationCodes
            .Where(v => v.Phone == dto.Phone.Trim() && v.Code == dto.Code.Trim()
                     && v.Purpose == "login" && v.ConsumedAt == null && v.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(v => v.Id).FirstOrDefaultAsync();
        if (vc == null) return BadRequest(new { error = "Невірний або прострочений код" });

        vc.ConsumedAt = DateTime.UtcNow;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == dto.Phone.Trim());
        if (user == null)
        {
            user = new User
            {
                Phone = dto.Phone.Trim(),
                Name = string.IsNullOrWhiteSpace(dto.Name) ? "Клієнт" : dto.Name.Trim(),
                PreferredChannel = vc.Channel.ToString(),
                ExternalContact = dto.Contact
            };
            db.Users.Add(user);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(dto.Name)) user.Name = dto.Name.Trim();
            if (!string.IsNullOrWhiteSpace(dto.Contact)) user.ExternalContact = dto.Contact;
            user.PreferredChannel = vc.Channel.ToString();
        }
        await db.SaveChangesAsync();
        return Ok(new { token = jwt.CreateToken(user), user = new { user.Id, user.Phone, user.Name, user.Email, role = user.Role.ToString(), user.PreferredChannel } });
    }

    [HttpPost("admin-login")]
    [AllowAnonymous]
    public async Task<IActionResult> AdminLogin(AdminLoginDto dto)
    {
        var adminPhone = cfg["Admin:Phone"] ?? "+380000000000";
        var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == dto.Phone.Trim() && u.Role == UserRole.Admin);
        if (user == null || user.PasswordHash == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return Unauthorized(new { error = "Невірний логін або пароль" });
        return Ok(new { token = jwt.CreateToken(user), user = new { user.Id, user.Phone, user.Name, role = user.Role.ToString() } });
    }
}
