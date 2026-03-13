using System.Security.Claims;
using DataVault.API.Data;
using DataVault.API.Models;
using DataVault.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DataVault.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LogsController : ControllerBase
{
    private readonly VaultDbContext _db;
    private readonly IAccessLogService _logService;

    public LogsController(VaultDbContext db, IAccessLogService logService)
    {
        _db = db; _logService = logService;
    }

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Get all access logs for the current user's files</summary>
    [HttpGet]
    public async Task<ActionResult<List<AccessLogDto>>> GetMyLogs(
        [FromQuery] int take = 100,
        [FromQuery] AccessAction? action = null,
        [FromQuery] bool? deniedOnly = null)
    {
        var userId = CurrentUserId;
        var query = _db.AccessLogs
            .Include(l => l.File)
            .Where(l => l.File.OwnerId == userId);

        if (action.HasValue)
            query = query.Where(l => l.Action == action.Value);

        if (deniedOnly == true)
            query = query.Where(l => !l.WasGranted);

        var logs = await query
            .OrderByDescending(l => l.Timestamp)
            .Take(take)
            .Select(l => new AccessLogDto(
                l.Id, l.File.OriginalFileName, l.Action,
                l.ActorIdentifier, l.IpAddress, l.WasGranted,
                l.Timestamp, l.Details))
            .ToListAsync();

        return Ok(logs);
    }

    /// <summary>Get access logs for a specific file</summary>
    [HttpGet("file/{fileId}")]
    public async Task<ActionResult<List<AccessLogDto>>> GetFileLog(Guid fileId)
    {
        var userId = CurrentUserId;
        var file = await _db.Files.FirstOrDefaultAsync(f => f.Id == fileId && f.OwnerId == userId);
        if (file == null) return NotFound();

        var logs = await _db.AccessLogs
            .Where(l => l.FileId == fileId)
            .OrderByDescending(l => l.Timestamp)
            .Select(l => new AccessLogDto(
                l.Id, file.OriginalFileName, l.Action,
                l.ActorIdentifier, l.IpAddress, l.WasGranted,
                l.Timestamp, l.Details))
            .ToListAsync();

        return Ok(logs);
    }
}
