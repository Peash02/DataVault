using DataVault.Web.Models;
using DataVault.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace DataVault.Web.Controllers;

public class AuthController : Controller
{
    private readonly VaultApiClient _api;

    public AuthController(VaultApiClient api) => _api = api;

    [HttpGet("/login")]
    public IActionResult Login() =>
        HttpContext.Session.GetString("jwt_token") != null
            ? RedirectToAction("Index", "Home")
            : View(new LoginViewModel());

    [HttpPost("/login")]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        var result = await _api.LoginAsync(model.Email, model.Password);
        if (result == null)
        {
            model.Error = "Invalid email or password.";
            return View(model);
        }

        HttpContext.Session.SetString("jwt_token", result.Token);
        HttpContext.Session.SetString("vault_username", result.Username);
        HttpContext.Session.SetString("vault_email", result.Email);
        // Store password in session for API calls that need it (decrypt)
        // In production, use a short-lived encrypted session token instead
        HttpContext.Session.SetString("vault_password", model.Password);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet("/register")]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost("/register")]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (model.Password.Length < 8)
        {
            model.Error = "Password must be at least 8 characters.";
            return View(model);
        }

        var result = await _api.RegisterAsync(model.Username, model.Email, model.Password);
        if (result == null)
        {
            model.Error = "Registration failed. Email or username may already exist.";
            return View(model);
        }

        HttpContext.Session.SetString("jwt_token", result.Token);
        HttpContext.Session.SetString("vault_username", result.Username);
        HttpContext.Session.SetString("vault_email", result.Email);
        HttpContext.Session.SetString("vault_password", model.Password);
        return RedirectToAction("Index", "Home");
    }

    [HttpPost("/logout")]
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Login");
    }
}
