using Microsoft.EntityFrameworkCore;
using FirebaseAdmin.Messaging;
using lpdeBack.Data;

namespace lpdeBack.Services;

public class PushNotificationService
{
    private readonly AppDbContext _context;

    public PushNotificationService(AppDbContext context)
    {
        _context = context;
    }

    public async Task SendToUser(string userId, string title, string body, string? link = null)
    {
        var tokens = await _context.PushTokens
            .Where(t => t.UserId == userId)
            .Select(t => t.Token)
            .ToListAsync();

        var tokensToRemove = new List<string>();

        foreach (var token in tokens)
        {
            var success = await SendFcmMessage(token, title, body, link);
            if (!success) tokensToRemove.Add(token);
        }

        if (tokensToRemove.Count > 0)
        {
            var invalidTokens = await _context.PushTokens
                .Where(t => tokensToRemove.Contains(t.Token))
                .ToListAsync();
            _context.PushTokens.RemoveRange(invalidTokens);
            await _context.SaveChangesAsync();
        }
    }

    private async Task<bool> SendFcmMessage(string deviceToken, string title, string body, string? link)
    {
        try
        {
            // Data-only message: one single notification, Capacitor plugin handles display
            var message = new Message
            {
                Token = deviceToken,
                Android = new AndroidConfig
                {
                    Priority = Priority.High,
                    Notification = new AndroidNotification
                    {
                        Title = title,
                        Body = body,
                        Sound = "default",
                        ChannelId = "PushPluginChannel"
                    }
                },
                Data = new Dictionary<string, string>
                {
                    { "link", link ?? "/" },
                    { "title", title },
                    { "body", body }
                }
            };

            await FirebaseMessaging.DefaultInstance.SendAsync(message);
            return true;
        }
        catch (FirebaseMessagingException ex) when (
            ex.MessagingErrorCode == MessagingErrorCode.Unregistered ||
            ex.MessagingErrorCode == MessagingErrorCode.InvalidArgument)
        {
            return false;
        }
        catch
        {
            return true;
        }
    }
}
