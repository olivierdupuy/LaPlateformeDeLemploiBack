using lpdeBack.Data;
using Microsoft.EntityFrameworkCore;

namespace lpdeBack.Middleware;

public class MaintenanceMiddleware
{
    private readonly RequestDelegate _next;
    private static DateTime _lastCheck = DateTime.MinValue;
    private static bool _isMaintenanceMode = false;

    public MaintenanceMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        // Cache the check for 30 seconds to avoid hitting DB on every request
        if ((DateTime.UtcNow - _lastCheck).TotalSeconds > 30)
        {
            var val = await db.PlatformSettings
                .Where(s => s.Key == "maintenance_mode")
                .Select(s => s.Value)
                .FirstOrDefaultAsync();
            _isMaintenanceMode = val == "true";
            _lastCheck = DateTime.UtcNow;
        }

        if (_isMaintenanceMode)
        {
            var path = context.Request.Path.Value ?? "";

            // Allow admin API calls and settings endpoint (so admin can disable maintenance)
            if (path.StartsWith("/api/admin") ||
                path.StartsWith("/api/auth/login") ||
                path.StartsWith("/api/auth/me") ||
                path.StartsWith("/hubs"))
            {
                await _next(context);
                return;
            }

            context.Response.StatusCode = 503;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"maintenance\":true,\"message\":\"La plateforme est en maintenance. Veuillez reessayer plus tard.\"}");
            return;
        }

        await _next(context);
    }
}
