using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace LaPlateformeDeLemploiAPI.Hubs;

[Authorize]
public class ChatHub : Hub
{
    public async Task SendMessage(int conversationId, string content)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId == null) return;

        // Broadcast to the conversation group
        await Clients.Group($"conv-{conversationId}").SendAsync("ReceiveMessage", new
        {
            conversationId,
            senderId = int.Parse(userId),
            content,
            sentAt = DateTime.UtcNow
        });
    }

    public async Task JoinConversation(int conversationId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"conv-{conversationId}");
    }

    public async Task LeaveConversation(int conversationId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"conv-{conversationId}");
    }

    public async Task StartTyping(int conversationId)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        await Clients.OthersInGroup($"conv-{conversationId}").SendAsync("UserTyping", new { conversationId, userId });
    }

    public async Task StopTyping(int conversationId)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        await Clients.OthersInGroup($"conv-{conversationId}").SendAsync("UserStoppedTyping", new { conversationId, userId });
    }
}
