using DataVault.API.Data;
using DataVault.API.Models;
using DataVault.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using static System.Net.WebRequestMethods;

namespace DataVault.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SharesController : ControllerBase
{
    private readonly VaultDbContext _db;
    private readonly IEncryptionService _crypto;
    private readonly IFileStorageService _storage;
    private readonly IAccessLogService _auditLog;
    private readonly IConfiguration _config;

    public SharesController(VaultDbContext db, IEncryptionService crypto,
        IFileStorageService storage, IAccessLogService auditLog, IConfiguration config)
    {
        _db = db; _crypto = crypto; _storage = storage; _auditLog = auditLog; _config = config;
    }

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private string BuildShareUrl(string token) =>
        $"{_config["Vault:BaseUrl"]}/api/shares/access/{token}";

    // ── Create Share Link ─────────────────────────────────────────────────

    /// <summary>Create a time-limited, use-limited share link for a file</summary>
    [HttpPost]
    public async Task<ActionResult<ShareLinkDto>> CreateShareLink([FromBody] CreateShareLinkRequest req)
    {
        var userId = CurrentUserId;
        var file = await _db.Files.FirstOrDefaultAsync(
            f => f.Id == req.FileId && f.OwnerId == userId && !f.IsDeleted);
        if (file == null) return NotFound(new { message = "File not found or not owned by you." });

        var token = _crypto.GenerateSecureToken(32);
        var expiry = DateTime.UtcNow.AddDays(req.ExpiryDays);

        var link = new ShareLink
        {
            Token = token,
            FileId = req.FileId,
            CreatedById = userId,
            RecipientEmail = req.RecipientEmail?.ToLower(),
            Permission = req.Permission,
            ExpiresAt = expiry,
            MaxUses = req.MaxUses
        };

        _db.ShareLinks.Add(link);
        await _db.SaveChangesAsync();

        await _auditLog.LogAsync(req.FileId, AccessAction.ShareLinkCreated,
            User.FindFirstValue(ClaimTypes.Email)!, true, userId,
            details: $"Permission={req.Permission}, Expires={expiry:yyyy-MM-dd}, MaxUses={req.MaxUses}");

        return Ok(ToDto(link, file.OriginalFileName));
    }

    // ── List Share Links ──────────────────────────────────────────────────

