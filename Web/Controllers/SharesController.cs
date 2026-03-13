using DataVault.Web.Models;
using DataVault.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace DataVault.Web.Controllers;

public class SharesController : Controller
{
    private readonly VaultApiClient _api;

    public SharesController(VaultApiClient api) => _api = api;

    private bool IsAuth => HttpContext.Session.GetString("jwt_token") != null;

    [HttpGet("/shares")]
    public async Task<IActionResult> Index()
    {
        if (!IsAuth) return RedirectToAction("Login", "Auth");
        var links = await _api.GetShareLinksAsync();
        var perms = await _api.GetPermissionsAsync();
        return View(new SharesViewModel { ShareLinks = links, Permissions = perms });
    }

    [HttpPost("/shares/create")]
    public async Task<IActionResult> Create(CreateShareForm form)
    {
        if (!IsAuth) return RedirectToAction("Login", "Auth");
        var link = await _api.CreateShareLinkAsync(form);
        if (link != null) TempData["ShareUrl"] = link.ShareUrl;
        else TempData["Error"] = "Failed to create share link.";
        return RedirectToAction("Index");
    }

    [HttpPost("/shares/{id}/revoke")]
    public async Task<IActionResult> Revoke(Guid id)
    {
        if (!IsAuth) return RedirectToAction("Login", "Auth");
        await _api.RevokeShareLinkAsync(id);
        TempData["Success"] = "Share link revoked.";
        return RedirectToAction("Index");
    }

    [HttpPost("/shares/permissions/grant")]
    public async Task<IActionResult> GrantPermission(GrantPermissionForm form)
    {
        if (!IsAuth) return RedirectToAction("Login", "Auth");
        var ok = await _api.GrantPermissionAsync(form);
        TempData[ok ? "Success" : "Error"] = ok ? "Permission granted." : "Failed to grant permission.";
        return RedirectToAction("Index");
    }

    [HttpPost("/shares/permissions/{id}/revoke")]
    public async Task<IActionResult> RevokePermission(Guid id)
    {
        if (!IsAuth) return RedirectToAction("Login", "Auth");
        await _api.RevokePermissionAsync(id);
        TempData["Success"] = "Permission revoked.";
        return RedirectToAction("Index");
    }
}
