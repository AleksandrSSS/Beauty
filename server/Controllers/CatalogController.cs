using Beauty.Server.Data;
using Beauty.Server.Models;
using Beauty.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Beauty.Server.Controllers;

[ApiController]
[Route("api/catalog")]
public class CatalogController(AppDbContext db, SalonClock clock, ScheduleService schedule) : ControllerBase
{
    [HttpGet("services")]
    public async Task<IActionResult> Services() =>
        Ok(await db.Services.Where(s => s.IsActive).OrderBy(s => s.Category).ThenBy(s => s.Name).ToListAsync());

    [HttpGet("masters")]
    public async Task<IActionResult> Masters()
    {
        var masters = await db.Masters.Include(m => m.MasterServices).Where(m => m.IsActive).ToListAsync();
        var today = clock.TodayInSalon;
        return Ok(masters.Select(m => new
        {
            m.Id, m.Name, m.Specialty, m.Bio, m.PhotoUrl,
            services = m.MasterServices.Select(ms => ms.ServiceId),
            rotationType = m.RotationType.ToString(),
            // «Цей/наступний тиждень» = робочий день у найближчі 7 / наступні 7 днів (без календарних меж).
            worksThisWeek = Enumerable.Range(0, 7).Any(i => RotationHelper.WorksOn(m, today.AddDays(i))),
            worksNextWeek = Enumerable.Range(7, 7).Any(i => RotationHelper.WorksOn(m, today.AddDays(i))),
            worksToday = RotationHelper.WorksOn(m, today),
            worksTomorrow = RotationHelper.WorksOn(m, today.AddDays(1))
        }));
    }

    /// <summary>Публічна сторінка майстра: дані + лише активні послуги.</summary>
    [AllowAnonymous]
    [HttpGet("masters/{id}")]
    public async Task<IActionResult> Master(int id, CancellationToken ct)
    {
        var m = await db.Masters
            .Include(x => x.MasterServices).ThenInclude(ms => ms.Service)
            .FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
        if (m == null) return NotFound(new { error = "Майстра не знайдено" });

        var today = clock.TodayInSalon;
        var services = m.MasterServices
            .Where(ms => ms.Service.IsActive)
            .OrderBy(ms => ms.Service.Category).ThenBy(ms => ms.Service.Name)
            .Select(ms => new MasterServiceBriefDto(ms.Service.Id, ms.Service.Name, ms.Service.Category,
                ms.Service.DurationMin, ms.Service.Price))
            .ToList();

        return Ok(new MasterDetailDto(m.Id, m.Name, m.Specialty, m.Bio, m.PhotoUrl,
            m.RotationType.ToString(), m.RotationAnchor,
            RotationHelper.WorksOn(m, today),
            RotationHelper.WorksOn(m, today.AddDays(1)),
            Enumerable.Range(0, 7).Any(i => RotationHelper.WorksOn(m, today.AddDays(i))),
            Enumerable.Range(7, 7).Any(i => RotationHelper.WorksOn(m, today.AddDays(i))),
            services));
    }

    /// <summary>Графік майстра на 28 днів: робочі дні, години, зайняті інтервали (без даних клієнтів).</summary>
    [AllowAnonymous]
    [HttpGet("masters/{id}/schedule")]
    public async Task<IActionResult> Schedule(int id, CancellationToken ct)
    {
        try
        {
            return Ok(await schedule.GetMasterScheduleAsync(id, ct));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}
