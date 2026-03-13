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
public class FilesController : ControllerBase
{
    private readonly VaultDbContext _db;
    private readonly IEncryptionService _crypto;
    private readonly IFileStorageService _storage;
    private readonly IAccessLogService _auditLog;
    private readonly ILogger<FilesController> _logger;

    public FilesController(VaultDbContext db, IEncryptionService crypto,
        IFileStorageService storage, IAccessLogService auditLog,
        ILogger<FilesController> logger)
    {
        _db = db; _crypto = crypto; _storage = storage;
        _auditLog = auditLog; _logger = logger;
    }

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ── Upload ────────────────────────────────────────────────────────────

    /// <summary>Upload and encrypt a file. Supports versioning if file name already exists.</summary>
    [HttpPost("upload")]
    [RequestSizeLimit(100 * 1024 * 1024)] // 100 MB
    public async Task<ActionResult<FileUploadResponse>> Upload(
        IFormFile file, [FromForm] string? changeNote)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file provided." });

        var userId = CurrentUserId;
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return Unauthorized();

        // We need the password to decrypt the master key.
        // In production, use a session-derived key stored in a secure cache.
        // For this implementation, the client passes it via a secure header.
        var password = Request.Headers["X-Vault-Password"].ToString();
        if (string.IsNullOrEmpty(password))
            return BadRequest(new { message = "Vault password required in X-Vault-Password header." });

        string masterKey;
        try { masterKey = _crypto.DecryptMasterKey(user.EncryptedMasterKey, password); }
        catch { return Unauthorized(new { message = "Invalid vault password." }); }

        // Compute plaintext hash for integrity
        using var plainStream = file.OpenReadStream();
        var fileHash = await _crypto.ComputeHashAsync(plainStream);
        plainStream.Position = 0;

        // Generate unique file key & IV
        var (fileKey, iv) = _crypto.GenerateFileKey();
        var encryptedFileKey = _crypto.EncryptFileKey(fileKey, masterKey);

        // Encrypt to memory stream then save
        using var encryptedMs = new MemoryStream();
        await _crypto.EncryptFileToStreamAsync(plainStream, encryptedMs, fileKey, iv);
        encryptedMs.Position = 0;
        var storedName = await _storage.SaveEncryptedFileAsync(encryptedMs, Path.GetExtension(file.FileName));

        // Check if this is a new version of existing file
        var existingFile = await _db.Files
            .Where(f => f.OwnerId == userId && f.OriginalFileName == file.FileName && !f.IsDeleted)
            .FirstOrDefaultAsync();

        VaultFile vaultFile;
        int versionNumber;

        if (existingFile != null)
        {
            // Save snapshot of current version
            var snapshot = new FileVersion
            {
                FileId = existingFile.Id,
                VersionNumber = existingFile.CurrentVersion,
                StoredFileName = existingFile.StoredFileName,
                EncryptedFileKey = existingFile.EncryptedFileKey,
                IV = existingFile.IV,
                FileHash = existingFile.FileHash,
                FileSizeBytes = existingFile.FileSizeBytes,
                ChangeNote = changeNote ?? "Auto-snapshot before update"
            };
            _db.FileVersions.Add(snapshot);

            // Update main record
            existingFile.StoredFileName = storedName;
            existingFile.EncryptedFileKey = encryptedFileKey;
            existingFile.IV = iv;
            existingFile.FileHash = fileHash;
            existingFile.FileSizeBytes = file.Length;
            existingFile.CurrentVersion++;
            existingFile.LastModifiedAt = DateTime.UtcNow;
            vaultFile = existingFile;
            versionNumber = existingFile.CurrentVersion;
        }
        else
        {
            vaultFile = new VaultFile
            {
                OriginalFileName = file.FileName,
                StoredFileName = storedName,
                ContentType = file.ContentType,
                FileSizeBytes = file.Length,
                EncryptedFileKey = encryptedFileKey,
                IV = iv,
                FileHash = fileHash,
                OwnerId = userId,
                CurrentVersion = 1
            };
            _db.Files.Add(vaultFile);
            versionNumber = 1;
        }

        await _db.SaveChangesAsync();

        await _auditLog.LogAsync(vaultFile.Id, AccessAction.FileUploaded,
            User.FindFirstValue(ClaimTypes.Email)!, true, userId,
            details: $"v{versionNumber}, {file.Length} bytes");

