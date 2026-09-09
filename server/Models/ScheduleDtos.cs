using Beauty.Server.Models;

namespace Beauty.Server.Models;

/// <summary>Майстер у списку вільних на слот (режим slots без masterId).</summary>
public record SlotMasterDto(int Id, string Name);

/// <summary>Вільний слот. Masters заповнено лише в режимі без masterId.</summary>
public record SlotDto(DateTime Start, DateTime End, List<SlotMasterDto>? Masters);

/// <summary>Зайнятий інтервал у графіку майстра (без даних клієнтів — публічний ендпоінт).</summary>
public record BusyIntervalDto(DateTime Start, DateTime End);

/// <summary>Один день графіка майстра.</summary>
public record MasterDayDto(DateOnly Date, bool IsWorking, TimeOnly? Start, TimeOnly? End, List<BusyIntervalDto> Busy);

/// <summary>Результат перевірки можливості запису. StatusCode — HTTP-код для контролерів (400/404/409).</summary>
public record BookingCheck(bool Ok, string? Error, int StatusCode = 400);

/// <summary>Запит прев'ю ротації для ще не збереженого майстра.</summary>
public record RotationPreviewDto(string RotationType, DateOnly RotationAnchor);

/// <summary>Короткий опис послуги майстра (лише активні) для сторінки майстра.</summary>
public record MasterServiceBriefDto(int Id, string Name, string Category, int DurationMin, decimal Price);

/// <summary>Публічні дані майстра для сторінки /masters/:id.</summary>
public record MasterDetailDto(int Id, string Name, string? Specialty, string? Bio, string? PhotoUrl,
    string RotationType, DateOnly RotationAnchor,
    bool WorksToday, bool WorksTomorrow, bool WorksThisWeek, bool WorksNextWeek,
    List<MasterServiceBriefDto> Services);