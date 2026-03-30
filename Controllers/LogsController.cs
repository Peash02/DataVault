using System.Security.Claims;
using DataVault.MVC.Data;
using DataVault.MVC.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DataVault.MVC.Controllers;

[Authorize]
public class LogsController : Controller
{
    private readonly VaultDbContext _db;

    public LogsController(VaultDbContext db) => _db = db;

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("/logs")]
    public async Task<IActionResult> Index(bool deniedOnly = false)
    {
        var userId = CurrentUserId;
        var query  = _db.AccessLogs
            .Include(l => l.File)
            .Where(l => l.File.OwnerId == userId);

        if (deniedOnly)
            query = query.Where(l => !l.WasGranted);

        var logs = await query
            .OrderByDescending(l => l.Timestamp)
            .Take(200)
            .Select(l => new AccessLogDto(
                l.Id, l.File.OriginalFileName, l.Action,
                l.ActorIdentifier, l.IpAddress, l.WasGranted,
                l.Timestamp, l.Details))
            .ToListAsync();

        return View(new LogsViewModel { Logs = logs, DeniedOnly = deniedOnly });
    }
}
