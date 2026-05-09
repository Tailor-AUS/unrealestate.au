namespace Aigents.Infrastructure.Services.Auth;

/// <summary>
/// Tiny HTML templates for the four transactional emails Identity needs.
/// Kept inline so we don't invent a templating subsystem just for four mails.
/// </summary>
internal static class AuthEmailTemplates
{
    private const string BrandColor = "#1a1a2e";
    private const string AccentColor = "#667eea";

    public static (string Subject, string Html, string Text) ConfirmEmail(string name, string confirmUrl) =>
    (
        "Confirm your unrealestate.au account",
        BaseHtml(
            heading: $"Welcome to unrealestate.au{(string.IsNullOrEmpty(name) ? string.Empty : $", {Esc(name)}")}.",
            body: "Please confirm your email address so we can finish setting up your account.",
            ctaLabel: "Confirm email",
            ctaUrl: confirmUrl,
            footer: "If you didn't create this account, you can safely ignore this email — nothing happens until the link is clicked."),
        $"Welcome to unrealestate.au.\n\nConfirm your email by visiting:\n{confirmUrl}\n\nIf you didn't create this account, ignore this email."
    );

    public static (string Subject, string Html, string Text) ResetPassword(string name, string resetUrl) =>
    (
        "Reset your unrealestate.au password",
        BaseHtml(
            heading: $"Password reset request",
            body: $"Hi{(string.IsNullOrEmpty(name) ? string.Empty : $" {Esc(name)}")}, click the button below to choose a new password. The link expires in 1 hour.",
            ctaLabel: "Reset password",
            ctaUrl: resetUrl,
            footer: "If you didn't request this, your password is unchanged. Tell us at admin@unrealestate.au if you're worried someone else is trying to access your account."),
        $"Reset your unrealestate.au password by visiting:\n{resetUrl}\n\nLink expires in 1 hour. If you didn't ask for this, ignore the email."
    );

    public static (string Subject, string Html, string Text) LoginAlert(string name, string ipAddress, string userAgent, DateTime whenUtc) =>
    (
        "New sign-in to your unrealestate.au account",
        BaseHtml(
            heading: "New sign-in",
            body: $"Hi{(string.IsNullOrEmpty(name) ? string.Empty : $" {Esc(name)}")}, we noticed a sign-in to your unrealestate.au account.<br><br>" +
                  $"<strong>When:</strong> {whenUtc:yyyy-MM-dd HH:mm} UTC<br>" +
                  $"<strong>From:</strong> {Esc(ipAddress)}<br>" +
                  $"<strong>Device:</strong> {Esc(Truncate(userAgent, 200))}<br><br>" +
                  "If that was you, no action needed.",
            ctaLabel: "Reset my password",
            ctaUrl: "https://unrealestate.au/Account/ForgotPassword",
            footer: "If you didn't sign in, reset your password immediately and email admin@unrealestate.au."),
        $"New sign-in to your unrealestate.au account.\n\nWhen: {whenUtc:yyyy-MM-dd HH:mm} UTC\nFrom: {ipAddress}\nDevice: {userAgent}\n\nIf that wasn't you, reset your password at https://unrealestate.au/Account/ForgotPassword"
    );

    public static (string Subject, string Html, string Text) PasskeyAdded(string name, string nickname, DateTime whenUtc) =>
    (
        "A new passkey was added to your unrealestate.au account",
        BaseHtml(
            heading: "Passkey registered",
            body: $"Hi{(string.IsNullOrEmpty(name) ? string.Empty : $" {Esc(name)}")}, a new passkey called <strong>{Esc(nickname)}</strong> was added to your account on {whenUtc:yyyy-MM-dd HH:mm} UTC. " +
                  "You can now sign in with this device's biometrics or PIN — no password needed.",
            ctaLabel: "Manage passkeys",
            ctaUrl: "https://unrealestate.au/Account/Passkeys",
            footer: "If you didn't add this passkey, sign in and remove it, then change your password."),
        $"A new passkey '{nickname}' was added to your unrealestate.au account at {whenUtc:yyyy-MM-dd HH:mm} UTC.\n\nManage at https://unrealestate.au/Account/Passkeys"
    );

    private static string BaseHtml(string heading, string body, string ctaLabel, string ctaUrl, string footer) => $@"<!DOCTYPE html>
<html lang=""en"">
<body style=""margin:0;padding:0;background:#f5f5f7;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif;color:#222"">
  <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f5f5f7;padding:40px 20px"">
    <tr><td align=""center"">
      <table role=""presentation"" width=""560"" cellpadding=""0"" cellspacing=""0"" style=""background:#fff;border-radius:14px;padding:40px;box-shadow:0 4px 14px rgba(0,0,0,.05)"">
        <tr><td style=""font-size:22px;font-weight:700;color:{BrandColor};padding-bottom:8px"">unreal<span style=""color:{AccentColor}"">.estate</span></td></tr>
        <tr><td style=""font-size:20px;font-weight:600;color:{BrandColor};padding:8px 0 16px"">{heading}</td></tr>
        <tr><td style=""font-size:15px;line-height:1.55;color:#3a3a52;padding-bottom:24px"">{body}</td></tr>
        <tr><td style=""padding:8px 0 32px""><a href=""{ctaUrl}"" style=""display:inline-block;background:{AccentColor};color:#fff;text-decoration:none;padding:12px 24px;border-radius:8px;font-weight:600"">{ctaLabel}</a></td></tr>
        <tr><td style=""font-size:13px;color:#888;line-height:1.5;border-top:1px solid #eee;padding-top:18px"">{footer}<br><br>unrealestate.au — Australia's free, sovereign property portal.</td></tr>
      </table>
    </td></tr>
  </table>
</body>
</html>";

    private static string Esc(string? s) =>
        string.IsNullOrEmpty(s) ? string.Empty : s
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty : s.Length <= max ? s : s[..max] + "…";
}
