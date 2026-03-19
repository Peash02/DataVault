using System.Net.Http.Headers;
using System.Net.Http.Json;
using DataVault.Web.Models;

namespace DataVault.Web.Services;

public class VaultApiClient
{
    private readonly HttpClient _http;
    private readonly IHttpContextAccessor _ctx;
    private readonly ILogger<VaultApiClient> _logger;

    public VaultApiClient(HttpClient http, IHttpContextAccessor ctx, ILogger<VaultApiClient> logger)
    {
        _http = http;
        _ctx = ctx;
        _logger = logger;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void SetAuth()
    {
        var token = _ctx.HttpContext?.Session.GetString("jwt_token");
        if (!string.IsNullOrEmpty(token))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private string? VaultPassword =>
        _ctx.HttpContext?.Session.GetString("vault_password");

    private HttpRequestMessage WithPassword(HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        var pw = VaultPassword;
        if (!string.IsNullOrEmpty(pw))
            req.Headers.Add("X-Vault-Password", pw);
        return req;
    }

    // ── Auth ──────────────────────────────────────────────────────────────

    public async Task<AuthResponse?> LoginAsync(string email, string password)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("api/auth/login", new { email, password });
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<AuthResponse>();
        }
        catch (Exception ex) { _logger.LogError(ex, "Login failed"); return null; }
    }

    public async Task<AuthResponse?> RegisterAsync(string username, string email, string password)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("api/auth/register", new { username, email, password });
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<AuthResponse>();
        }
        catch (Exception ex) { _logger.LogError(ex, "Register failed"); return null; }
    }

    // ── Files ─────────────────────────────────────────────────────────────

    public async Task<List<FileDto>> GetFilesAsync()
    {
        SetAuth();
        try { return await _http.GetFromJsonAsync<List<FileDto>>("api/files") ?? new(); }
        catch { return new(); }
    }

    public async Task<DashboardStats?> GetStatsAsync()
    {
        SetAuth();
        try { return await _http.GetFromJsonAsync<DashboardStats>("api/files/stats"); }
        catch { return null; }
    }

    public async Task<(bool Success, string Message)> UploadFileAsync(
        IFormFile file, string? changeNote)
    {
        SetAuth();
        try
        {
            using var content = new MultipartFormDataContent();
            using var stream = file.OpenReadStream();
            var sc = new StreamContent(stream);
            sc.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            content.Add(sc, "file", file.FileName);
            if (!string.IsNullOrEmpty(changeNote))
                content.Add(new StringContent(changeNote), "changeNote");

            var req = WithPassword(HttpMethod.Post, "api/files/upload");
            req.Content = content;
            var resp = await _http.SendAsync(req);
            return resp.IsSuccessStatusCode
                ? (true, "File encrypted and stored.")
                : (false, await resp.Content.ReadAsStringAsync());
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<(Stream? stream, string? contentType, string? fileName)> DownloadFileAsync(Guid fileId)
    {
        SetAuth();
        try
        {
            var req = WithPassword(HttpMethod.Get, $"api/files/{fileId}/download");
            var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode) return (null, null, null);

            var ct = resp.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            var fn = resp.Content.Headers.ContentDisposition?.FileNameStar
                     ?? resp.Content.Headers.ContentDisposition?.FileName
                     ?? "download";
            fn = fn.Trim('"');
            return (await resp.Content.ReadAsStreamAsync(), ct, fn);
        }
        catch { return (null, null, null); }
    }

    public async Task<bool> DeleteFileAsync(Guid id)
    {
        SetAuth();
        try { return (await _http.DeleteAsync($"api/files/{id}")).IsSuccessStatusCode; }
        catch { return false; }
    }

    public async Task<List<FileVersionDto>> GetVersionsAsync(Guid fileId)
    {
        SetAuth();
        try { return await _http.GetFromJsonAsync<List<FileVersionDto>>($"api/files/{fileId}/versions") ?? new(); }
        catch { return new(); }
    }

    public async Task<bool> RestoreVersionAsync(Guid fileId, Guid versionId)
    {
        SetAuth();
        try { return (await _http.PostAsync($"api/files/{fileId}/versions/{versionId}/restore", null)).IsSuccessStatusCode; }
        catch { return false; }
    }

    // ── Shares ────────────────────────────────────────────────────────────

    public async Task<List<ShareLinkDto>> GetShareLinksAsync()
    {
        SetAuth();
        try { return await _http.GetFromJsonAsync<List<ShareLinkDto>>("api/shares") ?? new(); }
        catch { return new(); }
    }

    public async Task<ShareLinkDto?> CreateShareLinkAsync(CreateShareForm form)
    {
        SetAuth();
        try
        {
            int permissionValue = form.Permission switch
            {
                "Download" => 1,
                "Comment" => 2,
                _ => 0
            };

            var payload = new
            {
                FileId = form.FileId,
                Permission = permissionValue,
                RecipientEmail = form.RecipientEmail,
                ExpiryDays = form.ExpiryDays,
                MaxUses = form.MaxUses
            };

            // Build request manually so JWT is guaranteed to be attached
            var token = _ctx.HttpContext?.Session.GetString("jwt_token");
            var req = new HttpRequestMessage(HttpMethod.Post, "api/shares");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(payload);

            var resp = await _http.SendAsync(req);

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                _logger.LogError("Share creation failed {Status}: {Body}", resp.StatusCode, body);
                return null;
            }

            return await resp.Content.ReadFromJsonAsync<ShareLinkDto>();
        }
        catch (Exception ex) { _logger.LogError(ex, "Share creation exception"); return null; }
    }

    public async Task<bool> RevokeShareLinkAsync(Guid id)
    {
        SetAuth();
        try { return (await _http.DeleteAsync($"api/shares/{id}")).IsSuccessStatusCode; }
        catch { return false; }
    }

    public async Task<List<PermissionDto>> GetPermissionsAsync()
    {
        SetAuth();
        try { return await _http.GetFromJsonAsync<List<PermissionDto>>("api/shares/permissions") ?? new(); }
        catch { return new(); }
    }

    public async Task<bool> GrantPermissionAsync(GrantPermissionForm form)
    {
        SetAuth();
        try
        {
            var resp = await _http.PostAsJsonAsync("api/shares/permissions",
                new { form.FileId, form.GrantedToEmail, form.Permission });
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> RevokePermissionAsync(Guid id)
    {
        SetAuth();
        try { return (await _http.DeleteAsync($"api/shares/permissions/{id}")).IsSuccessStatusCode; }
        catch { return false; }
    }

    // ── Logs ──────────────────────────────────────────────────────────────

    public async Task<List<AccessLogDto>> GetLogsAsync(bool deniedOnly = false)
    {
        SetAuth();
        var url = deniedOnly ? "api/logs?deniedOnly=true" : "api/logs";
        try { return await _http.GetFromJsonAsync<List<AccessLogDto>>(url) ?? new(); }
        catch { return new(); }
    }

    public async Task<List<AccessLogDto>> GetFileLogsAsync(Guid fileId)
    {
        SetAuth();
        try { return await _http.GetFromJsonAsync<List<AccessLogDto>>($"api/logs/file/{fileId}") ?? new(); }
        catch { return new(); }
    }
}
