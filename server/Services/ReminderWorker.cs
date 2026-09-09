using Beauty.Server.Data;
using Beauty.Server.Models;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace Beauty.Server.Services;

/// <summary>
/// Кожні 5 хв шукає підтверджені візити, які починаються за ~3 години, і надсилає нагадування.
/// Час показів — у таймзоні салону (SalonClock).
/// </summary>
public class ReminderWorker(IServiceProvider sp, SalonClock clock, ILogger<ReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await RunOnceAsync(ct); }
            catch (Exception ex) { logger.LogError(ex, "ReminderWorker error"); }
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var from = DateTime.UtcNow.AddHours(3).AddMinutes(-10);
        var to = DateTime.UtcNow.AddHours(3).AddMinutes(10);

        var appointments = await db.Appointments
            .Include(a => a.Client).Include(a => a.Master).Include(a => a.Service)
            .Where(a => a.Status == AppointmentStatus.Confirmed
                     && a.Master.IsActive
                     && a.ReminderSentAt == null
                     && a.StartTime >= from && a.StartTime <= to)
            .ToListAsync(ct);

        foreach (var a in appointments)
        {
            var recipient = NotificationService.ResolveRecipient(a.Client, a.NotifyChannel);
            var msg = $"Нагадування: у вас запис «{a.Service.Name}» до майстра {a.Master.Name} " +
                      $"{clock.Format(a.StartTime)}. Чекаємо на вас!";
            var result = await notifier.SendAsync(a.NotifyChannel, recipient, msg);
            if (result.Success)
            {
                a.ReminderSentAt = DateTime.UtcNow;
                logger.LogInformation("Нагадування по запису {Id}: {Result}", a.Id, result.Info);
            }
            else
            {
                a.ReminderSentAt = null;
                logger.LogWarning("Нагадування не надіслано по запису {Id}: {Result}", a.Id, result.Info);
            }
        }
        if (appointments.Count > 0) await db.SaveChangesAsync(ct);
    }
}
