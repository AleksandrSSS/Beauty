namespace Beauty.Server.Services;

/// <summary>
/// Робота з часом салону: усі слоти й графіки зберігаються у таймзоні салону,
/// а в БД час пишеться в UTC. Таймзона береться з конфіга Salon:TimeZone (за замовчуванням Europe/Kyiv).
/// </summary>
public class SalonClock
{
    private readonly TimeZoneInfo _tz;

    public SalonClock(IConfiguration cfg)
    {
        var id = cfg["Salon:TimeZone"] ?? "Europe/Kyiv";
        try { _tz = TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { _tz = TimeZoneInfo.Local; }
        catch (InvalidTimeZoneException) { _tz = TimeZoneInfo.Local; }
    }

    public TimeZoneInfo TimeZone => _tz;

    /// <summary>Поточна дата у салоні.</summary>
    public DateOnly TodayInSalon => DateOnly.FromDateTime(ToSalon(DateTime.UtcNow));

    /// <summary>Перетворює "настінний" час салону на UTC (для запису в БД).</summary>
    public DateTime ToUtc(DateOnly day, TimeOnly time) =>
        TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(time), _tz);

    /// <summary>Перетворює UTC у локальний час салону.</summary>
    public DateTime ToSalon(DateTime utc) =>
        TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _tz);

    /// <summary>Форматує UTC-час у часі салону.</summary>
    public string Format(DateTime utc, string format = "dd.MM HH:mm") => ToSalon(utc).ToString(format);
}
