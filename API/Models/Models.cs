using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataVault.API.Models;

// ─────────────────────────────────────────────
//  USER
// ─────────────────────────────────────────────
public class User
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    // Each user gets their own AES-256 master key (stored encrypted)
    public string EncryptedMasterKey { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<VaultFile> Files { get; set; } = new List<VaultFile>();
    public ICollection<ShareLink> SharedLinks { get; set; } = new List<ShareLink>();
    public ICollection<AccessLog> AccessLogs { get; set; } = new List<AccessLog>();
}

// ─────────────────────────────────────────────
//  VAULT FILE
// ─────────────────────────────────────────────
public class VaultFile
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(512)]
    public string OriginalFileName { get; set; } = string.Empty;

    [Required, MaxLength(512)]
    public string StoredFileName { get; set; } = string.Empty;   // GUID-based, on disk

    [Required, MaxLength(200)]
    public string ContentType { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    // Cryptography
    public string EncryptedFileKey { get; set; } = string.Empty; // AES key, encrypted with master key
    public string IV { get; set; } = string.Empty;               // Base64 IV for this file
    public string FileHash { get; set; } = string.Empty;         // SHA-256 of plaintext

    public int CurrentVersion { get; set; } = 1;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastModifiedAt { get; set; }
    public bool IsDeleted { get; set; } = false;

    [ForeignKey(nameof(Owner))]
    public Guid OwnerId { get; set; }
    public User Owner { get; set; } = null!;

    public ICollection<FileVersion> Versions { get; set; } = new List<FileVersion>();
    public ICollection<ShareLink> ShareLinks { get; set; } = new List<ShareLink>();
    public ICollection<AccessLog> AccessLogs { get; set; } = new List<AccessLog>();
    public ICollection<FilePermission> Permissions { get; set; } = new List<FilePermission>();
}

// ─────────────────────────────────────────────
//  FILE VERSION
// ─────────────────────────────────────────────
public class FileVersion
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [ForeignKey(nameof(File))]
    public Guid FileId { get; set; }
    public VaultFile File { get; set; } = null!;

    public int VersionNumber { get; set; }
    public string StoredFileName { get; set; } = string.Empty;
    public string EncryptedFileKey { get; set; } = string.Empty;
    public string IV { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? ChangeNote { get; set; }
}

// ─────────────────────────────────────────────
//  SHARE LINK
// ─────────────────────────────────────────────
public enum SharePermission { View, Download, Comment }

public class ShareLink
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string Token { get; set; } = string.Empty;            // Secure random token

    [ForeignKey(nameof(File))]
    public Guid FileId { get; set; }
    public VaultFile File { get; set; } = null!;

    [ForeignKey(nameof(CreatedBy))]
    public Guid CreatedById { get; set; }
    public User CreatedBy { get; set; } = null!;

    public string? RecipientEmail { get; set; }
    public SharePermission Permission { get; set; } = SharePermission.View;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public int MaxUses { get; set; } = 10;
    public int UseCount { get; set; } = 0;
    public bool IsRevoked { get; set; } = false;

    public bool IsActive => !IsRevoked && DateTime.UtcNow < ExpiresAt && UseCount < MaxUses;

    public ICollection<AccessLog> AccessLogs { get; set; } = new List<AccessLog>();
}

// ─────────────────────────────────────────────
//  FILE PERMISSION (direct user access)
// ─────────────────────────────────────────────
public class FilePermission
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [ForeignKey(nameof(File))]
    public Guid FileId { get; set; }
    public VaultFile File { get; set; } = null!;

    [Required, MaxLength(256)]
    public string GrantedToEmail { get; set; } = string.Empty;

    public SharePermission Permission { get; set; } = SharePermission.View;
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
    public bool IsActive { get; set; } = true;
}

// ─────────────────────────────────────────────
//  ACCESS LOG
// ─────────────────────────────────────────────
public enum AccessAction
{
    Viewed, Downloaded, ShareLinkCreated, ShareLinkRevoked,
    ShareLinkExpired, AccessDenied, PermissionGranted, PermissionRevoked,
    FileUploaded, FileDeleted, VersionRestored
}

public class AccessLog
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [ForeignKey(nameof(File))]
    public Guid FileId { get; set; }
    public VaultFile File { get; set; } = null!;

    public Guid? UserId { get; set; }
    public User? User { get; set; }

    public Guid? ShareLinkId { get; set; }
    public ShareLink? ShareLink { get; set; }

    public AccessAction Action { get; set; }

    [MaxLength(512)]
    public string ActorIdentifier { get; set; } = string.Empty;  // email or "system" or "anonymous"

    [MaxLength(50)]
    public string? IpAddress { get; set; }

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    public bool WasGranted { get; set; } = true;
    public string? Details { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
