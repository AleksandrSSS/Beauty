using Beauty.Server.Data;
using Beauty.Server.Models;
using Beauty.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Beauty.Server.Controllers;

public record UpdateProfileDto(string? Name, string? Email, string? PreferredChannel, string? ExternalContact);

[ApiController]
[Route("api/account")]
[Authorize]
public class AccountController(AppDbContext db) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await db.Users.FindAsync(JwtService.GetUserId(User));
        if (user == null) return NotFound();
        return Ok(new { user.Id, user.Phone, user.Name, user.Email, role = user.Role.ToString(), user.PreferredChannel, user.ExternalContact });
    }

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(UpdateProfileDto dto)
    {
        var user = await db.Users.FindAsync(JwtService.GetUserId(User));
        if (user == null) return NotFound();
        if (dto.Name != null) user.Name = dto.Name;
        if (dto.Email != null) user.Email = dto.Email;
        if (dto.PreferredChannel != null) user.PreferredChannel = dto.PreferredChannel;
        if (dto.ExternalContact != null) user.ExternalContact = dto.ExternalContact;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>Історія всіх записів клієнта.</summary>
    [HttpGet("appointments")]
    public async Task<IActionResult> MyAppointments()
    {
        var userId = JwtService.GetUserId(User);
        var list = await db.Appointments.Include(a => a.Service).Include(a => a.Master)
            .Where(a => a.ClientId == userId)
            .OrderByDescending(a => a.StartTime).ToListAsync();
        return Ok(list.Select(a => new
        {
            a.Id, service = a.Service.Name, master = a.Master.Name,
            a.StartTime, a.EndTime, status = a.Status.ToString(), a.NotifyChannel, price = a.Service.Price
        }));
    }

    [HttpPost("appointments/{id}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        var userId = JwtService.GetUserId(User);
        var appt = await db.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.ClientId == userId);
        if (appt == null) return NotFound();
        if (appt.Status == AppointmentStatus.Completed) return BadRequest(new { error = "Візит уже завершено" });
        appt.Status = AppointmentStatus.Cancelled;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
