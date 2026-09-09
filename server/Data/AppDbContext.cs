using Beauty.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Beauty.Server.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<Master> Masters => Set<Master>();
    public DbSet<MasterService> MasterServices => Set<MasterService>();
    public DbSet<SalonSettings> SalonSettings => Set<SalonSettings>();
    public DbSet<MasterDayOverride> MasterDayOverrides => Set<MasterDayOverride>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<VerificationCode> VerificationCodes => Set<VerificationCode>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<User>().HasIndex(u => u.Phone).IsUnique();
        mb.Entity<MasterService>().HasKey(ms => new { ms.MasterId, ms.ServiceId });
        mb.Entity<Appointment>().HasIndex(a => new { a.MasterId, a.StartTime });
        // Один override на дату на майстра; каскадне видалення при hard-delete майстра.
        mb.Entity<MasterDayOverride>().HasIndex(o => new { o.MasterId, o.Date }).IsUnique();
    }
}
