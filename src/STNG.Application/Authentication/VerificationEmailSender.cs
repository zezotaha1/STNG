using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Emailing;

namespace STNG.Authentication;

public class VerificationEmailSender : ITransientDependency
{
    private readonly IEmailSender _sender;
    private readonly ILogger<VerificationEmailSender> _logger;
    public VerificationEmailSender(IEmailSender sender, ILogger<VerificationEmailSender> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task SendAsync(string email, string code)
    {
        try
        {
            await _sender.SendAsync(email, "Verify your STNG email", $"Your STNG verification code is {code}.", false);
        }
        catch (Exception)
        {
            // The database transaction already committed. Resend is the recovery path.
            // SMTP exception details can contain message content or credentials.
            _logger.LogWarning("Verification email delivery failed; the account can request a new code.");
        }
    }
}
