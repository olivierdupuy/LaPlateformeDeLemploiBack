using lpdeBack.Data;
using lpdeBack.Models;

namespace lpdeBack.Services;

public class ActivityLogService
{
    private readonly AppDbContext _context;

    public ActivityLogService(AppDbContext context) => _context = context;

    public async Task Log(string action, string entityType, int? entityId, string details, string? userId = null, string? userName = null, string? ip = null)
    {
        _context.ActivityLogs.Add(new ActivityLog
        {
            UserId = userId,
            UserName = userName ?? "Systeme",
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            IpAddress = ip,
        });
        await _context.SaveChangesAsync();
    }
}
