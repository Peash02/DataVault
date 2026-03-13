using DataVault.Web.Models;
using DataVault.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace DataVault.Web.Controllers;

public class HomeController : Controller
{
    private readonly VaultApiClient _api;

    public HomeController(VaultApiClient api) => _api = api;

    [HttpGet("/")]
    public async Task<IActionResult> Index()
    {
        if (HttpContext.Session.GetString("jwt_token") == null)
            return RedirectToAction("Login", "Auth");

        var stats = await _api.GetStatsAsync();
        var logs = await _api.GetLogsAsync();

        return View(new DashboardViewModel
        {
            Stats = stats,
            RecentLogs = logs.Take(10).ToList(),
            Username = HttpContext.Session.GetString("vault_username") ?? "User"
        });
    }
}
