using Beauty.Server.Data;
using Beauty.Server.Models;
using Beauty.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Claims;

namespace Beauty.Server.Controllers;

public record ServiceDto(string Name, string? Description, string Category, int DurationMin, decimal Price, bool IsActive);
public record MasterDto(string Name, string? Specialty, string? Bio, bool IsActive, string RotationType, DateOnly RotationAnchor, List<int> ServiceIds, string? PhotoUrl);
public record SalonHoursDto(TimeOnly OpenTime, TimeOnly CloseTime);
public record DayOverrideDto(DateOnly Date, bool IsWorking, TimeOnly? Start, TimeOnly? End);
public record RescheduleDto(DateTime StartTime);
public record AppointmentStatusDto(string Status);
public record SetAppointmentMasterDto(int MasterId);
public record DeactivationRequest(string Policy, List<ReassignmentDto>? Reassignments);
public record ReassignmentDto(int AppointmentId, int MasterId);

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController(AppDbContext db, IWebHostEnvironment env, IConfiguration cfg,
    ScheduleService schedule, INotificationService notifier, SalonClock clock, ILogger<AdminController> logger) : ControllerBase
{
    // ---- Послуги ----
    [HttpGet("services")]
    public async Task<IActionResult> Services() => Ok(await db.Services.OrderBy(s => s.Name).ToListAsync());

    [HttpPost("services")]
    public async Task<IActionResult> AddService(ServiceDto dto)
    {
        var s = new Service { Name = dto.Name, Description = dto.Description, Category = dto.Category, DurationMin = dto.DurationMin, Price = dto.Price, IsActive = dto.IsActive };
        db.Services.Add(s);
        await db.SaveChangesAsync();
        return Ok(s);
    }

    [HttpPut("services/{id}")]
    public async Task<IActionResult> UpdateService(int id, ServiceDto dto)
    {
        var s = await db.Services.FindAsync(id);
        if (s == null) return NotFound();
        s.Name = dto.Name; s.Description = dto.Description; s.Category = dto.Category;
        s.DurationMin = dto.DurationMin; s.Price = dto.Price; s.IsActive = dto.IsActive;
        await db.SaveChangesAsync();
        return Ok(s);
    }

    [HttpDelete("services/{id}")]
    public async Task<IActionResult> DeleteService(int id)
    {
        var s = await db.Services.FindAsync(id);
        if (s == null) return NotFound();
        s.IsActive = false; // м'яке видалення, щоб не зламати історію записів
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ---- Майстри ----
    [HttpGet("masters")]
    public async Task<IActionResult> Masters()
    {
        var masters = await db.Masters.Include(m => m.MasterServices).ToListAsync();
        return Ok(masters.Select(m => new
        {
            m.Id, m.Name, m.Specialty, m.Bio, m.PhotoUrl, m.IsActive,
            rotationType = m.RotationType.ToString(),
            rotationAnchor = m.RotationAnchor,
            services = m.MasterServices.Select(ms => ms.ServiceId)
        }));
    }

    [HttpPost("masters")]
    public async Task<IActionResult> AddMaster(MasterDto dto)
    {
        var ve = ValidateMasterData(dto); if (ve != null) return ve;
        var m = new Master { Name = dto.Name, Specialty = dto.Specialty, Bio = dto.Bio, IsActive = dto.IsActive,
            RotationType = Enum.TryParse<RotationType>(dto.RotationType, true, out var rt) ? rt : RotationType.Weekly,
            // Якір зберігаємо як є: Weekly рахує 7/7 блоками від першого робочого дня (будь-який день тижня).
            RotationAnchor = dto.RotationAnchor, PhotoUrl = dto.PhotoUrl };
        db.Masters.Add(m);
        await db.SaveChangesAsync();
        foreach (var sid in dto.ServiceIds) db.MasterServices.Add(new MasterService { MasterId = m.Id, ServiceId = sid });
        await db.SaveChangesAsync();
        return Ok(new { m.Id });
    }

    [HttpPut("masters/{id}")]
    public async Task<IActionResult> UpdateMaster(int id, MasterDto dto)
    {
        var ve = ValidateMasterData(dto); if (ve != null) return ve;
        var m = await db.Masters.Include(x => x.MasterServices).FirstOrDefaultAsync(x => x.Id == id);
        if (m == null) return NotFound();
        m.Name = dto.Name; m.Specialty = dto.Specialty; m.Bio = dto.Bio; m.IsActive = dto.IsActive;
        // Фото обнуляється тільки через DELETE .../photo, порожнє поле форми його не затирає
        if (!string.IsNullOrEmpty(dto.PhotoUrl)) m.PhotoUrl = dto.PhotoUrl;
        if (Enum.TryParse<RotationType>(dto.RotationType, true, out var rt)) m.RotationType = rt;
        m.RotationAnchor = dto.RotationAnchor;
        db.MasterServices.RemoveRange(m.MasterServices);
        foreach (var sid in dto.ServiceIds) db.MasterServices.Add(new MasterService { MasterId = id, ServiceId = sid });
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpDelete("masters/{id}")]
    public async Task<IActionResult> DeleteMaster(int id)
    {
        var m = await db.Masters
            .Include(x => x.MasterServices)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (m == null) return NotFound();

        var hasAppointments = await db.Appointments.AnyAsync(a => a.MasterId == id);
        if (hasAppointments)
            return Conflict(new { error = "У майстра є записи. Використайте деактивацію з обробкою майбутніх записів." });

        db.MasterServices.RemoveRange(m.MasterServices);
        TryDeletePhotoFile(m.PhotoUrl);
        m.PhotoUrl = null;
        db.Masters.Remove(m);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>Завантаження фото майстра. Ім'я файлу генерує сервер; file.FileName не використовується (захист від path traversal).</summary>
    [HttpPost("masters/{id}/photo")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    public async Task<IActionResult> UploadPhoto(int id, IFormFile file)
    {
        var m = await db.Masters.FindAsync(id);
        if (m == null) return NotFound(new { error = "Майстра не знайдено" });
        if (file == null || file.Length == 0) return BadRequest(new { error = "Файл не вибрано" });

        var maxBytes = cfg.GetValue("Uploads:MaxFileSizeBytes", 3 * 1024 * 1024);
        if (file.Length > maxBytes) return BadRequest(new { error = "Розмір файлу перевищує 3 МБ" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp"))
            return BadRequest(new { error = "Дозволені лише файли JPG, PNG або WebP" });

        // ContentType від клієнта не довіряємо — перевіряємо magic bytes.
        // Файл може бути коротшим за 12 байт: читаємо скільки є і звіряємо за фактом.
        var header = new byte[12];
        int read;
        await using (var rs = file.OpenReadStream())
        {
            read = 0;
            int n;
            while (read < header.Length && (n = await rs.ReadAsync(header.AsMemory(read))) > 0)
                read += n;
        }
        if (!IsImage(header, read))
            return BadRequest(new { error = "Вміст файлу не є зображенням JPG, PNG або WebP" });

        var dir = Path.Combine(env.ContentRootPath, cfg["Uploads:MastersPath"] ?? "wwwroot/uploads/masters");
        Directory.CreateDirectory(dir);
        var name = $"{id}-{Guid.NewGuid():N}{ext}";
        await using (var fs = System.IO.File.Create(Path.Combine(dir, name)))
        {
            await using var rs = file.OpenReadStream();
            await rs.CopyToAsync(fs);
        }

        // Старий файл видаляємо після успішного запису; помилка видалення запит не валить
        TryDeletePhotoFile(m.PhotoUrl);
        m.PhotoUrl = $"/uploads/masters/{name}";
        await db.SaveChangesAsync();
        return Ok(new { photoUrl = m.PhotoUrl });
    }

    /// <summary>Видалення фото майстра. Ідемпотентний: якщо фото не було — теж 200.</summary>
    [HttpDelete("masters/{id}/photo")]
    public async Task<IActionResult> DeletePhoto(int id)
    {
        var m = await db.Masters.FindAsync(id);
        if (m == null) return NotFound(new { error = "Майстра не знайдено" });
        TryDeletePhotoFile(m.PhotoUrl);
        m.PhotoUrl = null;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>Перевірка сигнатури JPG (3 байти) / PNG (8) / WebP (12) за фактично прочитаними байтами.</summary>
    private static bool IsImage(byte[] h, int count) =>
        // JPEG: FF D8 FF
        (count >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF)
        // PNG: 89 50 4E 47 0D 0A 1A 0A
        || (count >= 8 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47
            && h[4] == 0x0D && h[5] == 0x0A && h[6] == 0x1A && h[7] == 0x0A)
        // WebP: RIFF....WEBP
        || (count >= 12 && h[0] == 0x52 && h[1] == 0x49 && h[2] == 0x46 && h[3] == 0x46
            && h[8] == 0x57 && h[9] == 0x45 && h[10] == 0x42 && h[11] == 0x50);

    /// <summary>Видалення файлу фото за відносним шляхом. Працює тільки всередині папки uploads.</summary>
    private void TryDeletePhotoFile(string? photoUrl)
    {
        if (string.IsNullOrEmpty(photoUrl)) return;
        try
        {
            var dir = Path.GetFullPath(Path.Combine(env.ContentRootPath, cfg["Uploads:MastersPath"] ?? "wwwroot/uploads/masters"));
            var full = Path.GetFullPath(Path.Combine(dir, Path.GetFileName(photoUrl)));
            if (full.StartsWith(dir, StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(full))
                System.IO.File.Delete(full);
        }
        catch { /* помилка видалення старого файлу не валить запит */ }
    }

    /// <summary>
    /// Поточна зайнятість/кандидати для форми звільнення.
    /// Повертає лише майбутні записи (Confirmed + PendingVerification), кожен із кандидатами —
    /// активними майстрами, що надають цю послугу і вільні в цей час (перевірка через ScheduleService.ValidateAsync).
    /// </summary>
    [HttpGet("masters/{id}/deactivation-impact")]
    public async Task<IActionResult> DeactivationImpact(int id, CancellationToken ct)
    {
        var m = await db.Masters.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m == null) return NotFound(new { error = "Майстра не знайдено" });

        var future = await db.Appointments
            .Include(a => a.Service)
            .Include(a => a.Client)
            .Where(a => a.MasterId == id
                && a.StartTime > DateTime.UtcNow
                && (a.Status == AppointmentStatus.Confirmed || a.Status == AppointmentStatus.PendingVerification))
            .OrderBy(a => a.StartTime)
            .ToListAsync(ct);

        async Task<List<Master>> Candidates(Appointment appt)
        {
            var candidates = await db.Masters
                .Include(x => x.MasterServices)
                .Where(x => x.IsActive && x.MasterServices.Any(ms => ms.ServiceId == appt.ServiceId))
                .ToListAsync(ct);
            var result = new List<Master>();
            foreach (var cand in candidates)
            {
                if (cand.Id == appt.MasterId) continue;
                var check = await schedule.ValidateAsync(cand.Id, appt.ServiceId, appt.StartTime, appt.Id, ct);
                if (check.Ok) result.Add(cand);
            }
            result.Sort((x, y) => x.Name.CompareTo(y.Name));
            return result;
        }

        var items = new List<object>();
        foreach (var a in future)
        {
            var candidates = await Candidates(a);
            items.Add(new
            {
                a.Id, a.StartTime, a.EndTime, serviceId = a.ServiceId, service = a.Service.Name,
                client = a.Client.Name, phone = a.Client.Phone, status = a.Status.ToString(),
                candidates = candidates.Select(c => new { c.Id, c.Name }).ToList()
            });
        }

        return Ok(new { masterId = id, masterName = m.Name, futureAppointments = items });
    }

    /// <summary>
    /// Звільнення майстра: усі майбутні записи або скасовуються (CancelAll), або переназначаються
    /// активним майстрам через ValidateAsync (Reassign). Працює в Serializable-транзакції — при помилці
    /// валідації нічого не змінюється. Нотифікації клієнтам — після CommitAsync (SendAsync робить власні SaveChanges).
    /// </summary>
    [HttpPost("masters/{id}/deactivate")]
    public async Task<IActionResult> Deactivate(int id, DeactivationRequest dto, CancellationToken ct)
    {
        var m = await db.Masters.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m == null) return NotFound(new { error = "Майстра не знайдено" });

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var future = await db.Appointments
            .Include(a => a.Service)
            .Include(a => a.Client)
            .Where(a => a.MasterId == id
                && a.StartTime > DateTime.UtcNow
                && (a.Status == AppointmentStatus.Confirmed || a.Status == AppointmentStatus.PendingVerification))
            .OrderBy(a => a.StartTime)
            .ToListAsync(ct);

        var plan = dto.Reassignments == null
            ? new Dictionary<int, int>()
            : dto.Reassignments.ToDictionary(r => r.AppointmentId, r => r.MasterId);

        if (dto.Policy == "Reassign")
        {
            foreach (var a in future)
            {
                var newMasterId = plan.GetValueOrDefault(a.Id, -1);
                if (newMasterId <= 0)
                    return BadRequest(new { error = $"Для запису #{a.Id} не заданий новий майстер" });
                var check = await schedule.ValidateAsync(newMasterId, a.ServiceId, a.StartTime, a.Id, ct);
                if (!check.Ok)
                {
                    if (check.StatusCode == 404) return NotFound(new { error = check.Error });
                    if (check.StatusCode == 409) return Conflict(new { error = check.Error });
                    return BadRequest(new { error = check.Error });
                }
                a.MasterId = newMasterId;
                a.ReminderSentAt = null;
            }
        }
        else if (dto.Policy != "CancelAll")
            return BadRequest(new { error = "Невідома політика деактивації (очікується CancelAll або Reassign)" });

        if (dto.Policy == "CancelAll")
            foreach (var a in future) a.Status = AppointmentStatus.Cancelled;

        m.IsActive = false;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync();

        // Нотифікації — після коміту (SendAsync робить власні SaveChangesAsync).
        foreach (var a in future)
        {
            var recipient = NotificationService.ResolveRecipient(a.Client, a.NotifyChannel);
            string msg;
            if (dto.Policy == "Reassign")
            {
                var nm = await db.Masters.FindAsync([a.MasterId], ct);
                msg = $"Ваш запис «{a.Service.Name}» {clock.Format(a.StartTime)} перенесено до майстра {nm!.Name}.";
            }
            else
                msg = $"Ваш запис «{a.Service.Name}» {clock.Format(a.StartTime)} скасовано: майстра звільнено.";
            logger.LogInformation("Деактивація майстра {MasterId} ({Policy}): запис {AppointmentId} -> {Recipient}", id, dto.Policy, a.Id, recipient);
            await notifier.SendAsync(a.NotifyChannel, recipient, msg);
        }

        return Ok(new { ok = true, affected = future.Count });
    }
    /// <summary>Переназначення запису іншому майстру з валідацією перетинів/графіка (ScheduleService.ValidateAsync).</summary>
    [HttpPut("appointments/{id}/master")]
    public async Task<IActionResult> SetAppointmentMaster(int id, SetAppointmentMasterDto dto, CancellationToken ct)
    {
        var appt = await db.Appointments.Include(a => a.Service).Include(a => a.Client).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (appt == null) return NotFound(new { error = "Запис не знайдено" });

        var check = await schedule.ValidateAsync(dto.MasterId, appt.ServiceId, appt.StartTime, appt.Id, ct);
        if (!check.Ok)
        {
            if (check.StatusCode == 404) return NotFound(new { error = check.Error });
            if (check.StatusCode == 409) return Conflict(new { error = check.Error });
            return BadRequest(new { error = check.Error });
        }

        var nm = await db.Masters.FindAsync([dto.MasterId], ct);
        var old = await db.Masters.FindAsync([appt.MasterId], ct);
        appt.MasterId = dto.MasterId;
        appt.ReminderSentAt = null;
        await db.SaveChangesAsync(ct);

        var recipient = NotificationService.ResolveRecipient(appt.Client, appt.NotifyChannel);
        var msg = $"Ваш запис «{appt.Service.Name}» {clock.Format(appt.StartTime)} перенесено до майстра {nm!.Name}.";
        logger.LogInformation("Переназначення запису {Id}: {Old} -> {New}, отримувач {Recipient}", id, old!.Name, nm.Name, recipient);
        await notifier.SendAsync(appt.NotifyChannel, recipient, msg);

        return Ok(new { ok = true });
    }

    /// <summary>Прев'ю ротації для ще не збереженого майстра — 15 днів від сьогодні, без звернення до БД.</summary>
    [HttpPost("rotation-preview")]
    public async Task<IActionResult> RotationPreview(RotationPreviewDto dto)
    {
        if (!Enum.TryParse<RotationType>(dto.RotationType, true, out var rt))
            return BadRequest(new { error = "Невідомий тип графіка" });
        var preview = schedule.PreviewRotation(rt, dto.RotationAnchor);
        return Ok(preview.Select(d => new { date = d.Date, isWorking = d.IsWorking }));
    }

    /// <summary>Обов'язкові обмеження коректності даних майстра (Фаза 11). null — валідацію пройдено.</summary>
    private IActionResult? ValidateMasterData(MasterDto dto)
    {
        if (dto.ServiceIds == null || dto.ServiceIds.Count == 0)
            return BadRequest(new { error = "Оберіть хоча б одну послугу майстра" });
        return null;
    }

    /// <summary>Години закладу (єдині на всі дні). Якщо рядок ще не створено — дефолт 09:00–18:00.</summary>
    [HttpGet("salon-hours")]
    public async Task<IActionResult> GetSalonHours(CancellationToken ct)
    {
        var s = await db.SalonSettings.FirstOrDefaultAsync(ct);
        var open = s?.OpenTime ?? new TimeOnly(9, 0);
        var close = s?.CloseTime ?? new TimeOnly(18, 0);
        return Ok(new { openTime = open, closeTime = close });
    }

    /// <summary>Зміна годин закладу. Діє на всі майбутні слоти й перевірки (ScheduleService читає з БД).</summary>
    [HttpPut("salon-hours")]
    public async Task<IActionResult> UpdateSalonHours(SalonHoursDto dto, CancellationToken ct)
    {
        if (dto.CloseTime <= dto.OpenTime)
            return BadRequest(new { error = "Час закриття має бути пізніше часу відкриття" });
        var s = await db.SalonSettings.FirstOrDefaultAsync(ct);
        if (s == null)
        {
            s = new SalonSettings { OpenTime = dto.OpenTime, CloseTime = dto.CloseTime };
            db.SalonSettings.Add(s);
        }
        else
        {
            s.OpenTime = dto.OpenTime;
            s.CloseTime = dto.CloseTime;
        }
        await db.SaveChangesAsync(ct);
        return Ok(new { openTime = s.OpenTime, closeTime = s.CloseTime });
    }

    [HttpGet("appointments")]
    public async Task<IActionResult> Appointments([FromQuery] DateTime? from)
    {
        var query = db.Appointments.Include(a => a.Client).Include(a => a.Master).Include(a => a.Service).AsQueryable();
        if (from.HasValue) query = query.Where(a => a.StartTime >= from);
        var list = await query.OrderBy(a => a.StartTime).ToListAsync();
        return Ok(list.Select(a => new
        {
            a.Id, client = a.Client.Name, phone = a.Client.Phone, service = a.Service.Name, master = a.Master.Name,
            a.StartTime, a.EndTime, status = a.Status.ToString()
        }));
    }

    [HttpPut("appointments/{id}/reschedule")]
    public async Task<IActionResult> Reschedule(int id, RescheduleDto dto)
    {
        var appt = await db.Appointments.Include(a => a.Service).FirstOrDefaultAsync(a => a.Id == id);
        if (appt == null) return NotFound();
        var start = DateTime.SpecifyKind(dto.StartTime, DateTimeKind.Utc);
        appt.EndTime = start.AddMinutes(appt.Service.DurationMin);
        appt.StartTime = start;
        appt.ReminderSentAt = null;   // нагадування буде за 3 год до нового часу
        if (appt.Status == AppointmentStatus.PendingVerification) appt.Status = AppointmentStatus.Confirmed;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPut("appointments/{id}/status")]
    public async Task<IActionResult> SetStatus(int id, AppointmentStatusDto dto)
    {
        if (!Enum.TryParse<AppointmentStatus>(dto.Status, true, out var status))
            return BadRequest(new { error = "Невірний статус" });
        var appt = await db.Appointments.FindAsync(id);
        if (appt == null) return NotFound();
        appt.Status = status;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>
    /// Повне (hard) видалення запису. Рядок зникає з таблиці Appointments — тому автоматично
    /// зникає в кабінеті клієнта (той самий рядок) і звільняє час майстра (ScheduleService
    /// більше не бачить запису). Пов'язані коди підтвердження (VerificationCode.AppointmentId)
    /// видаляються каскадно вручну, щоб не впасти на FK.
    /// </summary>
    [HttpDelete("appointments/{id}")]
    public async Task<IActionResult> DeleteAppointment(int id, CancellationToken ct)
    {
        var appt = await db.Appointments.FindAsync([id], ct);
        if (appt == null) return NotFound(new { error = "Запис не знайдено" });

        var codes = await db.VerificationCodes.Where(v => v.AppointmentId == id).ToListAsync(ct);
        if (codes.Count > 0) db.VerificationCodes.RemoveRange(codes);
        db.Appointments.Remove(appt);
        await db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    /// <summary>Відхилення графіка майстра у вікні бронювання (для редактора-календаря).</summary>
    [HttpGet("masters/{id}/day-overrides")]
    public async Task<IActionResult> GetDayOverrides(int id, CancellationToken ct)
    {
        if (!await db.Masters.AnyAsync(m => m.Id == id, ct))
            return NotFound(new { error = "Майстра не знайдено" });
        var today = clock.TodayInSalon;
        var list = await db.MasterDayOverrides
            .Where(o => o.MasterId == id && o.Date >= today && o.Date <= today.AddDays(ScheduleService.WindowDays))
            .OrderBy(o => o.Date)
            .ToListAsync(ct);
        return Ok(list.Select(o => new { date = o.Date, isWorking = o.IsWorking, start = o.Start, end = o.End }));
    }

    /// <summary>
    /// Створення/оновлення відхилення дня (upsert). Пріоритет: override &gt; (ротація + графік закладу).
    /// Години — в межах роботи закладу; дата — у вікні бронювання.
    /// </summary>
    [HttpPut("masters/{id}/day-override")]
    public async Task<IActionResult> PutDayOverride(int id, DayOverrideDto dto, CancellationToken ct)
    {
        if (!await db.Masters.AnyAsync(m => m.Id == id, ct))
            return NotFound(new { error = "Майстра не знайдено" });
        var today = clock.TodayInSalon;
        if (dto.Date < today || dto.Date.DayNumber - today.DayNumber > ScheduleService.WindowDays)
            return BadRequest(new { error = "Дата поза вікном бронювання" });

        TimeOnly? start = null, end = null;
        if (dto.IsWorking && (dto.Start.HasValue || dto.End.HasValue))
        {
            if (!dto.Start.HasValue || !dto.End.HasValue)
                return BadRequest(new { error = "Задайте і початок, і кінець робочого дня" });
            if (dto.End <= dto.Start)
                return BadRequest(new { error = "Час закінчення має бути пізніше часу початку" });
            var h = await db.SalonSettings.Select(s => new { s.OpenTime, s.CloseTime }).FirstOrDefaultAsync(ct);
            var open = h?.OpenTime ?? new TimeOnly(9, 0);
            var close = h?.CloseTime ?? new TimeOnly(18, 0);
            if (dto.Start < open || dto.End > close)
                return BadRequest(new { error = "Години мають бути в межах роботи закладу" });
            start = dto.Start;
            end = dto.End;
        }

        var ov = await db.MasterDayOverrides.FirstOrDefaultAsync(o => o.MasterId == id && o.Date == dto.Date, ct);
        if (ov == null)
        {
            ov = new MasterDayOverride { MasterId = id, Date = dto.Date };
            db.MasterDayOverrides.Add(ov);
        }
        ov.IsWorking = dto.IsWorking;
        ov.Start = start;
        ov.End = end;
        await db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    /// <summary>Прибрання відхилення — дата повертається до стандартної поведінки (ротація + заклад).</summary>
    [HttpDelete("masters/{id}/day-override")]
    public async Task<IActionResult> DeleteDayOverride(int id, [FromQuery] DateOnly date, CancellationToken ct)
    {
        var ov = await db.MasterDayOverrides.FirstOrDefaultAsync(o => o.MasterId == id && o.Date == date, ct);
        if (ov == null) return NotFound(new { error = "Відхилення не знайдено" });
        db.MasterDayOverrides.Remove(ov);
        await db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }
}
