namespace Beauty.Server.Models;

public enum UserRole { Client, Admin }
public enum AppointmentStatus { PendingVerification, Confirmed, Cancelled, Completed }
public enum NotifyChannel { Sms, Telegram, WhatsApp, Viber, Email }
/// <summary>Weekly — робочий тиждень через тиждень; TwoTwo — 2 дні працює, 2 вихідні.</summary>
public enum RotationType { Weekly, TwoTwo }

public class User
{
    public int Id { get; set; }
    public string Phone { get; set; } = default!;
    public string? Email { get; set; }
    public string Name { get; set; } = default!;
    public UserRole Role { get; set; } = UserRole.Client;
    public string? PasswordHash { get; set; }          // для адміна
    public string? PreferredChannel { get; set; }      // обраний канал нотифікацій
    public string? ExternalContact { get; set; }       // telegram chat id / email / etc.
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
}

public class Service
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string Category { get; set; } = "hair";   // hair | manicure
    public int DurationMin { get; set; } = 60;
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
    public List<MasterService> MasterServices { get; set; } = new();
}

public class Master
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public string? Specialty { get; set; }
    public string? Bio { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Відносний шлях до фото, напр. /uploads/masters/3-a1b2c3d4.webp. null → показуємо ініціали.</summary>
    public string? PhotoUrl { get; set; }
    /// <summary>Тип графіка: Weekly — тижні через тиждень; TwoTwo — 2/2 по днях.</summary>
    public RotationType RotationType { get; set; } = RotationType.Weekly;
    /// <summary>Якорна дата графіка: перший робочий день (будь-який день тижня). Weekly рахує 7/7 блоками від якоря, TwoTwo — 2/2.</summary>
    public DateOnly RotationAnchor { get; set; }
    public List<MasterService> MasterServices { get; set; } = new();
    public List<Appointment> Appointments { get; set; } = new();
    public List<MasterDayOverride> DayOverrides { get; set; } = new();
}

/// <summary>Єдиний графік закладу: одні години на всі 7 днів тижня. Рядок-одинак.</summary>
public class SalonSettings
{
    public int Id { get; set; }
    public TimeOnly OpenTime { get; set; }
    public TimeOnly CloseTime { get; set; }
}

/// <summary>Індивідуальне відхилення графіка майстра на конкретну дату (перекриває ротацію + графік закладу).</summary>
public class MasterDayOverride
{
    public int Id { get; set; }
    public int MasterId { get; set; }
    public Master Master { get; set; } = null!;
    public DateOnly Date { get; set; }
    /// <summary>true — майстер працює цю дату; false — вихідний (перекриває ротацію).</summary>
    public bool IsWorking { get; set; }
    /// <summary>Години override. null/null при IsWorking=true → повний день за графіком закладу.</summary>
    public TimeOnly? Start { get; set; }
    public TimeOnly? End { get; set; }
}

public class MasterService
{
    public int MasterId { get; set; }
    public Master Master { get; set; } = default!;
    public int ServiceId { get; set; }
    public Service Service { get; set; } = default!;
}

public class Appointment
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public User Client { get; set; } = default!;
    public int MasterId { get; set; }
    public Master Master { get; set; } = default!;
    public int ServiceId { get; set; }
    public Service Service { get; set; } = default!;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.PendingVerification;
    public NotifyChannel NotifyChannel { get; set; }
    public DateTime? ReminderSentAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class VerificationCode
{
    public int Id { get; set; }
    public string Phone { get; set; } = default!;
    public string Code { get; set; } = default!;
    public string Purpose { get; set; } = default!;    // login | booking
    public int? AppointmentId { get; set; }
    public NotifyChannel Channel { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}

public class NotificationLog
{
    public int Id { get; set; }
    public NotifyChannel Channel { get; set; }
    public string Recipient { get; set; } = default!;
    public string Message { get; set; } = default!;
    public bool Success { get; set; }
    public bool IsMock { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
