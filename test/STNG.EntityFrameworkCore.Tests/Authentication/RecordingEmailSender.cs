using System;
using System.Collections.Generic;
using System.Net.Mail;
using System.Threading.Tasks;
using Volo.Abp.Emailing;

namespace STNG.Authentication;

public class RecordingEmailSender : IEmailSender
{
    public List<(string Email, string Body)> Messages { get; } = new();
    public bool FailDelivery { get; set; }
    public Task SendAsync(string to, string? subject, string? body, bool isBodyHtml = true, AdditionalEmailSendingArgs? additionalEmailSendingArgs = null)
    {
        if (FailDelivery) throw new InvalidOperationException("Simulated delivery failure");
        Messages.Add((to, body ?? ""));
        return Task.CompletedTask;
    }
    public Task SendAsync(string from, string to, string? subject, string? body, bool isBodyHtml = true, AdditionalEmailSendingArgs? additionalEmailSendingArgs = null) => SendAsync(to, subject, body, isBodyHtml, additionalEmailSendingArgs);
    public Task SendAsync(MailMessage mail, bool normalize = true) => SendAsync(mail.To.ToString(), mail.Subject, mail.Body);
    public Task QueueAsync(string to, string subject, string body, bool isBodyHtml = true, AdditionalEmailSendingArgs? additionalEmailSendingArgs = null) => SendAsync(to, subject, body, isBodyHtml, additionalEmailSendingArgs);
    public Task QueueAsync(string from, string to, string subject, string body, bool isBodyHtml = true, AdditionalEmailSendingArgs? additionalEmailSendingArgs = null) => SendAsync(to, subject, body, isBodyHtml, additionalEmailSendingArgs);
}
