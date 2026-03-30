using System.Security.Claims;
using DataVault.MVC.Data;
using DataVault.MVC.Models;
using DataVault.MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DataVault.MVC.Controllers;

[Authorize]
public class FilesController : Controller
{
    private readonly VaultDbContext _db;
    private readonly IEncryptionService _crypto;
    private readonly IFileStorageService _storage;
    private readonly IAccessLogService _auditLog;

    public FilesController(VaultDbContext db, IEncryptionService crypto,
        IFileStorageService storage, IAccessLogService auditLog)
    {
        _db = db; _crypto = crypto; _storage = storage; _auditLog = auditLog;
    }

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private string CurrentEmail =>
        User.FindFirstValue(ClaimTypes.Email)!;

    private string? VaultPassword =>
        User.FindFirstValue("vault_password");

    // ── Index ─────────────────────────────────────────────────────────────

    [HttpGet("/files")]
    public async Task<IActionResult> Index()
    {
        var files = await GetFileDtosAsync(CurrentUserId);
        return View(new FilesViewModel { Files = files });
    }

    // ── Upload ────────────────────────────────────────────────────────────

    [HttpPost("/files/upload")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(100 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file, string? changeNote)
    {
        if (file == null || file.Length == 0)
        {
            TempData["Error"] = "Please select a file.";
            return RedirectToAction("Index");
        }

        var password = VaultPassword;
        if (string.IsNullOrEmpty(password))
        {
            TempData["Error"] = "Session expired — please log in again.";
            return RedirectToAction("Login", "Auth");
        }

        var userId = CurrentUserId;
        var user   = await _db.Users.FindAsync(userId);
        if (user == null) return Unauthorized();

        string masterKey;
        try { masterKey = _crypto.DecryptMasterKey(user.EncryptedMasterKey, password); }
        catch
        {
            TempData["Error"] = "Failed to decrypt vault. Please log in again.";
            return RedirectToAction("Login", "Auth");
        }

        using var plainStream = file.OpenReadStream();
        var fileHash = await _crypto.ComputeHashAsync(plainStream);
        plainStream.Position = 0;

        var (fileKey, iv) = _crypto.GenerateFileKey();
        var encryptedFileKey = _crypto.EncryptFileKey(fileKey, masterKey);

        using var encryptedMs = new MemoryStream();
        await _crypto.EncryptFileToStreamAsync(plainStream, encryptedMs, fileKey, iv);
        encryptedMs.Position = 0;
        var storedName = await _storage.SaveEncryptedFileAsync(encryptedMs, Path.GetExtension(file.FileName));

        var existing = await _db.Files
            .Where(f => f.OwnerId == userId && f.OriginalFileName == file.FileName && !f.IsDeleted)
            .FirstOrDefaultAsync();

        VaultFile vaultFile;
        int versionNumber;

        if (existing != null)
        {
            _db.FileVersions.Add(new FileVersion
            {
                FileId           = existing.Id,
                VersionNumber    = existing.CurrentVersion,
                StoredFileName   = existing.StoredFileName,
                EncryptedFileKey = existing.EncryptedFileKey,
                IV               = existing.IV,
                FileHash         = existing.FileHash,
                FileSizeBytes    = existing.FileSizeBytes,
                ChangeNote       = changeNote ?? "Auto-snapshot before update"
            });

            existing.StoredFileName   = storedName;
            existing.EncryptedFileKey = encryptedFileKey;
            existing.IV               = iv;
            existing.FileHash         = fileHash;
            existing.FileSizeBytes    = file.Length;
            existing.CurrentVersion++;
            existing.LastModifiedAt   = DateTime.UtcNow;
            vaultFile     = existing;
            versionNumber = existing.CurrentVersion;
        }
        else
        {
            vaultFile = new VaultFile
            {
                OriginalFileName = file.FileName,
                StoredFileName   = storedName,
                ContentType      = file.ContentType,
                FileSizeBytes    = file.Length,
                EncryptedFileKey = encryptedFileKey,
                IV               = iv,
                FileHash         = fileHash,
                OwnerId          = userId,
                CurrentVersion   = 1
            };
            _db.Files.Add(vaultFile);
            versionNumber = 1;
        }

        await _db.SaveChangesAsync();

        await _auditLog.LogAsync(vaultFile.Id, AccessAction.FileUploaded,
            CurrentEmail, true, userId,
            details: $"v{versionNumber}, {file.Length} bytes");

        TempData["Success"] = $"'{file.FileName}' encrypted and stored.";
        return RedirectToAction("Index");
    }

    // ── Download ──────────────────────────────────────────────────────────

    [HttpGet("/files/{id}/download")]
    public async Task<IActionResult> Download(Guid id)
    {
        var userId = CurrentUserId;
        var file   = await _db.Files.Include(f => f.Owner)
            .FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted);

        if (file == null) return NotFound();
        if (file.OwnerId != userId) return Forbid();

        var password = VaultPassword;
        if (string.IsNullOrEmpty(password))
            return RedirectToAction("Login", "Auth");

        string masterKey;
        try { masterKey = _crypto.DecryptMasterKey(file.Owner.EncryptedMasterKey, password); }
        catch { return Forbid(); }

        var fileKey   = _crypto.DecryptFileKey(file.EncryptedFileKey, masterKey);
        var encStream = await _storage.OpenEncryptedFileAsync(file.StoredFileName);

        var plainStream = new MemoryStream();
        await _crypto.DecryptFileToStreamAsync(encStream, plainStream, fileKey, file.IV);
        plainStream.Position = 0;

        await _auditLog.LogAsync(id, AccessAction.Downloaded, CurrentEmail, true, userId);

        return File(plainStream, file.ContentType, file.OriginalFileName, enableRangeProcessing: false);
    }

