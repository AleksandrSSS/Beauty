using Beauty.Server.Data;
using Beauty.Server.Models;
using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text;

namespace Beauty.Server.Services;

public record NotificationResult(bool Success, bool IsMock, string Info);

public interface INotificationService
{
    Task<NotificationResult> SendAsync(NotifyChannel channel, string recipient, string message);
}

/// <summary>
/// Надсилає повідомлення через обраний канал.
/// Якщо провайдер каналу не налаштований в appsettings/env — працює MOCK-режим:
/// повідомлення логується і зберігається в NotificationLog (код видно в логах сервера).
/// </summary>
public class NotificationService(IConfiguration cfg, IHttpClientFactory http, ILogger<NotificationService> logger, AppDbContext db) : INotificationService
{
    /// <summary>Куди слати нотифікацію: для Email — адреса або телефон; решта каналів — зовнішній контакт або телефон.</summary>
    public static string ResolveRecipient(User client, NotifyChannel channel) =>
        channel == NotifyChannel.Email ? (client.Email ?? client.Phone)
                                       : (client.ExternalContact ?? client.Phone);
    public async Task<NotificationResult> SendAsync(NotifyChannel channel, string recipient, string message)
    {
        NotificationResult r = channel switch
        {
            NotifyChannel.Sms => await SendTwilioAsync(cfg["Notifications:Twilio:FromSms"], recipient, message, "SMS"),
            NotifyChannel.WhatsApp => await SendTwilioAsync(cfg["Notifications:Twilio:FromWhatsApp"], $"whatsapp:{recipient}", message, "WhatsApp"),
            NotifyChannel.Telegram => await SendTelegramAsync(recipient, message),
            NotifyChannel.Viber => await SendViberAsync(recipient, message),
            NotifyChannel.Email => await SendEmailAsync(recipient, message),
            _ => new NotificationResult(false, true, "unknown channel")
        };
        db.NotificationLogs.Add(new NotificationLog { Channel = channel, Recipient = recipient, Message = message, Success = r.Success, IsMock = r.IsMock });
        await db.SaveChangesAsync();
        return r;
    }

    private NotificationResult Mock(string label, string recipient, string message)
    {
        logger.LogWarning("[{Label} MOCK] -> {Recipient}: {Message}", label, recipient, message);
        return new NotificationResult(true, true, $"{label} mock (провайдер не налаштований; повідомлення в логах сервера)");
    }

    private async Task<NotificationResult> SendTelegramAsync(string chatId, string message)
    {
        var token = cfg["Notifications:TelegramBotToken"];
        if (string.IsNullOrWhiteSpace(token)) return Mock("Telegram", chatId, message);
        try
        {
            var c = http.CreateClient();
            var resp = await c.PostAsync($"https://api.telegram.org/bot{token}/sendMessage",
                JsonContent.Create(new { chat_id = chatId, text = message }));
            return new NotificationResult(resp.IsSuccessStatusCode, false, $"Telegram HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex) { return new NotificationResult(false, false, ex.Message); }
    }

    private async Task<NotificationResult> SendTwilioAsync(string? from, string to, string message, string label)
    {
        var sid = cfg["Notifications:Twilio:AccountSid"];
        var auth = cfg["Notifications:Twilio:AuthToken"];
        if (string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(auth) || string.IsNullOrWhiteSpace(from))
            return Mock(label, to, message);
        try
        {
            var c = http.CreateClient();
            var authBytes = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{sid}:{auth}"));
            c.DefaultRequestHeaders.Authorization = new("Basic", authBytes);
            var resp = await c.PostAsync($"https://api.twilio.com/2010-04-01/Accounts/{sid}/Messages.json",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["From"] = from, ["To"] = to, ["Body"] = message }));
            return new NotificationResult(resp.IsSuccessStatusCode, false, $"{label} HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex) { return new NotificationResult(false, false, ex.Message); }
    }

    private async Task<NotificationResult> SendViberAsync(string recipient, string message)
    {
        // Viber REST-шлюз (напр. Infobip/SmsClub) підключається за потреби; тут — mock
        return await Task.FromResult(Mock("Viber", recipient, message));
    }

    private async Task<NotificationResult> SendEmailAsync(string to, string message)
    {
        var host = cfg["Notifications:Smtp:Host"];
        if (string.IsNullOrWhiteSpace(host)) return Mock("Email", to, message);
        try
        {
            using var smtp = new SmtpClient(host, cfg.GetValue("Notifications:Smtp:Port", 587)) { EnableSsl = true };
            var user = cfg["Notifications:Smtp:User"];
            if (!string.IsNullOrWhiteSpace(user))
            {
                smtp.Credentials = new NetworkCredential(user, cfg["Notifications:Smtp:Password"]);
            }
            await smtp.SendMailAsync(new System.Net.Mail.MailMessage(cfg["Notifications:Smtp:From"]!, to, "Beauty Salon", message));
            return new NotificationResult(true, false, "Email відправлено");
        }
        catch (Exception ex) { return new NotificationResult(false, false, ex.Message); }
    }
}