        return Ok(new FileUploadResponse(
            vaultFile.Id, vaultFile.OriginalFileName, vaultFile.ContentType,
            vaultFile.FileSizeBytes, fileHash, versionNumber, vaultFile.UploadedAt));
    }

    // ── List ──────────────────────────────────────────────────────────────

    /// <summary>List all files owned by the authenticated user</summary>
    [HttpGet]
    public async Task<ActionResult<List<FileDto>>> GetMyFiles()
    {
        var userId = CurrentUserId;
        var files = await _db.Files
            .Where(f => f.OwnerId == userId && !f.IsDeleted)
            .Select(f => new FileDto(
                f.Id, f.OriginalFileName, f.ContentType, f.FileSizeBytes,
                f.FileHash, f.CurrentVersion, f.UploadedAt, f.LastModifiedAt,
                f.ShareLinks.Count(s => s.IsRevoked == false),
                f.AccessLogs.Count(l => l.Action == AccessAction.Viewed || l.Action == AccessAction.Downloaded)))
            .ToListAsync();

        return Ok(files);
    }

    // ── Download ──────────────────────────────────────────────────────────

    /// <summary>Download and decrypt a file (owner only)</summary>
    [HttpGet("{id}/download")]
    public async Task<IActionResult> Download(Guid id)
    {
        var userId = CurrentUserId;
        var file = await _db.Files.Include(f => f.Owner)
            .FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted);

        if (file == null) return NotFound();
        if (file.OwnerId != userId) return Forbid();

        var password = Request.Headers["X-Vault-Password"].ToString();
        if (string.IsNullOrEmpty(password))
            return BadRequest(new { message = "Vault password required." });

        string masterKey;
        try { masterKey = _crypto.DecryptMasterKey(file.Owner.EncryptedMasterKey, password); }
        catch { return Unauthorized(new { message = "Invalid vault password." }); }

        var fileKey = _crypto.DecryptFileKey(file.EncryptedFileKey, masterKey);
        var encStream = await _storage.OpenEncryptedFileAsync(file.StoredFileName);

        var plainStream = new MemoryStream();
        await _crypto.DecryptFileToStreamAsync(encStream, plainStream, fileKey, file.IV);
        plainStream.Position = 0;

        await _auditLog.LogAsync(id, AccessAction.Downloaded,
            User.FindFirstValue(ClaimTypes.Email)!, true, userId);

        return File(plainStream, file.ContentType,
            file.OriginalFileName, enableRangeProcessing: false);
    }

    // ── Delete ────────────────────────────────────────────────────────────

    /// <summary>Soft-delete a file</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = CurrentUserId;
        var file = await _db.Files.FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted);
        if (file == null) return NotFound();
        if (file.OwnerId != userId) return Forbid();

        file.IsDeleted = true;
        await _db.SaveChangesAsync();

        await _auditLog.LogAsync(id, AccessAction.FileDeleted,
            User.FindFirstValue(ClaimTypes.Email)!, true, userId);

        return NoContent();
    }

    // ── Version History ────────────────────────────────────────────────────

    /// <summary>Get version history for a file</summary>
    [HttpGet("{id}/versions")]
    public async Task<ActionResult<List<FileVersionDto>>> GetVersions(Guid id)
    {
        var userId = CurrentUserId;
        var file = await _db.Files.FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted);
        if (file == null) return NotFound();
        if (file.OwnerId != userId) return Forbid();

        var versions = await _db.FileVersions
            .Where(v => v.FileId == id)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new FileVersionDto(
                v.Id, v.VersionNumber, v.FileSizeBytes,
                v.FileHash, v.CreatedAt, v.ChangeNote))
            .ToListAsync();

        return Ok(versions);
    }

    /// <summary>Restore a specific version</summary>
    [HttpPost("{id}/versions/{versionId}/restore")]
    public async Task<IActionResult> RestoreVersion(Guid id, Guid versionId)
    {
        var userId = CurrentUserId;
        var file = await _db.Files.Include(f => f.Owner)
            .FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted);
        if (file == null) return NotFound();
        if (file.OwnerId != userId) return Forbid();

        var version = await _db.FileVersions
            .FirstOrDefaultAsync(v => v.Id == versionId && v.FileId == id);
        if (version == null) return NotFound();

        // Snapshot current as a version before restoring
        var snapshot = new FileVersion
        {
            FileId = file.Id,
            VersionNumber = file.CurrentVersion,
            StoredFileName = file.StoredFileName,
            EncryptedFileKey = file.EncryptedFileKey,
            IV = file.IV,
            FileHash = file.FileHash,
            FileSizeBytes = file.FileSizeBytes,
            ChangeNote = "Auto-snapshot before restore"
        };
        _db.FileVersions.Add(snapshot);

        // Restore
        file.StoredFileName = version.StoredFileName;
        file.EncryptedFileKey = version.EncryptedFileKey;
        file.IV = version.IV;
        file.FileHash = version.FileHash;
        file.FileSizeBytes = version.FileSizeBytes;
        file.CurrentVersion++;
        file.LastModifiedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        await _auditLog.LogAsync(id, AccessAction.VersionRestored,
            User.FindFirstValue(ClaimTypes.Email)!, true, userId,
            details: $"Restored to v{version.VersionNumber}");

        return Ok(new { message = $"Restored to version {version.VersionNumber}" });
    }

    // ── Dashboard Stats ────────────────────────────────────────────────────

    /// <summary>Get dashboard statistics for the current user</summary>
    [HttpGet("stats")]
    public async Task<ActionResult<DashboardStats>> GetStats()
    {
        var userId = CurrentUserId;

        var files = await _db.Files.Where(f => f.OwnerId == userId && !f.IsDeleted).ToListAsync();
        var activeShares = await _db.ShareLinks
            .CountAsync(s => s.CreatedById == userId && !s.IsRevoked && s.ExpiresAt > DateTime.UtcNow);
        var allLogs = await _db.AccessLogs
            .Where(l => l.File.OwnerId == userId).ToListAsync();

        return Ok(new DashboardStats(
            TotalFiles: files.Count,
            TotalStorageBytes: files.Sum(f => f.FileSizeBytes),
            ActiveShareLinks: activeShares,
            TotalAccessEvents: allLogs.Count,
            DeniedAccessAttempts: allLogs.Count(l => !l.WasGranted),
            FilesSharedWithOthers: await _db.FilePermissions
                .CountAsync(p => p.File.OwnerId == userId && p.IsActive)
        ));
    }
}
