using System.Security.Claims;
using DataVault.MVC.Data;
using DataVault.MVC.Models;
using DataVault.MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DataVault.MVC.Controllers;

[Authorize]
public class SharesController : Controller
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

    private string CurrentEmail =>
        User.FindFirstValue(ClaimTypes.Email)!;

    private string BuildShareUrl(string token) =>
        $"{_config["Vault:BaseUrl"]}/s/{token}";

    // ── Index ─────────────────────────────────────────────────────────────

    [HttpGet("/shares")]
    public async Task<IActionResult> Index()
    {
        var userId = CurrentUserId;

        var links = await _db.ShareLinks
            .Include(s => s.File)
            .Where(s => s.CreatedById == userId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new ShareLinkDto(
                s.Id, s.Token, s.File.OriginalFileName, s.IntendedFor,
                s.Permission, s.CreatedAt, s.ExpiresAt,
                s.MaxUses, s.UseCount, s.IsActive,
                $"{_config["Vault:BaseUrl"]}/s/{s.Token}"))
            .ToListAsync();

        var perms = await _db.FilePermissions
            .Include(p => p.File)
            .Where(p => p.File.OwnerId == userId && p.IsActive)
            .Select(p => new PermissionDto(
                p.Id, p.File.OriginalFileName, p.GrantedToEmail,
                p.Permission, p.GrantedAt, p.IsActive))
            .ToListAsync();

        return View(new SharesViewModel { ShareLinks = links, Permissions = perms });
    }

    // ── Create Share Link ─────────────────────────────────────────────────

    [HttpPost("/shares/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateShareForm form)
    {
        var userId = CurrentUserId;
        var file = await _db.Files.FirstOrDefaultAsync(
            f => f.Id == form.FileId && f.OwnerId == userId && !f.IsDeleted);

        if (file == null)
        {
            TempData["Error"] = "File not found or not owned by you.";
            return RedirectToAction("Index");
        }

        var token = _crypto.GenerateSecureToken(32);
        var expiry = DateTime.UtcNow.AddDays(form.ExpiryDays);

        var link = new ShareLink
        {
            Token = token,
            FileId = form.FileId,
            CreatedById = userId,
            IntendedFor = form.IntendedFor?.ToLower(),
            Permission = form.Permission,
            ExpiresAt = expiry,
            MaxUses = form.MaxUses
        };

        _db.ShareLinks.Add(link);
        await _db.SaveChangesAsync();

        await _auditLog.LogAsync(form.FileId, AccessAction.ShareLinkCreated,
            CurrentEmail, true, userId,
            details: $"Permission={form.Permission}, Expires={expiry:yyyy-MM-dd}, MaxUses={form.MaxUses}");

        TempData["ShareUrl"] = BuildShareUrl(token);
        return RedirectToAction("Index");
    }

    // ── Revoke Share Link ─────────────────────────────────────────────────

    [HttpPost("/shares/{id}/revoke")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(Guid id)
    {
        var userId = CurrentUserId;
        var link = await _db.ShareLinks
            .FirstOrDefaultAsync(s => s.Id == id && s.CreatedById == userId);

        if (link == null) return NotFound();

        link.IsRevoked = true;
        await _db.SaveChangesAsync();

        await _auditLog.LogAsync(link.FileId, AccessAction.ShareLinkRevoked,
            CurrentEmail, true, userId);

        TempData["Success"] = "Share link revoked.";
        return RedirectToAction("Index");
    }

    // ── Public Share Access (anonymous) ──────────────────────────────────

    [HttpGet("/s/{token}")]
    [AllowAnonymous]
    public async Task<IActionResult> PublicAccess(string token)
    {
        var link = await _db.ShareLinks
            .Include(s => s.File)
            .FirstOrDefaultAsync(s => s.Token == token);

        if (link == null)
            return View("ShareInvalid");

        if (!link.IsActive)
        {
            var reason = link.IsRevoked ? "This link has been revoked."
                : link.UseCount >= link.MaxUses ? "This link has reached its maximum number of uses."
                : "This link has expired.";

            await _auditLog.LogAsync(link.FileId, AccessAction.AccessDenied,
                "Anonymous", false, details: reason);

            ViewBag.Reason = reason;
            return View("ShareInvalid");
        }

        link.UseCount++;
        await _db.SaveChangesAsync();

        await _auditLog.LogAsync(link.File.Id, AccessAction.Viewed,
            "ShareLink:" + token[..8], true, shareLinkId: link.Id);

        ViewBag.FileName = link.File.OriginalFileName;
        ViewBag.ContentType = link.File.ContentType;
        ViewBag.FileSize = link.File.FileSizeBytes;
        ViewBag.Permission = link.Permission.ToString();
        ViewBag.ExpiresAt = link.ExpiresAt;
        ViewBag.UsesLeft = link.MaxUses - link.UseCount;
        ViewBag.Token = token;

        return View("PublicShare");
    }

    // ── Public Share Download (anonymous) ────────────────────────────────

    [HttpGet("/s/{token}/download")]
    [AllowAnonymous]
    public async Task<IActionResult> PublicDownload(string token)
    {
        var link = await _db.ShareLinks
            .Include(s => s.File).ThenInclude(f => f.Owner)
            .FirstOrDefaultAsync(s => s.Token == token);

        if (link == null) return NotFound();

        if (!link.IsActive)
        {
            await _auditLog.LogAsync(link.FileId, AccessAction.AccessDenied,
                "Anonymous", false, details: "Link inactive on download attempt");
            return Forbid();
        }

        if (link.Permission != SharePermission.Download)
        {
            await _auditLog.LogAsync(link.FileId, AccessAction.AccessDenied,
                "Anonymous", false, details: "Download not permitted by link permission");
            return Forbid();
        }

        var file = link.File;
        var owner = file.Owner;

        // We need the owner's master key to decrypt — for share links we use
        // the owner's stored encrypted master key with the server secret only.
        // This works because EncryptMasterKey derives the KEK from password+serverSecret,
        // so we pass an empty string as password and rely purely on the server secret.
        // NOTE: This means share downloads only work when ServerSecret is set in config.
        // The master key is derived from the owner's password + server secret.
        // Since we don't have the owner's password for anonymous access,
        // we cannot decrypt on their behalf. The owner must be logged in to serve downloads.
        // Check if the owner happens to be the current logged-in user:
        var ownerIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var ownerPassword = User.FindFirst("vault_password")?.Value;

        if (ownerIdClaim == null || !Guid.TryParse(ownerIdClaim, out var loggedInId)
            || loggedInId != owner.Id || string.IsNullOrEmpty(ownerPassword))
        {
            // Owner not logged in — cannot decrypt. Show a friendly page.
            ViewBag.FileName = file.OriginalFileName;
            ViewBag.Token = token;
            return View("ShareDownloadLogin");
        }

        string masterKey;
        try { masterKey = _crypto.DecryptMasterKey(owner.EncryptedMasterKey, ownerPassword); }
        catch { return StatusCode(500, "Vault decryption failed."); }

        string fileKey;
        try { fileKey = _crypto.DecryptFileKey(file.EncryptedFileKey, masterKey); }
        catch { return StatusCode(500, "File key decryption failed."); }

        var encStream = await _storage.OpenEncryptedFileAsync(file.StoredFileName);
        var plainStream = new MemoryStream();
        await _crypto.DecryptFileToStreamAsync(encStream, plainStream, fileKey, file.IV);
        plainStream.Position = 0;

        await _auditLog.LogAsync(file.Id, AccessAction.Downloaded,
            "ShareLink:" + token[..8], true, shareLinkId: link.Id);

        return File(plainStream, file.ContentType, file.OriginalFileName, enableRangeProcessing: false);
    }

    // ── Grant Permission ──────────────────────────────────────────────────

    [HttpPost("/shares/permissions/grant")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GrantPermission(GrantPermissionForm form)
    {
        var userId = CurrentUserId;
        var file = await _db.Files.FirstOrDefaultAsync(
            f => f.Id == form.FileId && f.OwnerId == userId && !f.IsDeleted);

        if (file == null)
        {
            TempData["Error"] = "File not found.";
            return RedirectToAction("Index");
        }

        // Revoke any existing permission for same email + file
        var existing = await _db.FilePermissions
            .Where(p => p.FileId == form.FileId && p.GrantedToEmail == form.GrantedToEmail.ToLower())
            .ToListAsync();
        foreach (var p in existing) { p.IsActive = false; p.RevokedAt = DateTime.UtcNow; }

        _db.FilePermissions.Add(new FilePermission
        {
            FileId = form.FileId,
            GrantedToEmail = form.GrantedToEmail.ToLower(),
            Permission = form.Permission
        });

        await _db.SaveChangesAsync();

        await _auditLog.LogAsync(form.FileId, AccessAction.PermissionGranted,
            CurrentEmail, true, userId,
            details: $"Granted {form.Permission} to {form.GrantedToEmail}");

        TempData["Success"] = "Permission granted.";
        return RedirectToAction("Index");
    }

    // ── Revoke Permission ─────────────────────────────────────────────────

    [HttpPost("/shares/permissions/{id}/revoke")]
    [ValidateAntiForgeryToken]
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
            CurrentEmail, true, userId);

        TempData["Success"] = "Permission revoked.";
        return RedirectToAction("Index");
    }
}