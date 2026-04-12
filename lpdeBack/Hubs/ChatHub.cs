using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace lpdeBack.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private static readonly Dictionary<string, HashSet<string>> _userConnections = new();
    private static readonly Dictionary<int, HashSet<string>> _applicationGroups = new();

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId != null)
        {
            lock (_userConnections)
            {
                if (!_userConnections.ContainsKey(userId))
                    _userConnections[userId] = new HashSet<string>();
                _userConnections[userId].Add(Context.ConnectionId);
            }
            await Clients.Others.SendAsync("UserOnline", userId);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId != null)
        {
            lock (_userConnections)
            {
                if (_userConnections.ContainsKey(userId))
                {
                    _userConnections[userId].Remove(Context.ConnectionId);
                    if (_userConnections[userId].Count == 0)
                        _userConnections.Remove(userId);
                }
            }
            if (!IsUserOnline(userId))
                await Clients.Others.SendAsync("UserOffline", userId);
        }
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinConversation(int applicationId)
    {
        var group = $"conversation_{applicationId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
    }

    public async Task LeaveConversation(int applicationId)
    {
        var group = $"conversation_{applicationId}";
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
    }

    public async Task SendTyping(int applicationId)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var group = $"conversation_{applicationId}";
        await Clients.OthersInGroup(group).SendAsync("UserTyping", new { userId, applicationId });
    }

    public async Task StopTyping(int applicationId)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var group = $"conversation_{applicationId}";
        await Clients.OthersInGroup(group).SendAsync("UserStoppedTyping", new { userId, applicationId });
    }

    public static bool IsUserOnline(string userId)
    {
        lock (_userConnections)
        {
            return _userConnections.ContainsKey(userId) && _userConnections[userId].Count > 0;
        }
    }

    public static IEnumerable<string> GetOnlineUserIds()
    {
        lock (_userConnections)
        {
            return _userConnections.Keys.ToList();
        }
    }

    public static IEnumerable<string> GetConnectionIds(string userId)
    {
        lock (_userConnections)
        {
            if (_userConnections.TryGetValue(userId, out var connections))
                return connections.ToList();
            return Enumerable.Empty<string>();
        }
    }
}
