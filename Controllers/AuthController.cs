using System.Security.Claims;
using DataVault.MVC.Data;
using DataVault.MVC.Models;
using DataVault.MVC.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DataVault.MVC.Controllers;

public class AuthController : Controller
{
    private readonly VaultDbContext _db;
    private readonly IEncryptionService _crypto;
    private readonly ILogger<AuthController> _logger;

    public AuthController(VaultDbContext db, IEncryptionService crypto, ILogger<AuthController> logger)
    {
        _db = db; _crypto = crypto; _logger = logger;
    }

    // ── Login ─────────────────────────────────────────────────────────────

    [HttpGet("/login")]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return Redirect(returnUrl ?? "/");
        ViewBag.ReturnUrl = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost("/login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == model.Email.ToLower());

        if (user == null || !BCrypt.Net.BCrypt.Verify(model.Password, user.PasswordHash))
        {
            model.Error = "Invalid email or password.";
            ViewBag.ReturnUrl = returnUrl;
            return View(model);
        }

        if (!user.IsActive)
        {
            model.Error = "Account is disabled.";
            ViewBag.ReturnUrl = returnUrl;
            return View(model);
        }

        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await SignInAsync(user, model.Password);
        _logger.LogInformation("User logged in: {Email}", user.Email);
        return Redirect(returnUrl ?? "/");
    }

    // ── Register ──────────────────────────────────────────────────────────

    [HttpGet("/register")]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost("/register")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (model.Password.Length < 8)
        {
            model.Error = "Password must be at least 8 characters.";
            return View(model);
        }

        if (await _db.Users.AnyAsync(u => u.Email == model.Email.ToLower()))
        {
            model.Error = "Email already registered.";
            return View(model);
        }

        if (await _db.Users.AnyAsync(u => u.Username == model.Username))
        {
            model.Error = "Username already taken.";
            return View(model);
        }

        var masterKey = _crypto.GenerateMasterKey();
        var encryptedMasterKey = _crypto.EncryptMasterKey(masterKey, model.Password);

        var user = new User
        {
            Username = model.Username,
            Email = model.Email.ToLower(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password, workFactor: 12),
            EncryptedMasterKey = encryptedMasterKey
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _logger.LogInformation("New user registered: {Email}", user.Email);

        await SignInAsync(user, model.Password);
        return RedirectToAction("Index", "Home");
    }

    // ── Logout ────────────────────────────────────────────────────────────

    [HttpPost("/logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }

    // ── Helper ────────────────────────────────────────────────────────────

    private async Task SignInAsync(User user, string password)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email,          user.Email),
            new(ClaimTypes.Name,           user.Username),
            // Store the vault password in the auth ticket so controllers
            // can retrieve it when decrypting the master key.
            // NOTE: In production replace this with a short-lived encrypted
            // server-side session token; never store raw passwords in cookies.
            new("vault_password", password)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
            });
    }
}