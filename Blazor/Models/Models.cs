namespace DataVault.Blazor.Models;

public record AuthResponse(string Token, string Username, string Email, DateTime ExpiresAt);
public record LoginRequest(string Email, string Password);
public record RegisterRequest(string Username, string Email, string Password);

public record FileDto(
    Guid Id, string OriginalFileName, string ContentType,
    long FileSizeBytes, string FileHash, int CurrentVersion,
    DateTime UploadedAt, DateTime? LastModifiedAt, int ShareCount, int ViewCount);

public record FileVersionDto(
    Guid Id, int VersionNumber, long FileSizeBytes,
    string FileHash, DateTime CreatedAt, string? ChangeNote);

public record ShareLinkDto(
    Guid Id, string Token, string FileName, string? RecipientEmail,
    string Permission, DateTime CreatedAt, DateTime ExpiresAt,
    int MaxUses, int UseCount, bool IsActive, string ShareUrl);

public record CreateShareRequest(
    Guid FileId, string? RecipientEmail, string Permission,
    int ExpiryDays, int MaxUses);

public record PermissionDto(
    Guid Id, string FileName, string GrantedToEmail,
    string Permission, DateTime GrantedAt, bool IsActive);

public record GrantPermissionRequest(Guid FileId, string GrantedToEmail, string Permission);

public record AccessLogDto(
    Guid Id, string FileName, string Action, string ActorIdentifier,
    string? IpAddress, bool WasGranted, DateTime Timestamp, string? Details);

public record DashboardStats(
    int TotalFiles, long TotalStorageBytes, int ActiveShareLinks,
    int TotalAccessEvents, int DeniedAccessAttempts, int FilesSharedWithOthers);