    /// <summary>List all share links created by the current user</summary>
    [HttpGet]
    public async Task<ActionResult<List<ShareLinkDto>>> GetMyShareLinks()
    {
        var userId = CurrentUserId;
        var links = await _db.ShareLinks
            .Include(s => s.File)
            .Where(s => s.CreatedById == userId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        return Ok(links.Select(s => ToDto(s, s.File.OriginalFileName)).ToList());
    }

    // ── Revoke Share Link ─────────────────────────────────────────────────

    /// <summary>Immediately revoke a share link</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Revoke(Guid id)
    {
        var userId = CurrentUserId;
        var link = await _db.ShareLinks.Include(s => s.File)
            .FirstOrDefaultAsync(s => s.Id == id && s.CreatedById == userId);
        if (link == null) return NotFound();

        link.IsRevoked = true;
        await _db.SaveChangesAsync();

        await _auditLog.LogAsync(link.FileId, AccessAction.ShareLinkRevoked,
            User.FindFirstValue(ClaimTypes.Email)!, true, userId);

        return NoContent();
    }

    // ── Access via Share Token (anonymous) ───────────────────────────────

    /// <summary>Validate a share token and stream the decrypted file</summary>
    [HttpGet("access/{token}")]
    [AllowAnonymous]
    public async Task<IActionResult> AccessByToken(string token)
    {
        var link = await _db.ShareLinks
            .Include(s => s.File).ThenInclude(f => f.Owner)
            .FirstOrDefaultAsync(s => s.Token == token);

        if (link == null)
        {
            await LogDenied(Guid.Empty, "Anonymous", "Invalid token");
            return NotFound(new { message = "Share link not found." });
        }

        if (!link.IsActive)
        {
            var reason = link.IsRevoked ? "Link has been revoked."
                : link.UseCount >= link.MaxUses ? "Link has reached maximum uses."
                : "Link has expired.";

            await _auditLog.LogAsync(link.FileId, AccessAction.AccessDenied,
                "Anonymous", false, details: reason);
            return Forbid();
        }

        // Increment use count
        link.UseCount++;
        await _db.SaveChangesAsync();

        var file = link.File;
        var owner = file.Owner;

        // For View permission, stream inline; for Download, force download
        if (link.Permission == SharePermission.View)
        {
            await _auditLog.LogAsync(file.Id, AccessAction.Viewed,
                "ShareLink:" + token[..8], true, shareLinkId: link.Id);

            // Return file metadata only for view; actual rendering handled client-side
            return Ok(new
            {
                fileName = file.OriginalFileName,
                contentType = file.ContentType,
                fileSizeBytes = file.FileSizeBytes,
                permission = link.Permission.ToString(),
                expiresAt = link.ExpiresAt,
                usesRemaining = link.MaxUses - link.UseCount
            });
        }

        // Download: decrypt and stream
        // NOTE: In production, the server's encryption key would be used here.
        // We use a service account key approach for share links.
        await _auditLog.LogAsync(file.Id, AccessAction.Downloaded,
            "ShareLink:" + token[..8], true, shareLinkId: link.Id);

        return Ok(new
        {
            fileName = file.OriginalFileName,
            contentType = file.ContentType,
            message = "Download authorized. Client should request /api/shares/download/{token}"
        });
    }

    // ── Grant direct permission ───────────────────────────────────────────

    /// <summary>Grant a named user direct access to a file</summary>
    [HttpPost("permissions")]
    public async Task<ActionResult<PermissionDto>> GrantPermission([FromBody] GrantPermissionRequest req)
    {
        var userId = CurrentUserId;
        var file = await _db.Files.FirstOrDefaultAsync(
            f => f.Id == req.FileId && f.OwnerId == userId && !f.IsDeleted);
        if (file == null) return NotFound();

        // Revoke any existing permission for same email+file
        var existing = await _db.FilePermissions
            .Where(p => p.FileId == req.FileId && p.GrantedToEmail == req.GrantedToEmail.ToLower())
            .ToListAsync();
        foreach (var p in existing) { p.IsActive = false; p.RevokedAt = DateTime.UtcNow; }

        var permission = new FilePermission
        {
            FileId = req.FileId,
            GrantedToEmail = req.GrantedToEmail.ToLower(),
            Permission = req.Permission
        };
        _db.FilePermissions.Add(permission);
        await _db.SaveChangesAsync();

        await _auditLog.LogAsync(req.FileId, AccessAction.PermissionGranted,
            User.FindFirstValue(ClaimTypes.Email)!, true, userId,
            details: $"Granted {req.Permission} to {req.GrantedToEmail}");

        return Ok(new PermissionDto(permission.Id, file.OriginalFileName,
            permission.GrantedToEmail, permission.Permission, permission.GrantedAt, permission.IsActive));
    }

    /// <summary>List permissions for all files owned by the user</summary>
    [HttpGet("permissions")]
    public async Task<ActionResult<List<PermissionDto>>> GetPermissions()
    {
        var userId = CurrentUserId;
        var permissions = await _db.FilePermissions
            .Include(p => p.File)
            .Where(p => p.File.OwnerId == userId && p.IsActive)
            .Select(p => new PermissionDto(
                p.Id, p.File.OriginalFileName, p.GrantedToEmail,
                p.Permission, p.GrantedAt, p.IsActive))
            .ToListAsync();
        return Ok(permissions);
    }

    /// <summary>Revoke a direct permission</summary>
    [HttpDelete("permissions/{id}")]
    public async Task<IActionResult> RevokePermission(Guid id)
    {
        var userId = CurrentUserId;
        var perm = await _db.FilePermissions.Include(p => p.File)
            .FirstOrDefaultAsync(p => p.Id == id && p.File.OwnerId == userId);
        if (perm == null) return NotFound();

        perm.IsActive = false;
        perm.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _auditLog.LogAsync(perm.FileId, AccessAction.PermissionRevoked,
            User.FindFirstValue(ClaimTypes.Email)!, true, userId);

        return NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────
    private ShareLinkDto ToDto(ShareLink s, string fileName) => new(
        s.Id, s.Token, fileName, s.RecipientEmail, s.Permission,
        s.CreatedAt, s.ExpiresAt, s.MaxUses, s.UseCount, s.IsActive, BuildShareUrl(s.Token));

    private async Task LogDenied(Guid fileId, string actor, string details) =>
        await _auditLog.LogAsync(fileId, AccessAction.AccessDenied, actor, false, details: details);

    [HttpGet("/s/{token}")]
    public async Task<IActionResult> PublicAccess(string token)
    {
        // Call API to validate the token
        var resp = await _http.GetAsync($"api/shares/access/{token}");
        if (!resp.IsSuccessStatusCode)
            return View("ShareInvalid");

        var info = await resp.Content.ReadFromJsonAsync<PublicShareInfo>();
        return View("PublicShare", info);
    }

    [HttpGet("/s/{token}/download")]
    public async Task<IActionResult> PublicDownload(string token)
    {
        var resp = await _http.GetAsync($"api/shares/download/{token}");
        if (!resp.IsSuccessStatusCode) return NotFound();

        var ct = resp.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var fn = resp.Content.Headers.ContentDisposition?.FileNameStar
                 ?? resp.Content.Headers.ContentDisposition?.FileName
                 ?? "download";
        return File(await resp.Content.ReadAsStreamAsync(), ct, fn.Trim('"'));
    }
}


