using Beauty.Server.Data;
using Beauty.Server.Models;
using Beauty.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace Beauty.Server.Controllers;

public record SlotsQuery(int? MasterId, int ServiceId);
// Нові параметри — лише в кінці (§5 AGENTS.md). Channel/Phone/Name опційні, щоб залогінений
// клієнт міг слати лише (MasterId, ServiceId, StartTime) — решту сервер бере з профілю User.
public record CreateBookingDto(int MasterId, int ServiceId, DateTime StartTime, NotifyChannel? Channel = null, string? Contact = null, string? Phone = null, string? Name = null);
public record ConfirmBookingDto(int AppointmentId, string Code, string? Phone = null);

[ApiController]
[Route("api/booking")]
public class BookingController(AppDbContext db, INotificationService notifier, SalonClock clock, ScheduleService schedule, JwtService jwt) : ControllerBase
{
    /// <summary>
    /// Вільні слоти на 28 днів. Якщо MasterId задано — лише обраний майстер;
    /// якщо ні — об'єднані слоти всіх активних майстрів послуги, кожен слот
    /// містить список майстрів, вільних саме в цей час.
    /// </summary>
    [HttpPost("slots")]
    public async Task<IActionResult> Slots(SlotsQuery q, CancellationToken ct)
    {
        List<SlotDto> slots;
        try
        {
            slots = await schedule.GetSlotsAsync(q.MasterId, q.ServiceId, ct);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        // Форму відповіді зберігаємо: з masterId — [{start, end}], без — [{start, end, masters}]
        var list = slots
            .OrderBy(s => s.Start)
            .Select(s => q.MasterId.HasValue
                ? (object)new { start = s.Start, end = s.End }
                : new { start = s.Start, end = s.End, masters = s.Masters!.Select(m => new { id = m.Id, name = m.Name }) })
            .ToList();
        return Ok(list);
    }


    /// <summary>
    /// Гостьове бронювання: дві гілки — залогінений (одразу Confirmed, без коду)
    /// і гість (PendingVerification + код підтвердження запису).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("create")]
    public async Task<IActionResult> Create(CreateBookingDto dto, CancellationToken ct)
    {
        var isAuthenticated = User.Identity?.IsAuthenticated == true;

        // Гілка залогіненого: телефон і канал — з профілю User, поля DTO ігноруються.
        User? profile = null;
        NotifyChannel channel;
        string? contact;
        string phone;
        if (isAuthenticated)
        {
            profile = await db.Users.FindAsync([JwtService.GetUserId(User)], ct);
            if (profile == null) return Unauthorized(new { error = "Користувача не знайдено" });
            phone = profile.Phone;
            contact = profile.ExternalContact;
            if (!Enum.TryParse<NotifyChannel>(profile.PreferredChannel, out channel))
                channel = NotifyChannel.Sms;
        }
        else
        {
            // Гілка гостя: телефон обов'язковий у тілі (JWT немає).
            phone = dto.Phone?.Trim() ?? "";
            if (phone.Length < 9) return BadRequest(new { error = "Некоректний номер телефону" });
            if (dto.Channel == null) return BadRequest(new { error = "Оберіть канал надсилання коду" });
            channel = dto.Channel.Value;
            contact = dto.Contact;

            // Легкий per-phone ліміт (§6.6 плану): макс. 3 коди booking за 15 хв.
            // Час створення виводимо з ExpiresAt (код живе 10 хв): створено за останні
            // 15 хв ⇔ ExpiresAt > now − 5 хв. Без нового поля й міграції.
            var windowFrom = DateTime.UtcNow.AddMinutes(-5);
            var freshCodes = await db.VerificationCodes.CountAsync(
                v => v.Phone == phone && v.Purpose == "booking" && v.ExpiresAt > windowFrom, ct);
            if (freshCodes >= 3)
                return StatusCode(429, new { error = "Забагато запитів коду. Спробуйте за кілька хвилин." });
        }

        // Серіалізована транзакція: усуває race condition (подвійне бронювання одного слота)
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        // Find-or-create клієнта за телефоном (як у AuthController.VerifyCode).
        User user;
        if (profile != null)
        {
            user = profile;
        }
        else
        {
            user = (await db.Users.FirstOrDefaultAsync(u => u.Phone == phone, ct))!;
            if (user == null)
            {
                user = new User
                {
                    Phone = phone,
                    Name = string.IsNullOrWhiteSpace(dto.Name) ? "Клієнт" : dto.Name.Trim(),
                    PreferredChannel = channel.ToString(),
                    ExternalContact = contact
                };
                db.Users.Add(user);
                await db.SaveChangesAsync(ct);
            }
            else
            {
                // Синхронізуємо профіль з місцем відправки коду (як VerifyCode),
                // щоб confirm-повідомлення і майбутні записи йшли за актуальними даними.
                if (!string.IsNullOrWhiteSpace(contact)) user.ExternalContact = contact;
                user.PreferredChannel = channel.ToString();
                await db.SaveChangesAsync(ct);
            }
        }

        var start = DateTime.SpecifyKind(dto.StartTime, DateTimeKind.Utc);
        var check = await schedule.ValidateAsync(dto.MasterId, dto.ServiceId, start, null, ct);
        if (!check.Ok)
        {
            if (check.StatusCode == 404) return NotFound(new { error = check.Error });
            if (check.StatusCode == 409) return Conflict(new { error = check.Error });
            return BadRequest(new { error = check.Error });
        }

        // ValidateAsync уже підтвердив існування й активність; тривалість для кінця запису
        var service = await db.Services.FindAsync([dto.ServiceId], ct);
        var end = start.AddMinutes(service!.DurationMin);

        var appt = new Appointment
        {
            ClientId = user.Id, MasterId = dto.MasterId, ServiceId = dto.ServiceId,
            StartTime = start, EndTime = end, NotifyChannel = channel,
            Status = isAuthenticated ? AppointmentStatus.Confirmed : AppointmentStatus.PendingVerification
        };
        db.Appointments.Add(appt);
        await db.SaveChangesAsync(ct);

        string? code = null;
        if (!isAuthenticated)
        {
            code = new string(Enumerable.Range(0, 4).Select(_ => AuthController.CodeChars[Random.Shared.Next(AuthController.CodeChars.Length)]).ToArray());
            db.VerificationCodes.Add(new VerificationCode
            {
                Phone = phone,
                Code = code, Purpose = "booking", AppointmentId = appt.Id,
                Channel = channel, ExpiresAt = DateTime.UtcNow.AddMinutes(10)
            });
            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);

        var isDev = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development" || HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Environment"] == "Development";
        if (isAuthenticated)
        {
            var master = await db.Masters.FindAsync([dto.MasterId], ct);
            var msg = $"Запис підтверджено: {service.Name} у майстра {master?.Name}, {clock.Format(start)}. Ми нагадаємо вам за 3 години.";
            var result = await notifier.SendAsync(channel, NotificationService.ResolveRecipient(user, channel), msg);
            return Ok(new { appointmentId = appt.Id, confirmed = true, mock = result.IsMock, info = result.Info, devCode = (string?)null });
        }

        var codeMsg = $"Ваш код підтвердження запису ({service.Name}, {clock.Format(start)}): {code}";
        var codeResult = await notifier.SendAsync(channel, contact ?? phone, codeMsg);
        return Ok(new { appointmentId = appt.Id, confirmed = false, mock = codeResult.IsMock, info = codeResult.Info, devCode = (isDev && codeResult.IsMock) ? code : null });
    }

