using Beauty.Server.Data;
using Beauty.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Beauty.Server.Services;

/// <summary>
/// Єдине джерело правил графіка: ротація, робочі години, зайнятість, вікно бронювання.
/// Використовують BookingController (slots/create), CatalogController (графік майстра),
/// AdminController (переназначення, прев'ю ротації).
/// </summary>
public class ScheduleService(AppDbContext db, SalonClock clock)
{
    /// <summary>Глибина вікна бронювання: 4 тижні наперед.</summary>
    public const int WindowDays = 28;

    /// <summary>Записи, що блокують слот: підтверджені і свіжі (15 хв) очікуючі.</summary>
    private static readonly TimeSpan PendingHold = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Години закладу (єдині на всі дні тижня). Фолбек 09:00–18:00, якщо рядок ще не створено сідом.
    /// </summary>
    private async Task<(TimeOnly Open, TimeOnly Close)> GetHoursAsync(CancellationToken ct)
    {
        var h = await db.SalonSettings.Select(s => new { s.OpenTime, s.CloseTime }).FirstOrDefaultAsync(ct);
        return h == null ? (new TimeOnly(9, 0), new TimeOnly(18, 0)) : (h.OpenTime, h.CloseTime);
    }

    /// <summary>
    /// Ефективний графік дня майстра з урахуванням override.
    /// Пріоритет: override &gt; (ротація + графік закладу). Джерело правди годин — override або заклад.
    /// </summary>
    private static (bool Works, TimeOnly Open, TimeOnly Close) EffectiveDay(
        Master master, DateOnly day, TimeOnly salonOpen, TimeOnly salonClose,
        Dictionary<(int MasterId, DateOnly Date), MasterDayOverride> overrides)
    {
        if (overrides.TryGetValue((master.Id, day), out var ov))
        {
            if (!ov.IsWorking) return (false, salonOpen, salonClose);
            return (true, ov.Start ?? salonOpen, ov.End ?? salonClose);
        }
        return (RotationHelper.WorksOn(master, day), salonOpen, salonClose);
    }

    /// <summary>
    /// Вільні слоти. masterId == null → усі активні майстри послуги,
    /// кожен слот містить список вільних майстрів.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Майстра або послугу не знайдено (або неактивні / не пов'язані).</exception>
    public async Task<List<SlotDto>> GetSlotsAsync(int? masterId, int serviceId, CancellationToken ct = default)
    {
        var service = await db.Services.FindAsync([serviceId], ct);
        if (service == null || !service.IsActive)
            throw new KeyNotFoundException("Послугу не знайдено");

        List<Master> masters;
        if (masterId.HasValue)
        {
            var master = await db.Masters
                .Include(m => m.MasterServices)
                .FirstOrDefaultAsync(m => m.Id == masterId.Value && m.IsActive, ct);
            if (master == null || !master.MasterServices.Any(ms => ms.ServiceId == serviceId))
                throw new KeyNotFoundException("Майстра або послугу не знайдено");
            masters = [master];
        }
        else
        {
            masters = await db.Masters
                .Include(m => m.MasterServices)
                .Where(m => m.IsActive && m.MasterServices.Any(ms => ms.ServiceId == serviceId))
                .ToListAsync(ct);
        }

        var (open, close) = await GetHoursAsync(ct);

        var today = clock.TodayInSalon;
        var lastDay = today.AddDays(WindowDays);
        var windowStartUtc = clock.ToUtc(today, TimeOnly.MinValue);
        var taken = await ActiveAppointments(masterId, windowStartUtc, null, ct);

        // Override на вікно одним запитом (для masterId==null — по всіх майстрах послуги).
        var masterIds = masters.Select(m => m.Id).ToList();
        var overrides = await db.MasterDayOverrides
            .Where(o => o.Date >= today && o.Date <= lastDay && masterIds.Contains(o.MasterId))
            .ToListAsync(ct);
        var ovByKey = overrides.ToDictionary(o => (o.MasterId, o.Date));

        var slots = new List<SlotDto>();
        for (var day = today; day <= lastDay; day = day.AddDays(1))
        {
            var daySlots = new List<(DateTime Start, DateTime End, Master Master)>();
            if (close <= open) continue; // заклад зачинено — слотів цього дня немає
            foreach (var master in masters)
            {
                // Ефективний графік дня: override поверх (ротація + графік закладу).
                var (works, dayOpen, dayClose) = EffectiveDay(master, day, open, close, ovByKey);
                if (!works || dayClose <= dayOpen) continue;

                var t = dayOpen;
                while (t.AddMinutes(service.DurationMin) <= dayClose)
                {
                    var startUtc = clock.ToUtc(day, t);
                    var endUtc = startUtc.AddMinutes(service.DurationMin);
                    var busy = taken.Any(x => x.MasterId == master.Id && startUtc < x.EndTime && endUtc > x.StartTime);
                    if (!busy && startUtc > DateTime.UtcNow)
                        daySlots.Add((startUtc, endUtc, master));
                    t = t.AddMinutes(30);
                }
            }

            foreach (var g in daySlots.GroupBy(s => s.Start).OrderBy(g => g.Key))
            {
                var first = g.First();
                slots.Add(new SlotDto(
                    first.Start, first.End,
                    masterId.HasValue ? null : g.Select(s => new SlotMasterDto(s.Master.Id, s.Master.Name)).ToList()));
            }
        }
        return slots;
    }

