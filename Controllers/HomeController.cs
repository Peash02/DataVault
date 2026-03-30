using System.Security.Claims;
using DataVault.MVC.Data;
using DataVault.MVC.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DataVault.MVC.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly VaultDbContext _db;

    public HomeController(VaultDbContext db) => _db = db;

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("/")]
    public async Task<IActionResult> Index()
    {
        var userId = CurrentUserId;

        var files = await _db.Files.Where(f => f.OwnerId == userId && !f.IsDeleted).ToListAsync();

        var activeShares = await _db.ShareLinks
            .CountAsync(s => s.CreatedById == userId && !s.IsRevoked && s.ExpiresAt > DateTime.UtcNow);

        var allLogs = await _db.AccessLogs
            .Include(l => l.File)
            .Where(l => l.File.OwnerId == userId)
            .OrderByDescending(l => l.Timestamp)
            .ToListAsync();

        var directPermissions = await _db.FilePermissions
            .CountAsync(p => p.File.OwnerId == userId && p.IsActive);

        var stats = new DashboardStats(
            TotalFiles:            files.Count,
            TotalStorageBytes:     files.Sum(f => f.FileSizeBytes),
            ActiveShareLinks:      activeShares,
            TotalAccessEvents:     allLogs.Count,
            DeniedAccessAttempts:  allLogs.Count(l => !l.WasGranted),
            FilesSharedWithOthers: directPermissions
        );

        var recentLogs = allLogs.Take(10).Select(l => new AccessLogDto(
            l.Id, l.File.OriginalFileName, l.Action,
            l.ActorIdentifier, l.IpAddress, l.WasGranted,
            l.Timestamp, l.Details)).ToList();

        return View(new DashboardViewModel
        {
            Stats      = stats,
            RecentLogs = recentLogs,
            Username   = User.FindFirstValue(ClaimTypes.Name) ?? "User"
        });
    }
}