    /// <summary>
    /// Підтвердження гостьового запису кодом. Одночасно є входом:
    /// після успіху видає JWT для клієнта (автологін у кабінет).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(ConfirmBookingDto dto)
    {
        // Гість не має JWT — шукаємо клієнта за телефоном із тіла.
        User? user;
        if (User.Identity?.IsAuthenticated == true)
        {
            user = await db.Users.FindAsync([JwtService.GetUserId(User)]);
            if (user == null) return Unauthorized(new { error = "Користувача не знайдено" });
        }
        else
        {
            var phone = dto.Phone?.Trim() ?? "";
            if (phone.Length < 9) return BadRequest(new { error = "Некоректний номер телефону" });
            user = await db.Users.FirstOrDefaultAsync(u => u.Phone == phone);
            if (user == null) return NotFound(new { error = "Запис не знайдено" });
        }

        var appt = await db.Appointments.Include(a => a.Service).Include(a => a.Master)
            .FirstOrDefaultAsync(a => a.Id == dto.AppointmentId && a.ClientId == user.Id);
        if (appt == null) return NotFound(new { error = "Запис не знайдено" });
        if (appt.Status != AppointmentStatus.PendingVerification) return BadRequest(new { error = "Запис уже оброблено" });

        var vc = await db.VerificationCodes
            .Where(v => v.AppointmentId == appt.Id && v.Phone == user.Phone && v.Code == dto.Code.Trim() && v.ConsumedAt == null && v.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(v => v.Id).FirstOrDefaultAsync();
        if (vc == null) return BadRequest(new { error = "Невірний або прострочений код" });

        vc.ConsumedAt = DateTime.UtcNow;
        appt.Status = AppointmentStatus.Confirmed;
        await db.SaveChangesAsync();

        var msg = $"Запис підтверджено: {appt.Service.Name} у майстра {appt.Master.Name}, {clock.Format(appt.StartTime)}. Ми нагадаємо вам за 3 години.";
        await notifier.SendAsync(appt.NotifyChannel, NotificationService.ResolveRecipient(user, appt.NotifyChannel), msg);
        return Ok(new { ok = true, token = jwt.CreateToken(user), user = new { user.Id, user.Phone, user.Name, user.Email, role = user.Role.ToString(), user.PreferredChannel } });
    }
}

