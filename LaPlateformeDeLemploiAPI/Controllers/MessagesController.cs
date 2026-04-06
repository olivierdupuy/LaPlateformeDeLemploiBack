using System.Security.Claims;
using LaPlateformeDeLemploiAPI.Data;
using LaPlateformeDeLemploiAPI.DTOs;
using LaPlateformeDeLemploiAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LaPlateformeDeLemploiAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MessagesController : ControllerBase
{
    private readonly AppDbContext _context;
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public MessagesController(AppDbContext context) => _context = context;

    [HttpGet("conversations")]
    public async Task<ActionResult<List<ConversationDto>>> GetConversations()
    {
        var userId = UserId;
        var conversations = await _context.Conversations
            .Include(c => c.User1).ThenInclude(u => u.Company)
            .Include(c => c.User2).ThenInclude(u => u.Company)
            .Include(c => c.Messages)
            .Where(c => c.User1Id == userId || c.User2Id == userId)
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .ToListAsync();

        return Ok(conversations.Select(c =>
        {
            var other = c.User1Id == userId ? c.User2 : c.User1;
            var lastMsg = c.Messages.OrderByDescending(m => m.SentAt).FirstOrDefault();
            return new ConversationDto
            {
                Id = c.Id,
                OtherUserId = other.Id,
                OtherUserName = $"{other.FirstName} {other.LastName}",
                OtherUserRole = other.Role,
                CompanyName = other.Company?.Name,
                CompanyLogoUrl = other.Company?.LogoUrl,
                LastMessage = lastMsg?.Content,
                LastMessageAt = lastMsg?.SentAt,
                UnreadCount = c.Messages.Count(m => m.SenderId != userId && !m.IsRead)
            };
        }).ToList());
    }

    [HttpGet("conversations/{conversationId}")]
    public async Task<ActionResult<List<MessageDto>>> GetMessages(int conversationId)
    {
        var userId = UserId;
        var conv = await _context.Conversations.FindAsync(conversationId);
        if (conv == null || (conv.User1Id != userId && conv.User2Id != userId))
            return NotFound();

        // Marquer comme lu
        var unread = await _context.Messages
            .Where(m => m.ConversationId == conversationId && m.SenderId != userId && !m.IsRead)
            .ToListAsync();
        unread.ForEach(m => m.IsRead = true);
        await _context.SaveChangesAsync();

        var messages = await _context.Messages
            .Include(m => m.Sender)
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.SentAt)
            .Select(m => new MessageDto
            {
                Id = m.Id,
                SenderId = m.SenderId,
                SenderName = m.Sender.FirstName + " " + m.Sender.LastName,
                Content = m.Content,
                SentAt = m.SentAt,
                IsRead = m.IsRead,
                IsMine = m.SenderId == userId
            })
            .ToListAsync();

        return Ok(messages);
    }

    [HttpPost]
    public async Task<ActionResult<MessageDto>> SendMessage(SendMessageDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Content))
            return BadRequest(new { message = "Le message ne peut pas etre vide." });

        var userId = UserId;
        if (dto.RecipientId == userId)
            return BadRequest(new { message = "Vous ne pouvez pas vous envoyer un message." });

        var recipient = await _context.Users.FindAsync(dto.RecipientId);
        if (recipient == null) return NotFound(new { message = "Destinataire introuvable." });

        // Trouver ou creer la conversation
        var conv = await _context.Conversations.FirstOrDefaultAsync(c =>
            (c.User1Id == userId && c.User2Id == dto.RecipientId) ||
            (c.User1Id == dto.RecipientId && c.User2Id == userId));

        if (conv == null)
        {
            conv = new Conversation { User1Id = userId, User2Id = dto.RecipientId };
            _context.Conversations.Add(conv);
            await _context.SaveChangesAsync();
        }

        var message = new Message
        {
            ConversationId = conv.Id,
            SenderId = userId,
            Content = dto.Content
        };
        _context.Messages.Add(message);
        conv.LastMessageAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Creer une notification
        _context.Notifications.Add(new Notification
        {
            UserId = dto.RecipientId,
            Title = "Nouveau message",
            Message = $"{(await _context.Users.FindAsync(userId))!.FirstName} vous a envoye un message",
            Type = "info",
            Link = "/messagerie"
        });
        await _context.SaveChangesAsync();

        var sender = await _context.Users.FindAsync(userId);
        return Ok(new MessageDto
        {
            Id = message.Id,
            SenderId = userId,
            SenderName = sender!.FirstName + " " + sender.LastName,
            Content = message.Content,
            SentAt = message.SentAt,
            IsRead = false,
            IsMine = true
        });
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<int>> GetUnreadCount()
    {
        var userId = UserId;
        var count = await _context.Messages
            .Where(m => m.SenderId != userId && !m.IsRead &&
                (m.Conversation.User1Id == userId || m.Conversation.User2Id == userId))
            .CountAsync();
        return Ok(count);
    }
}
