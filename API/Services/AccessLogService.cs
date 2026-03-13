using DataVault.API.Data;
using DataVault.API.Models;
using Microsoft.EntityFrameworkCore;

namespace DataVault.API.Services;

public interface IAccessLogService
{
    Task LogAsync(Guid fileId, AccessAction action, string actorIdentifier,
        bool wasGranted, Guid? userId = null, Guid? shareLinkId = null,
        string? ipAddress = null, string? userAgent = null, string? details = null);

    Task<List<AccessLog>> GetLogsForUserAsync(Guid userId, int take = 100);
    Task<List<AccessLog>> GetLogsForFileAsync(Guid fileId);
}

public class AccessLogService : IAccessLogService
{
    private readonly VaultDbContext _db;
    private readonly IHttpContextAccessor _httpContext;

    public AccessLogService(VaultDbContext db, IHttpContextAccessor httpContext)
    {
        _db = db;
        _httpContext = httpContext;
    }

    public async Task LogAsync(Guid fileId, AccessAction action, string actorIdentifier,
        bool wasGranted, Guid? userId = null, Guid? shareLinkId = null,
        string? ipAddress = null, string? userAgent = null, string? details = null)
    {
        var context = _httpContext.HttpContext;
        var log = new AccessLog
        {
            FileId = fileId,
            Action = action,
            ActorIdentifier = actorIdentifier,
            WasGranted = wasGranted,
            UserId = userId,
            ShareLinkId = shareLinkId,
            IpAddress = ipAddress ?? GetClientIp(context),
            UserAgent = userAgent ?? context?.Request.Headers["User-Agent"].ToString(),
            Details = details,
            Timestamp = DateTime.UtcNow
        };

        _db.AccessLogs.Add(log);
        await _db.SaveChangesAsync();
    }

    public async Task<List<AccessLog>> GetLogsForUserAsync(Guid userId, int take = 100)
    {
        return await _db.AccessLogs
            .Where(l => l.File.OwnerId == userId)
            .Include(l => l.File)
            .OrderByDescending(l => l.Timestamp)
            .Take(take)
            .ToListAsync();
    }

    public async Task<List<AccessLog>> GetLogsForFileAsync(Guid fileId)
    {
        return await _db.AccessLogs
            .Where(l => l.FileId == fileId)
            .Include(l => l.File)
            .OrderByDescending(l => l.Timestamp)
            .ToListAsync();
    }

    private static string? GetClientIp(HttpContext? ctx)
    {
        if (ctx == null) return null;
        var forwarded = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwarded))
            return forwarded.Split(',')[0].Trim();
        return ctx.Connection.RemoteIpAddress?.ToString();
    }
}
