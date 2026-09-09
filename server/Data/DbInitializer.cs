using Beauty.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Beauty.Server.Data;

/// <summary>
/// Початкове заповнення БД. Викликається при старті після `Migrate()`.
/// Ідемпотентний: адмін створюється лише якщо його немає, демо-дані — лише на порожніх послугах.
/// Семантика сіду не змінювалась (див. masters-feature-plan, Фаза 1).
/// </summary>
public static class DbInitializer
{
    public static void Seed(AppDbContext db, IConfiguration cfg)
    {
        db.Database.Migrate();

        if (!db.Users.Any(u => u.Role == UserRole.Admin))
        {
            db.Users.Add(new User
            {
                Phone = cfg["Admin:Phone"] ?? "+380000000000",
                Name = cfg["Admin:Name"] ?? "Адміністратор",
                Role = UserRole.Admin,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(cfg["Admin:Password"] ?? throw new InvalidOperationException(
                    "Admin:Password не задано. Збережіть його через 'dotnet user-secrets set \"Admin:Password\" \"<пароль>\"'.")),
                PreferredChannel = NotifyChannel.Sms.ToString()
            });
            db.SaveChanges();
        }

        // Єдиний графік закладу (затверджено 09:00–18:00, однаково на всі дні).
        if (!db.SalonSettings.Any())
        {
            db.SalonSettings.Add(new SalonSettings { OpenTime = new TimeOnly(9, 0), CloseTime = new TimeOnly(18, 0) });
            db.SaveChanges();
        }

        if (!db.Services.Any())
        {
            db.Services.AddRange(
                new Service { Name = "Стрижка чоловіча класична", Category = "hair", DurationMin = 30, Price = 300, Description = "Класична чоловіча стрижка" },
                new Service { Name = "Стрижка жіноча", Category = "hair", DurationMin = 90, Price = 550, Description = "Стрижка + укладка" },
                new Service { Name = "Укладка", Category = "hair", DurationMin = 45, Price = 350, Description = "Стайлінг та укладка" },
                new Service { Name = "Манікюр класичний", Category = "manicure", DurationMin = 60, Price = 350 },
                new Service { Name = "Манікюр з покриттям гель-лак", Category = "manicure", DurationMin = 120, Price = 550 },
                new Service { Name = "Педикюр", Category = "manicure", DurationMin = 75, Price = 450 });
            db.SaveChanges();

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            static DateOnly CurrentMonday() => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-(((int)DateTime.UtcNow.DayOfWeek + 6) % 7));

            // Перукарки (4): працюють у двох змінах 2/2 — роблять усі стрижки
            var hairdresser1 = new Master
            {
                Name = "Оксана", Specialty = "Перукар", Bio = "Жіночі та чоловічі стрижки, досвід 8 років",
                RotationType = RotationType.TwoTwo, RotationAnchor = today
            };
            var hairdresser2 = new Master
            {
                Name = "Ірина", Specialty = "Перукар", Bio = "Жіночі та чоловічі стрижки, досвід 6 років",
                RotationType = RotationType.TwoTwo, RotationAnchor = today
            };
            var hairdresser3 = new Master
            {
                Name = "Наталія", Specialty = "Перукар", Bio = "Стрижки та укладки, досвід 5 років",
                RotationType = RotationType.TwoTwo, RotationAnchor = today.AddDays(2)
            };
            var hairdresser4 = new Master
            {
                Name = "Тетяна", Specialty = "Перукар", Bio = "Стрижки та укладки, досвід 4 роки",
                RotationType = RotationType.TwoTwo, RotationAnchor = today.AddDays(2)
            };
            // Майстрині манікюру: працюють тижнями по черзі, роблять усі манікюрні послуги
            var nailTech1 = new Master
            {
                Name = "Марія", Specialty = "Майстер манікюру", Bio = "Стерео-манікюр, дизайн",
                RotationType = RotationType.Weekly, RotationAnchor = CurrentMonday()
            };
            var nailTech2 = new Master
            {
                Name = "Олена", Specialty = "Майстер манікюру", Bio = "Гель-лак, педикюр, дизайн",
                RotationType = RotationType.Weekly, RotationAnchor = CurrentMonday().AddDays(7)
            };
            db.Masters.AddRange(hairdresser1, hairdresser2, hairdresser3, hairdresser4, nailTech1, nailTech2);
            db.SaveChanges();

            var hairServiceIds = db.Services.Where(s => s.Category == "hair").Select(s => s.Id).ToList();
            var manicureServiceIds = db.Services.Where(s => s.Category == "manicure").Select(s => s.Id).ToList();
            foreach (var sid in hairServiceIds)
                foreach (var h in new[] { hairdresser1, hairdresser2, hairdresser3, hairdresser4 })
                    db.MasterServices.Add(new MasterService { MasterId = h.Id, ServiceId = sid });
            foreach (var sid in manicureServiceIds)
            {
                db.MasterServices.Add(new MasterService { MasterId = nailTech1.Id, ServiceId = sid });
                db.MasterServices.Add(new MasterService { MasterId = nailTech2.Id, ServiceId = sid });
            }
            db.SaveChanges();
        }
    }
}
