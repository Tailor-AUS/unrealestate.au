namespace Aigents.Infrastructure.Services.Email;

/// <summary>
/// Bound from the <c>Smtp</c> configuration section. On AloomU these values
/// arrive via env vars (Smtp__Host, Smtp__Port, ...) with the password coming
/// from /run/secrets/unrealestate_smtp_password through the <c>_FILE</c>
/// expansion in Program.cs.
/// </summary>
public class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = "noreply@unrealestate.au";
    public string FromName { get; set; } = "unrealestate.au";
    public bool UseStartTls { get; set; } = true;
}