    // ── Delete ────────────────────────────────────────────────────────────

    [HttpPost("/files/{id}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = CurrentUserId;
        var file   = await _db.Files.FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted);
        if (file == null) return NotFound();
        if (file.OwnerId != userId) return Forbid();

        file.IsDeleted = true;
        await _db.SaveChangesAsync();

        await _auditLog.LogAsync(id, AccessAction.FileDeleted, CurrentEmail, true, userId);

        TempData["Success"] = "File deleted.";
        return RedirectToAction("Index");
    }

    // ── Detail ────────────────────────────────────────────────────────────

    [HttpGet("/files/{id}/detail")]
    public async Task<IActionResult> Detail(Guid id)
    {
        var userId = CurrentUserId;
        var file   = await _db.Files
            .Where(f => f.Id == id && f.OwnerId == userId && !f.IsDeleted)
            .Select(f => new FileDto(
                f.Id, f.OriginalFileName, f.ContentType, f.FileSizeBytes,
                f.FileHash, f.CurrentVersion, f.UploadedAt, f.LastModifiedAt,
                f.ShareLinks.Count(s => !s.IsRevoked),
                f.AccessLogs.Count(l => l.Action == AccessAction.Viewed || l.Action == AccessAction.Downloaded)))
            .FirstOrDefaultAsync();

        if (file == null) return NotFound();

        var versions = await _db.FileVersions
            .Where(v => v.FileId == id)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new FileVersionDto(v.Id, v.VersionNumber, v.FileSizeBytes, v.FileHash, v.CreatedAt, v.ChangeNote))
            .ToListAsync();

        var logs = await _db.AccessLogs
            .Where(l => l.FileId == id)
            .OrderByDescending(l => l.Timestamp)
            .Select(l => new AccessLogDto(
                l.Id, file.OriginalFileName, l.Action,
                l.ActorIdentifier, l.IpAddress, l.WasGranted,
                l.Timestamp, l.Details))
            .ToListAsync();

        return View(new FileDetailViewModel { File = file, Versions = versions, Logs = logs });
    }

    // ── Restore Version ───────────────────────────────────────────────────

    [HttpPost("/files/{fileId}/versions/{versionId}/restore")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreVersion(Guid fileId, Guid versionId)
    {
        var userId = CurrentUserId;
        var file   = await _db.Files.FirstOrDefaultAsync(f => f.Id == fileId && !f.IsDeleted);
        if (file == null || file.OwnerId != userId) return NotFound();

        var version = await _db.FileVersions
            .FirstOrDefaultAsync(v => v.Id == versionId && v.FileId == fileId);
        if (version == null) return NotFound();

        _db.FileVersions.Add(new FileVersion
        {
            FileId           = file.Id,
            VersionNumber    = file.CurrentVersion,
            StoredFileName   = file.StoredFileName,
            EncryptedFileKey = file.EncryptedFileKey,
            IV               = file.IV,
            FileHash         = file.FileHash,
            FileSizeBytes    = file.FileSizeBytes,
            ChangeNote       = "Auto-snapshot before restore"
        });

        file.StoredFileName   = version.StoredFileName;
        file.EncryptedFileKey = version.EncryptedFileKey;
        file.IV               = version.IV;
        file.FileHash         = version.FileHash;
        file.FileSizeBytes    = version.FileSizeBytes;
        file.CurrentVersion++;
        file.LastModifiedAt   = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _auditLog.LogAsync(fileId, AccessAction.VersionRestored, CurrentEmail, true, userId,
            details: $"Restored to v{version.VersionNumber}");

        TempData["Success"] = "Version restored.";
        return RedirectToAction("Detail", new { id = fileId });
    }

    // ── Helper ────────────────────────────────────────────────────────────

    internal async Task<List<FileDto>> GetFileDtosAsync(Guid userId)
        => await _db.Files
            .Where(f => f.OwnerId == userId && !f.IsDeleted)
            .Select(f => new FileDto(
                f.Id, f.OriginalFileName, f.ContentType, f.FileSizeBytes,
                f.FileHash, f.CurrentVersion, f.UploadedAt, f.LastModifiedAt,
                f.ShareLinks.Count(s => !s.IsRevoked),
                f.AccessLogs.Count(l => l.Action == AccessAction.Viewed || l.Action == AccessAction.Downloaded)))
            .ToListAsync();
}
