using DataVault.API.Data;
using DataVault.API.Models;
using DataVault.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DataVault.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly VaultDbContext _db;
    private readonly IEncryptionService _crypto;
    private readonly ITokenService _tokens;
    private readonly ILogger<AuthController> _logger;

    public AuthController(VaultDbContext db, IEncryptionService crypto,
        ITokenService tokens, ILogger<AuthController> logger)
    {
        _db = db; _crypto = crypto; _tokens = tokens; _logger = logger;
    }

    /// <summary>Register a new vault user</summary>
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest req)
    {
        if (await _db.Users.AnyAsync(u => u.Email == req.Email.ToLower()))
            return Conflict(new { message = "Email already registered." });

        if (await _db.Users.AnyAsync(u => u.Username == req.Username))
            return Conflict(new { message = "Username already taken." });

        // Generate and encrypt user's master key
        var masterKey = _crypto.GenerateMasterKey();
        var encryptedMasterKey = _crypto.EncryptMasterKey(masterKey, req.Password);

        var user = new User
        {
            Username = req.Username,
            Email = req.Email.ToLower(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password, workFactor: 12),
            EncryptedMasterKey = encryptedMasterKey
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _logger.LogInformation("New user registered: {Email}", user.Email);

        var token = _tokens.GenerateToken(user);
        var expiry = DateTime.UtcNow.AddHours(12);
        return Ok(new AuthResponse(token, user.Username, user.Email, expiry));
    }

    /// <summary>Login and receive a JWT</summary>
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email.ToLower());

        if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { message = "Invalid email or password." });

        if (!user.IsActive)
            return Forbid();

        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var token = _tokens.GenerateToken(user);
        var expiry = DateTime.UtcNow.AddHours(12);
        return Ok(new AuthResponse(token, user.Username, user.Email, expiry));
    }
}