    /// <summary>
    /// Графік майстра на 28 днів без прив'язки до послуги: робочі дні, години,
    /// зайняті інтервали (без даних клієнтів).
    /// </summary>
    /// <exception cref="KeyNotFoundException">Майстра не знайдено або неактивний.</exception>
    public async Task<List<MasterDayDto>> GetMasterScheduleAsync(int masterId, CancellationToken ct = default)
    {
        var master = await db.Masters
            .FirstOrDefaultAsync(m => m.Id == masterId && m.IsActive, ct);
        if (master == null) throw new KeyNotFoundException("Майстра не знайдено");

        var (open, close) = await GetHoursAsync(ct);

        var today = clock.TodayInSalon;
        var windowStartUtc = clock.ToUtc(today, TimeOnly.MinValue);
        var taken = await ActiveAppointments(masterId, windowStartUtc, null, ct);

        var ovByKey = (await db.MasterDayOverrides
                .Where(o => o.MasterId == masterId && o.Date >= today && o.Date <= today.AddDays(WindowDays))
                .ToListAsync(ct))
            .ToDictionary(o => (o.MasterId, o.Date));

        var days = new List<MasterDayDto>();
        for (var day = today; day <= today.AddDays(WindowDays); day = day.AddDays(1))
        {
            var (works, dayOpen, dayClose) = EffectiveDay(master, day, open, close, ovByKey);
            var working = works && dayClose > dayOpen;
            days.Add(new MasterDayDto(
                day,
                working,
                working ? dayOpen : null,
                working ? dayClose : null,
                working
                    ? taken
                        .Where(x => x.StartTime < clock.ToUtc(day, dayClose) && x.EndTime > clock.ToUtc(day, dayOpen))
                        .Select(x => new BusyIntervalDto(x.StartTime, x.EndTime))
                        .OrderBy(b => b.Start)
                        .ToList()
                    : []));
        }
        return days;
    }

    /// <summary>
    /// Прев'ю ротації для довільних параметрів, без звернення до БД.
    /// Для ще не збереженого майстра у формі адмінки.
    /// Вікно — 5 повних календарних тижнів від понеділка тижня якоря, щоб останній
    /// тиждень не обрізався посеред робочих днів (видно весь тиждень цілком,
    /// а не лише його частину в межах N днів від anchor).
    /// </summary>
    public List<MasterDayDto> PreviewRotation(RotationType type, DateOnly anchor)
    {
        var probe = new Master { RotationType = type, RotationAnchor = anchor };
        var start = RotationHelper.MondayOf(anchor);
        return Enumerable.Range(0, 7 * 5)
            .Select(i => start.AddDays(i))
            .Select(day => new MasterDayDto(day, RotationHelper.WorksOn(probe, day), null, null, []))
            .ToList();
    }

