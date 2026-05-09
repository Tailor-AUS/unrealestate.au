namespace Aigents.Infrastructure.Services.Email;

/// <summary>
/// Lower-level email sender — sends a single message. Identity-specific
/// templating sits on top of this in <see cref="IdentityEmailSender"/>.
/// </summary>
public interface ITransactionalEmailSender
{
    Task SendAsync(string toEmail, string toName, string subject, string htmlBody, string? textBody = null, CancellationToken ct = default);
}
