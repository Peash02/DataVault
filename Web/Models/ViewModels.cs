namespace DataVault.Web.Models;

// ── Auth ──────────────────────────────────────────────────────────────────
public class LoginViewModel
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string? Error { get; set; }
}

public class RegisterViewModel
{
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string? Error { get; set; }
}

// ── API Response DTOs (mirror API models) ────────────────────────────────
public record AuthResponse(string Token, string Username, string Email, DateTime ExpiresAt);

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

public record PermissionDto(
    Guid Id, string FileName, string GrantedToEmail,
    string Permission, DateTime GrantedAt, bool IsActive);

public record AccessLogDto(
    Guid Id, string FileName, string Action, string ActorIdentifier,
    string? IpAddress, bool WasGranted, DateTime Timestamp, string? Details);

public record DashboardStats(
    int TotalFiles, long TotalStorageBytes, int ActiveShareLinks,
    int TotalAccessEvents, int DeniedAccessAttempts, int FilesSharedWithOthers);

// ── Page ViewModels ───────────────────────────────────────────────────────
public class DashboardViewModel
{
    public DashboardStats? Stats { get; set; }
    public List<AccessLogDto> RecentLogs { get; set; } = new();
    public string Username { get; set; } = "";
}

public class FilesViewModel
{
    public List<FileDto> Files { get; set; } = new();
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
}

public class FileDetailViewModel
{
    public FileDto? File { get; set; }
    public List<FileVersionDto> Versions { get; set; } = new();
    public List<AccessLogDto> Logs { get; set; } = new();
}

public class SharesViewModel
{
    public List<ShareLinkDto> ShareLinks { get; set; } = new();
    public List<PermissionDto> Permissions { get; set; } = new();
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
}

public class LogsViewModel
{
    public List<AccessLogDto> Logs { get; set; } = new();
    public bool DeniedOnly { get; set; }
}

// ── Form Models ───────────────────────────────────────────────────────────
public class CreateShareForm
{
    public Guid FileId { get; set; }
    public string? RecipientEmail { get; set; }
    public string Permission { get; set; } = "View";
    public int ExpiryDays { get; set; } = 7;
    public int MaxUses { get; set; } = 10;
}

public class GrantPermissionForm
{
    public Guid FileId { get; set; }
    public string GrantedToEmail { get; set; } = "";
    public string Permission { get; set; } = "View";
}