    /// <summary>
    /// Чи можна поставити запис майстру на цей час (UTC).
    /// ignoreAppointmentId — щоб запис не конфліктував сам із собою при переназначенні.
    /// </summary>
    public async Task<BookingCheck> ValidateAsync(int masterId, int serviceId, DateTime startUtc,
        int? ignoreAppointmentId = null, CancellationToken ct = default)
    {
        var master = await db.Masters
            .Include(m => m.MasterServices)
            .FirstOrDefaultAsync(m => m.Id == masterId && m.IsActive, ct);
        var service = await db.Services.FindAsync([serviceId], ct);
        if (master == null || service == null || !service.IsActive
            || !master.MasterServices.Any(ms => ms.ServiceId == serviceId))
            return new BookingCheck(false, "Майстра або послугу не знайдено", 404);

        var start = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        var end = start.AddMinutes(service.DurationMin);
        var startSalon = clock.ToSalon(start);
        var endSalon = clock.ToSalon(end);
        var day = DateOnly.FromDateTime(startSalon);

        if (start <= DateTime.UtcNow || day.DayNumber - clock.TodayInSalon.DayNumber > WindowDays)
            return new BookingCheck(false, "Час поза доступним вікном бронювання", 400);
        var (open, close) = await GetHoursAsync(ct);
        var ov = await db.MasterDayOverrides.FirstOrDefaultAsync(o => o.MasterId == masterId && o.Date == day, ct);
        var ovByKey = ov == null
            ? new Dictionary<(int MasterId, DateOnly Date), MasterDayOverride>()
            : new Dictionary<(int MasterId, DateOnly Date), MasterDayOverride> { [(masterId, day)] = ov };
        var (works, dayOpen, dayClose) = EffectiveDay(master, day, open, close, ovByKey);
        if (!works)
            return new BookingCheck(false, "Майстер не працює цього дня", 400);
        if (dayClose <= dayOpen || TimeOnly.FromDateTime(startSalon) < dayOpen || TimeOnly.FromDateTime(endSalon) > dayClose)
            return new BookingCheck(false, "Час поза годинами роботи закладу", 400);

        var busy = await db.Appointments.AnyAsync(a => a.MasterId == master.Id
            && a.Id != ignoreAppointmentId
            && (a.Status == AppointmentStatus.Confirmed
                || (a.Status == AppointmentStatus.PendingVerification && a.CreatedAt > DateTime.UtcNow.AddMinutes(-PendingHold.TotalMinutes)))
            && a.StartTime < end && a.EndTime > start, ct);
        if (busy)
            return new BookingCheck(false, "Цей час уже зайнятий або очікує підтвердження", 409);

        return new BookingCheck(true, null);
    }

    /// <summary>Активні (блокуючі) записи майстра (або всіх при masterId == null) від межі вікна.</summary>
    private async Task<List<Appointment>> ActiveAppointments(int? masterId, DateTime windowStartUtc,
        int? ignoreAppointmentId, CancellationToken ct) =>
        await db.Appointments
            .Where(a => (masterId == null || a.MasterId == masterId)
                && a.Id != ignoreAppointmentId
                && (a.Status == AppointmentStatus.Confirmed
                    || (a.Status == AppointmentStatus.PendingVerification && a.CreatedAt > DateTime.UtcNow.AddMinutes(-PendingHold.TotalMinutes)))
                && a.StartTime >= windowStartUtc)
            .Select(a => new Appointment { MasterId = a.MasterId, StartTime = a.StartTime, EndTime = a.EndTime })
            .ToListAsync(ct);
}
