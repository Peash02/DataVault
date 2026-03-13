using DataVault.Web.Models;
using DataVault.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace DataVault.Web.Controllers;

public class LogsController : Controller
{
    private readonly VaultApiClient _api;

    public LogsController(VaultApiClient api) => _api = api;

    [HttpGet("/logs")]
    public async Task<IActionResult> Index(bool deniedOnly = false)
    {
        if (HttpContext.Session.GetString("jwt_token") == null)
            return RedirectToAction("Login", "Auth");

        var logs = await _api.GetLogsAsync(deniedOnly);
        return View(new LogsViewModel { Logs = logs, DeniedOnly = deniedOnly });
    }
}
