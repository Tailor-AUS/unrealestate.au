using Aigents.Domain.Entities;
using Aigents.Infrastructure.Data;
using Aigents.Infrastructure.Services.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aigents.Infrastructure.Services.Auth;

public interface ILoginAlertService
{
    /// <summary>
    /// Records a successful sign-in for <paramref name="user"/> and, if the
    /// IP/user-agent looks new, fires off a courtesy alert email so the user
    /// can react to a stolen-credential scenario.
    /// </summary>
    Task RecordLoginAsync(User user, string ipAddress, string userAgent, CancellationToken ct = default);

    /// <summary>
    /// Sends the "passkey added" courtesy email so the user is alerted if a
    /// passkey was registered without their knowledge.
    /// </summary>
    Task NotifyPasskeyRegisteredAsync(User user, string nickname, CancellationToken ct = default);
}

public class LoginAlertService : ILoginAlertService
{
    private readonly AigentsDbContext _db;
    private readonly ITransactionalEmailSender _smtp;
    private readonly ILogger<LoginAlertService> _logger;

    public LoginAlertService(AigentsDbContext db, ITransactionalEmailSender smtp, ILogger<LoginAlertService> logger)
    {
        _db = db;
        _smtp = smtp;
        _logger = logger;
    }

    public async Task RecordLoginAsync(User user, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var isNewLocation =
            !string.Equals(user.LastLoginIp, ipAddress, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(user.LastLoginUserAgent, userAgent, StringComparison.OrdinalIgnoreCase);

        // Always update the on-record values.
        await _db.Users
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.LastLoginAt, now)
                .SetProperty(u => u.LastLoginIp, ipAddress)
                .SetProperty(u => u.LastLoginUserAgent, userAgent)
                .SetProperty(u => u.LastActiveAt, now), ct);

        if (string.IsNullOrEmpty(user.Email)) return;

        // Skip alert on the very first login (it's the registration confirmation
        // and the user just clicked through), but alert on any subsequent
        // login from a new IP/UA combination.
        if (user.LastLoginAt is null) return;
        if (!isNewLocation) return;

        var t = AuthEmailTemplates.LoginAlert(user.Name, ipAddress, userAgent, now);
        try
        {
            await _smtp.SendAsync(user.Email, user.Name, t.Subject, t.Html, t.Text, ct);
        }
        catch (Exception ex)
        {
            // Don't fail the login if SMTP is briefly down — just log.
            _logger.LogWarning(ex, "Failed to send login alert to {Email}", user.Email);
        }
    }

    public async Task NotifyPasskeyRegisteredAsync(User user, string nickname, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(user.Email)) return;

        var t = AuthEmailTemplates.PasskeyAdded(user.Name, nickname, DateTime.UtcNow);
        try
        {
            await _smtp.SendAsync(user.Email, user.Name, t.Subject, t.Html, t.Text, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send passkey-added alert to {Email}", user.Email);
        }
    }
}
