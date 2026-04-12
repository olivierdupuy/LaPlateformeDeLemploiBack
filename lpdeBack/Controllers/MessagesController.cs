using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using lpdeBack.Data;
using lpdeBack.Models;
using lpdeBack.DTOs;
using lpdeBack.Hubs;
using lpdeBack.Services;

namespace lpdeBack.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MessagesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly PushNotificationService _pushService;
    public MessagesController(AppDbContext context, IHubContext<ChatHub> hubContext, PushNotificationService pushService)
    {
        _context = context;
        _hubContext = hubContext;
        _pushService = pushService;
    }
    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet("conversations")]
    public async Task<ActionResult<IEnumerable<object>>> GetConversations()
    {
        var userId = GetUserId();

        var conversations = await _context.Messages
            .Include(m => m.Application).ThenInclude(a => a.JobOffer)
            .Include(m => m.Sender)
            .Include(m => m.Receiver)
            .Where(m => m.SenderId == userId || m.ReceiverId == userId)
            .GroupBy(m => m.ApplicationId)
            .Select(g => new
            {
                applicationId = g.Key,
                lastMessage = g.OrderByDescending(m => m.CreatedAt).First().Content,
                lastMessageAt = g.Max(m => m.CreatedAt),
                unreadCount = g.Count(m => m.ReceiverId == userId && !m.IsRead),
                jobTitle = g.First().Application.JobOffer.Title,
                company = g.First().Application.JobOffer.Company,
            })
            .OrderByDescending(c => c.lastMessageAt)
            .ToListAsync();

        // Get other user info for each conversation
        var result = new List<object>();
        foreach (var conv in conversations)
        {
            var firstMsg = await _context.Messages
                .Include(m => m.Sender).Include(m => m.Receiver)
                .FirstAsync(m => m.ApplicationId == conv.applicationId);
            var otherUser = firstMsg.SenderId == userId ? firstMsg.Receiver : firstMsg.Sender;

            result.Add(new
            {
                conv.applicationId,
                conv.lastMessage,
                conv.lastMessageAt,
                conv.unreadCount,
                conv.jobTitle,
                conv.company,
                otherUserName = $"{otherUser.FirstName} {otherUser.LastName}",
                otherUserId = otherUser.Id
            });
        }

        return Ok(result);
    }

    [HttpGet("conversation/{applicationId}")]
    public async Task<ActionResult<IEnumerable<object>>> GetMessages(int applicationId)
    {
        var userId = GetUserId();
        var app = await _context.Applications.Include(a => a.JobOffer).FirstOrDefaultAsync(a => a.Id == applicationId);
        if (app == null) return NotFound();

        // Only participants can view
        if (app.UserId != userId && app.JobOffer.CreatedByUserId != userId)
            return Forbid();

        var messages = await _context.Messages
            .Include(m => m.Sender)
            .Where(m => m.ApplicationId == applicationId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new
            {
                m.Id, m.SenderId, m.ReceiverId, m.Content, m.IsRead, m.CreatedAt,
                senderName = m.Sender.FirstName + " " + m.Sender.LastName,
                isMine = m.SenderId == userId
            })
            .ToListAsync();

        return Ok(messages);
    }

    [HttpPost]
    public async Task<ActionResult> Send(MessageCreateDto dto)
    {
        var userId = GetUserId();
        var app = await _context.Applications.Include(a => a.JobOffer).FirstOrDefaultAsync(a => a.Id == dto.ApplicationId);
        if (app == null) return NotFound();

        var isCandidate = app.UserId == userId;
        var isRecruiter = app.JobOffer.CreatedByUserId == userId;
        if (!isCandidate && !isRecruiter) return Forbid();

        var receiverId = isCandidate ? app.JobOffer.CreatedByUserId : app.UserId;
        if (receiverId == null) return BadRequest("Destinataire introuvable.");

        var message = new Message
        {
            SenderId = userId,
            ReceiverId = receiverId,
            ApplicationId = dto.ApplicationId,
            Content = dto.Content
        };

        _context.Messages.Add(message);

        var sender = await _context.Users.FindAsync(userId);
        _context.Notifications.Add(new Notification
        {
            UserId = receiverId,
            Title = "Nouveau message",
            Message = $"{sender?.FirstName} {sender?.LastName} vous a envoye un message.",
            Link = "/messagerie",
            Type = "NouveauMessage"
        });

        await _context.SaveChangesAsync();

        // Real-time: send message to conversation group and receiver
        var senderUser = await _context.Users.FindAsync(userId);
        var messagePayload = new
        {
            message.Id,
            message.SenderId,
            message.ReceiverId,
            message.ApplicationId,
            message.Content,
            message.IsRead,
            message.CreatedAt,
            senderName = senderUser != null ? $"{senderUser.FirstName} {senderUser.LastName}" : "Inconnu",
            isMine = false
        };

        await _hubContext.Clients.Group($"conversation_{dto.ApplicationId}")
            .SendAsync("NewMessage", messagePayload);

        // Also notify receiver directly for badge update
        foreach (var connId in ChatHub.GetConnectionIds(receiverId))
        {
            await _hubContext.Clients.Client(connId).SendAsync("UnreadCountUpdate");
        }

        // Push notification (mobile will suppress if app is in foreground)
        await _pushService.SendToUser(receiverId, "Nouveau message",
            $"{senderUser?.FirstName} {senderUser?.LastName}: {dto.Content[..Math.Min(dto.Content.Length, 80)]}",
            $"/conversation/{dto.ApplicationId}");

        return Ok(new { message.Id });
    }

    [HttpPatch("conversation/{applicationId}/read")]
    public async Task<IActionResult> MarkAsRead(int applicationId)
    {
        var userId = GetUserId();
        await _context.Messages
            .Where(m => m.ApplicationId == applicationId && m.ReceiverId == userId && !m.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsRead, true));
        return NoContent();
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<object>> GetUnreadCount()
    {
        var count = await _context.Messages.CountAsync(m => m.ReceiverId == GetUserId() && !m.IsRead);
        return new { count };
    }
}
