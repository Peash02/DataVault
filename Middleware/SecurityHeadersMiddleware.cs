namespace DataVault.MVC.Middleware;

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
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"]        = "DENY";
        context.Response.Headers["X-XSS-Protection"]       = "1; mode=block";
        context.Response.Headers["Referrer-Policy"]         = "strict-origin-when-cross-origin";
        context.Response.Headers["X-Request-ID"]            = Guid.NewGuid().ToString();

        if (context.Request.Headers.ContainsKey("X-Vault-Password"))
            _logger.LogDebug("Request to {Path} includes vault credential header", context.Request.Path);

        await _next(context);
    }
}
