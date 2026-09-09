using Beauty.Server.Models;

namespace Beauty.Server.Data;

public static class RotationHelper
{
    /// <summary>Понеділок календарного тижня, до якого належить день.</summary>
    public static DateOnly MondayOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>
    /// Чи працює майстер у вказаний день:
    /// - Weekly: тиждень якоря (Пн–Нд) — робочий з дня anchor до неділі, далі повні
    ///   календарні тижні через один. Дні раніше anchor — не робочі (майстер ще не почав).
    ///   Перший робочий день — сам anchor (будь-який день тижня);
    /// - TwoTwo: 2 дні працює, 2 вихідні, від якірного дня.
    /// </summary>
    public static bool WorksOn(Master master, DateOnly day) => master.RotationType switch
    {
        RotationType.TwoTwo => (((day.DayNumber - master.RotationAnchor.DayNumber) % 4) + 4) % 4 < 2,
        _ => day >= master.RotationAnchor
            && (((MondayOf(day).DayNumber - MondayOf(master.RotationAnchor).DayNumber) / 7) % 2 + 2) % 2 == 0
    };
}
