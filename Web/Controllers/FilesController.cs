using DataVault.Web.Models;
using DataVault.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace DataVault.Web.Controllers;

public class FilesController : Controller
{
    private readonly VaultApiClient _api;

    public FilesController(VaultApiClient api) => _api = api;

    private bool IsAuth => HttpContext.Session.GetString("jwt_token") != null;

    [HttpGet("/files")]
    public async Task<IActionResult> Index()
    {
        if (!IsAuth) return RedirectToAction("Login", "Auth");
        var files = await _api.GetFilesAsync();
        return View(new FilesViewModel { Files = files });
    }

    [HttpPost("/files/upload")]
    public async Task<IActionResult> Upload(IFormFile file, string? changeNote)
    {
        if (!IsAuth) return RedirectToAction("Login", "Auth");

        if (file == null || file.Length == 0)
        {
            TempData["Error"] = "Please select a file.";
            return RedirectToAction("Index");
        }

        var (success, message) = await _api.UploadFileAsync(file, changeNote);
        if (success) TempData["Success"] = $"'{file.FileName}' encrypted and stored.";
        else TempData["Error"] = $"Upload failed: {message}";

        return RedirectToAction("Index");
    }

    [HttpGet("/files/{id}/download")]
    public async Task<IActionResult> Download(Guid id)
    {
        if (!IsAuth) return RedirectToAction("Login", "Auth");

        var (stream, contentType, fileName) = await _api.DownloadFileAsync(id);
        if (stream == null) return NotFound();

        return File(stream, contentType!, fileName!, enableRangeProcessing: false);
    }

    [HttpPost("/files/{id}/delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!IsAuth) return RedirectToAction("Login", "Auth");
        await _api.DeleteFileAsync(id);
        TempData["Success"] = "File deleted.";
        return RedirectToAction("Index");
    }

    [HttpGet("/files/{id}/detail")]
    public async Task<IActionResult> Detail(Guid id)
    {
        if (!IsAuth) return RedirectToAction("Login", "Auth");

        var files = await _api.GetFilesAsync();
        var file = files.FirstOrDefault(f => f.Id == id);
        if (file == null) return NotFound();

        var versions = await _api.GetVersionsAsync(id);
        var logs = await _api.GetFileLogsAsync(id);

        return View(new FileDetailViewModel { File = file, Versions = versions, Logs = logs });
    }

    [HttpPost("/files/{fileId}/versions/{versionId}/restore")]
    public async Task<IActionResult> RestoreVersion(Guid fileId, Guid versionId)
    {
        if (!IsAuth) return RedirectToAction("Login", "Auth");
        var ok = await _api.RestoreVersionAsync(fileId, versionId);
        TempData[ok ? "Success" : "Error"] = ok ? "Version restored." : "Restore failed.";
        return RedirectToAction("Detail", new { id = fileId });
    }
}
