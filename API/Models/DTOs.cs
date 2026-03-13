namespace DataVault.API.Models;

// ── AUTH ──────────────────────────────────────
public record RegisterRequest(string Username, string Email, string Password);
public record LoginRequest(string Email, string Password);
public record AuthResponse(string Token, string Username, string Email, DateTime ExpiresAt);

// ── FILES ─────────────────────────────────────
public record FileUploadResponse(
    Guid Id, string OriginalFileName, string ContentType,
    long FileSizeBytes, string FileHash, int VersionNumber, DateTime UploadedAt);

public record FileDto(
    Guid Id, string OriginalFileName, string ContentType,
    long FileSizeBytes, string FileHash, int CurrentVersion,
    DateTime UploadedAt, DateTime? LastModifiedAt, int ShareCount, int ViewCount);

public record FileVersionDto(
    Guid Id, int VersionNumber, long FileSizeBytes,
    string FileHash, DateTime CreatedAt, string? ChangeNote);

// ── SHARES ────────────────────────────────────
public record CreateShareLinkRequest(
    Guid FileId, string? RecipientEmail, SharePermission Permission,
    int ExpiryDays, int MaxUses);

public record ShareLinkDto(
    Guid Id, string Token, string FileName, string? RecipientEmail,
    SharePermission Permission, DateTime CreatedAt, DateTime ExpiresAt,
    int MaxUses, int UseCount, bool IsActive, string ShareUrl);

// ── PERMISSIONS ───────────────────────────────
public record GrantPermissionRequest(Guid FileId, string GrantedToEmail, SharePermission Permission);

public record PermissionDto(
    Guid Id, string FileName, string GrantedToEmail,
    SharePermission Permission, DateTime GrantedAt, bool IsActive);

// ── ACCESS LOGS ───────────────────────────────
public record AccessLogDto(
    Guid Id, string FileName, AccessAction Action, string ActorIdentifier,
    string? IpAddress, bool WasGranted, DateTime Timestamp, string? Details);

// ── DASHBOARD ─────────────────────────────────
public record DashboardStats(
    int TotalFiles, long TotalStorageBytes, int ActiveShareLinks,
    int TotalAccessEvents, int DeniedAccessAttempts, int FilesSharedWithOthers);
