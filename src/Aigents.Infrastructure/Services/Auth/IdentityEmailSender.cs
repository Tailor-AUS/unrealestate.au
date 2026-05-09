using Aigents.Domain.Entities;
using Aigents.Infrastructure.Services.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Aigents.Infrastructure.Services.Auth;

/// <summary>
/// Wires the ASP.NET Core Identity transactional emails (confirmation, reset
/// password, change email) through MailKit + AloomU SMTP.
/// </summary>
public class IdentityEmailSender : IEmailSender<User>
{
    private readonly ITransactionalEmailSender _smtp;
    private readonly ILogger<IdentityEmailSender> _logger;

    public IdentityEmailSender(ITransactionalEmailSender smtp, ILogger<IdentityEmailSender> logger)
    {
        _smtp = smtp;
        _logger = logger;
    }

    public Task SendConfirmationLinkAsync(User user, string email, string confirmationLink)
    {
        _logger.LogDebug("Sending email confirmation to {Email}", email);
        var t = AuthEmailTemplates.ConfirmEmail(user.Name, confirmationLink);
        return Send(email, user.Name, t);
    }

    public Task SendPasswordResetLinkAsync(User user, string email, string resetLink)
    {
        _logger.LogDebug("Sending password-reset link to {Email}", email);
        var t = AuthEmailTemplates.ResetPassword(user.Name, resetLink);
        return Send(email, user.Name, t);
    }

    public Task SendPasswordResetCodeAsync(User user, string email, string resetCode)
    {
        // Stage 0 prefers link-based reset flow. If a code-based UI is added later,
        // wrap the code in a small inline template here.
        var url = $"https://unrealestate.au/Account/ResetPassword?code={Uri.EscapeDataString(resetCode)}&email={Uri.EscapeDataString(email)}";
        var t = AuthEmailTemplates.ResetPassword(user.Name, url);
        return Send(email, user.Name, t);
    }

    private Task Send(string email, string name, (string Subject, string Html, string Text) tmpl) =>
        _smtp.SendAsync(email, name, tmpl.Subject, tmpl.Html, tmpl.Text);
}
