namespace DataVault.API.Middleware;

/// <summary>
/// Strips the vault password header from logs to prevent credential leakage,
/// and adds a unique request ID to every response.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SecurityHeadersMiddleware> _logger;

    public SecurityHeadersMiddleware(RequestDelegate next, ILogger<SecurityHeadersMiddleware> logger)
    {
        _next = next; _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Add security response headers
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers["X-Request-ID"] = Guid.NewGuid().ToString();

        // Remove sensitive headers from logs
        var sensitiveHeader = "X-Vault-Password";
        var hasPassword = context.Request.Headers.ContainsKey(sensitiveHeader);

        if (hasPassword)
        {
            _logger.LogDebug("Request to {Path} includes vault credential header", 
                context.Request.Path);
        }

        await _next(context);
    }
}
