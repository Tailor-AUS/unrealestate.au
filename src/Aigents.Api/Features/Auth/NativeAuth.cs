// ═══════════════════════════════════════════════════════════════
// AUTH FEATURE - NATIVE (sovereign-from-day-1)
// ═══════════════════════════════════════════════════════════════
// ASP.NET Core Identity native flow: email + password, email
// confirmation, password reset, login alerts. No third-party IdP.
// ═══════════════════════════════════════════════════════════════

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Aigents.Domain.Entities;
using Aigents.Infrastructure.Services.Auth;
using Carter;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;

namespace Aigents.Api.Features.Auth;

public class AuthEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", Register).WithName("Register");
        group.MapPost("/confirm-email", ConfirmEmail).WithName("ConfirmEmail");
        group.MapPost("/resend-confirmation", ResendConfirmation).WithName("ResendConfirmation");
        group.MapPost("/login", Login).WithName("Login");
        group.MapPost("/forgot-password", ForgotPassword).WithName("ForgotPassword");
        group.MapPost("/reset-password", ResetPassword).WithName("ResetPassword");
        group.MapPost("/logout", Logout).RequireAuthorization().WithName("Logout");
        group.MapGet("/me", Me).RequireAuthorization().WithName("GetCurrentUser");
    }

    // ─── Register ───────────────────────────────────────────────

    public record RegisterRequest(string Email, string Password, string Name, string? Mode);

    private static async Task<IResult> Register(
        RegisterRequest request,
        UserManager<User> userManager,
        IEmailSender<User> emailSender,
        IConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return Results.BadRequest(new { error = "email_and_password_required" });

        var user = new User
        {
            Email = request.Email,
            UserName = request.Email,
            Name = request.Name ?? request.Email,
            PreferredMode = string.Equals(request.Mode, "sell", StringComparison.OrdinalIgnoreCase) ? AgentMode.Sell : AgentMode.Buy,
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return Results.BadRequest(new { errors = result.Errors.Select(e => new { e.Code, e.Description }) });

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var publicUrl = config["App:PublicUrl"] ?? "https://unrealestate.au";
        var link = $"{publicUrl}/Account/ConfirmEmail?userId={user.Id}&code={encoded}";
        await emailSender.SendConfirmationLinkAsync(user, request.Email, HtmlEncoder.Default.Encode(link));

        return Results.Ok(new { userId = user.Id, requiresConfirmation = true });
    }

    // ─── Confirm email ──────────────────────────────────────────

    public record ConfirmRequest(Guid UserId, string Code);

    private static async Task<IResult> ConfirmEmail(ConfirmRequest request, UserManager<User> userManager)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null) return Results.NotFound(new { error = "user_not_found" });

        var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Code));
        var result = await userManager.ConfirmEmailAsync(user, token);
        return result.Succeeded
            ? Results.Ok(new { confirmed = true })
            : Results.BadRequest(new { errors = result.Errors.Select(e => e.Description) });
    }

    public record ResendRequest(string Email);

    private static async Task<IResult> ResendConfirmation(
        ResendRequest request,
        UserManager<User> userManager,
        IEmailSender<User> emailSender,
        IConfiguration config)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        // Don't leak account existence.
        if (user is null || user.EmailConfirmed) return Results.Ok();

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var publicUrl = config["App:PublicUrl"] ?? "https://unrealestate.au";
        var link = $"{publicUrl}/Account/ConfirmEmail?userId={user.Id}&code={encoded}";
        await emailSender.SendConfirmationLinkAsync(user, request.Email, HtmlEncoder.Default.Encode(link));
        return Results.Ok();
    }

    // ─── Login ──────────────────────────────────────────────────

    public record LoginRequest(string Email, string Password);

    public record LoginResponse(string AccessToken, DateTime ExpiresAt, UserDto User);

    private static async Task<IResult> Login(
        LoginRequest request,
        UserManager<User> userManager,
        ILoginAlertService alerts,
        IConfiguration config,
        HttpContext http)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null) return Results.Unauthorized();

        if (!user.EmailConfirmed)
            return Results.Json(new { error = "email_not_confirmed" }, statusCode: 403);

        if (await userManager.IsLockedOutAsync(user))
            return Results.Json(new { error = "account_locked" }, statusCode: 423);

        var ok = await userManager.CheckPasswordAsync(user, request.Password);
        if (!ok)
        {
            await userManager.AccessFailedAsync(user);
            return Results.Unauthorized();
        }

        await userManager.ResetAccessFailedCountAsync(user);

        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var ua = http.Request.Headers.UserAgent.ToString();
        await alerts.RecordLoginAsync(user, ip, ua);

        var (token, expires) = IssueJwt(user, config);
        return Results.Ok(new LoginResponse(token, expires, ToDto(user)));
    }

    // ─── Forgot / reset password ────────────────────────────────

    public record ForgotPasswordRequest(string Email);

    private static async Task<IResult> ForgotPassword(
        ForgotPasswordRequest request,
        UserManager<User> userManager,
        IEmailSender<User> emailSender,
        IConfiguration config)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        // Always 200 — don't leak account existence.
        if (user is null || !user.EmailConfirmed) return Results.Ok();

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var publicUrl = config["App:PublicUrl"] ?? "https://unrealestate.au";
        var link = $"{publicUrl}/Account/ResetPassword?userId={user.Id}&code={encoded}";
        await emailSender.SendPasswordResetLinkAsync(user, request.Email, HtmlEncoder.Default.Encode(link));
        return Results.Ok();
    }

    public record ResetPasswordRequest(Guid UserId, string Code, string NewPassword);

    private static async Task<IResult> ResetPassword(ResetPasswordRequest request, UserManager<User> userManager)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null) return Results.NotFound(new { error = "user_not_found" });

        var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Code));
        var result = await userManager.ResetPasswordAsync(user, token, request.NewPassword);
        return result.Succeeded
            ? Results.Ok()
            : Results.BadRequest(new { errors = result.Errors.Select(e => e.Description) });
    }

    // ─── Logout / me ────────────────────────────────────────────

    private static IResult Logout() => Results.Ok();

    private static async Task<IResult> Me(ClaimsPrincipal principal, UserManager<User> userManager)
    {
        var idClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (idClaim is null) return Results.Unauthorized();
        var user = await userManager.FindByIdAsync(idClaim);
        return user is null ? Results.NotFound() : Results.Ok(ToDto(user));
    }

    // ─── Helpers ────────────────────────────────────────────────

    public record UserDto(Guid Id, string Name, string Email, string? AvatarUrl, string Mode, bool EmailConfirmed);

    private static UserDto ToDto(User u) => new(
        u.Id, u.Name, u.Email ?? string.Empty, u.AvatarUrl, u.PreferredMode.ToString(), u.EmailConfirmed);

    private static (string token, DateTime expiresAt) IssueJwt(User user, IConfiguration config)
    {
        var secret = config["Jwt:Secret"] ?? throw new InvalidOperationException("Jwt:Secret not configured");
        var issuer = config["Jwt:Issuer"] ?? "unrealestate.au";
        var audience = config["Jwt:Audience"] ?? "unrealestate.au";
        var expires = DateTime.UtcNow.AddDays(30);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim("mode", user.PreferredMode.ToString()),
        };

        var jwt = new JwtSecurityToken(issuer, audience, claims, expires: expires, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }
}
