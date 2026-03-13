using System.Net.Http.Headers;
using System.Net.Http.Json;
using Blazored.LocalStorage;
using DataVault.Blazor.Models;
using Microsoft.AspNetCore.Components.Forms;

namespace DataVault.Blazor.Services;

public class VaultApiService
{
    private readonly HttpClient _http;
    private readonly ILocalStorageService _localStorage;
    private string? _vaultPassword;  // Held in memory only, never persisted

    public VaultApiService(HttpClient http, ILocalStorageService localStorage)
    {
        _http = http;
        _localStorage = localStorage;
    }

    // ── Auth ──────────────────────────────────────────────────────────────

    public async Task<AuthResponse?> LoginAsync(string email, string password)
    {
        var resp = await _http.PostAsJsonAsync("api/auth/login",
            new LoginRequest(email, password));
        if (!resp.IsSuccessStatusCode) return null;
        var auth = await resp.Content.ReadFromJsonAsync<AuthResponse>();
        if (auth != null)
        {
            await _localStorage.SetItemAsync("vault_token", auth.Token);
            await _localStorage.SetItemAsync("vault_user", auth.Username);
            _vaultPassword = password;
            SetAuthHeader(auth.Token);
        }
        return auth;
    }

    public async Task<AuthResponse?> RegisterAsync(string username, string email, string password)
    {
        var resp = await _http.PostAsJsonAsync("api/auth/register",
            new RegisterRequest(username, email, password));
        if (!resp.IsSuccessStatusCode) return null;
        var auth = await resp.Content.ReadFromJsonAsync<AuthResponse>();
        if (auth != null)
        {
            await _localStorage.SetItemAsync("vault_token", auth.Token);
            await _localStorage.SetItemAsync("vault_user", auth.Username);
            _vaultPassword = password;
            SetAuthHeader(auth.Token);
        }
        return auth;
    }

    public async Task LogoutAsync()
    {
        await _localStorage.RemoveItemAsync("vault_token");
        await _localStorage.RemoveItemAsync("vault_user");
        _vaultPassword = null;
        _http.DefaultRequestHeaders.Authorization = null;
    }

    public async Task<bool> InitFromStorageAsync()
    {
        var token = await _localStorage.GetItemAsync<string>("vault_token");
        if (!string.IsNullOrEmpty(token)) { SetAuthHeader(token); return true; }
        return false;
    }

    public async Task<bool> IsAuthenticatedAsync() =>
        !string.IsNullOrEmpty(await _localStorage.GetItemAsync<string>("vault_token"));

    public async Task<string?> GetUsernameAsync() =>
        await _localStorage.GetItemAsync<string>("vault_user");

    public void SetVaultPassword(string password) => _vaultPassword = password;
    public bool HasVaultPassword => !string.IsNullOrEmpty(_vaultPassword);

    // ── Files ──────────────────────────────────────────────────────────────

    public async Task<List<FileDto>?> GetFilesAsync() =>
        await _http.GetFromJsonAsync<List<FileDto>>("api/files");

    public async Task<DashboardStats?> GetStatsAsync() =>
        await _http.GetFromJsonAsync<DashboardStats>("api/files/stats");

    public async Task<(bool Success, string Message)> UploadFileAsync(
        IBrowserFile file, string? changeNote = null)
    {
        if (string.IsNullOrEmpty(_vaultPassword))
            return (false, "Vault password not set. Please re-login.");

        using var content = new MultipartFormDataContent();
        var fileStream = file.OpenReadStream(maxAllowedSize: 100 * 1024 * 1024);
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
        content.Add(streamContent, "file", file.Name);
        if (changeNote != null) content.Add(new StringContent(changeNote), "changeNote");

        var req = new HttpRequestMessage(HttpMethod.Post, "api/files/upload") { Content = content };
        req.Headers.Add("X-Vault-Password", _vaultPassword);

        var resp = await _http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        return resp.IsSuccessStatusCode
            ? (true, "File encrypted and stored successfully.")
            : (false, $"Upload failed: {body}");
    }

    public async Task<bool> DeleteFileAsync(Guid fileId)
    {
        var resp = await _http.DeleteAsync($"api/files/{fileId}");
        return resp.IsSuccessStatusCode;
    }

    public async Task<List<FileVersionDto>?> GetVersionsAsync(Guid fileId) =>
        await _http.GetFromJsonAsync<List<FileVersionDto>>($"api/files/{fileId}/versions");

    public async Task<bool> RestoreVersionAsync(Guid fileId, Guid versionId)
    {
        var resp = await _http.PostAsync($"api/files/{fileId}/versions/{versionId}/restore", null);
        return resp.IsSuccessStatusCode;
    }

    // ── Share Links ────────────────────────────────────────────────────────

    public async Task<List<ShareLinkDto>?> GetShareLinksAsync() =>
        await _http.GetFromJsonAsync<List<ShareLinkDto>>("api/shares");

    public async Task<ShareLinkDto?> CreateShareLinkAsync(CreateShareRequest req)
    {
        var resp = await _http.PostAsJsonAsync("api/shares", req);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<ShareLinkDto>();
    }

    public async Task<bool> RevokeShareLinkAsync(Guid linkId)
    {
        var resp = await _http.DeleteAsync($"api/shares/{linkId}");
        return resp.IsSuccessStatusCode;
    }

    // ── Permissions ────────────────────────────────────────────────────────

    public async Task<List<PermissionDto>?> GetPermissionsAsync() =>
        await _http.GetFromJsonAsync<List<PermissionDto>>("api/shares/permissions");

    public async Task<PermissionDto?> GrantPermissionAsync(GrantPermissionRequest req)
    {
        var resp = await _http.PostAsJsonAsync("api/shares/permissions", req);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<PermissionDto>();
    }

    public async Task<bool> RevokePermissionAsync(Guid permissionId)
    {
        var resp = await _http.DeleteAsync($"api/shares/permissions/{permissionId}");
        return resp.IsSuccessStatusCode;
    }

    // ── Access Logs ────────────────────────────────────────────────────────

    public async Task<List<AccessLogDto>?> GetLogsAsync(bool? deniedOnly = null)
    {
        var url = deniedOnly == true ? "api/logs?deniedOnly=true" : "api/logs";
        return await _http.GetFromJsonAsync<List<AccessLogDto>>(url);
    }

    public async Task<List<AccessLogDto>?> GetFileLogsAsync(Guid fileId) =>
        await _http.GetFromJsonAsync<List<AccessLogDto>>($"api/logs/file/{fileId}");

    // ── Helpers ────────────────────────────────────────────────────────────

    private void SetAuthHeader(string token) =>
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }

    public static string FileTypeIcon(string contentType) => contentType switch
    {
        var t when t.Contains("pdf") => "📄",
        var t when t.Contains("image") => "🖼️",
        var t when t.Contains("video") => "🎬",
        var t when t.Contains("audio") => "🎵",
        var t when t.Contains("zip") || t.Contains("compressed") => "📦",
        var t when t.Contains("spreadsheet") || t.Contains("excel") => "📊",
        var t when t.Contains("word") || t.Contains("document") => "📝",
        var t when t.Contains("text") => "📃",
        _ => "📁"
    };
}
