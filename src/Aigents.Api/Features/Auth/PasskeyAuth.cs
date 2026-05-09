// ═══════════════════════════════════════════════════════════════
// PASSKEY (WebAuthn) FEATURE
// ═══════════════════════════════════════════════════════════════
// Parallel sovereign auth path. Identity native (email + password)
// remains the default at launch; passkeys are an opt-in registration
// the user can add from the account page once logged in.
// ═══════════════════════════════════════════════════════════════

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Aigents.Domain.Entities;
using Aigents.Infrastructure.Data;
using Aigents.Infrastructure.Services.Auth;
using Carter;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.IdentityModel.Tokens;

namespace Aigents.Api.Features.Auth;

public class PasskeyEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth/passkey").WithTags("Auth");

        group.MapPost("/register/options", RegisterOptions).RequireAuthorization();
        group.MapPost("/register/complete", RegisterComplete).RequireAuthorization();
        group.MapPost("/login/options", LoginOptions);
        group.MapPost("/login/complete", LoginComplete);
        group.MapGet("/list", ListCredentials).RequireAuthorization();
        group.MapDelete("/{id:guid}", DeleteCredential).RequireAuthorization();
    }

    // ─── Registration ───────────────────────────────────────────

    public record RegisterOptionsRequest(string? Nickname);

    [Authorize]
    private static async Task<IResult> RegisterOptions(
        RegisterOptionsRequest request,
        ClaimsPrincipal principal,
        UserManager<User> userManager,
        IFido2 fido2,
        AigentsDbContext db,
        IDistributedCache cache)
    {
        var idClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (idClaim is null) return Results.Unauthorized();
        var user = await userManager.FindByIdAsync(idClaim);
        if (user is null) return Results.Unauthorized();

        var existing = await db.Fido2Credentials
            .Where(c => c.UserId == user.Id)
            .Select(c => new PublicKeyCredentialDescriptor(c.CredentialId))
            .ToListAsync();

        var fidoUser = new Fido2User
        {
            Id = user.Id.ToByteArray(),
            Name = user.Email ?? user.UserName ?? user.Id.ToString(),
            DisplayName = user.Name,
        };

        var options = fido2.RequestNewCredential(
            fidoUser,
            existing,
            new AuthenticatorSelection
            {
                RequireResidentKey = false,
                UserVerification = UserVerificationRequirement.Preferred,
            },
            AttestationConveyancePreference.None);

        var key = $"passkey:reg:{user.Id}";
        await cache.SetStringAsync(key, options.ToJson(), new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        });

        return Results.Content(options.ToJson(), "application/json");
    }

    public record RegisterCompleteRequest(AuthenticatorAttestationRawResponse Response, string? Nickname);

    [Authorize]
    private static async Task<IResult> RegisterComplete(
        RegisterCompleteRequest request,
        ClaimsPrincipal principal,
        UserManager<User> userManager,
        IFido2 fido2,
        AigentsDbContext db,
        IDistributedCache cache,
        ILoginAlertService alerts)
    {
        var idClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (idClaim is null) return Results.Unauthorized();
        var user = await userManager.FindByIdAsync(idClaim);
        if (user is null) return Results.Unauthorized();

        var key = $"passkey:reg:{user.Id}";
        var optionsJson = await cache.GetStringAsync(key);
        if (optionsJson is null) return Results.BadRequest(new { error = "challenge_expired" });
        var options = CredentialCreateOptions.FromJson(optionsJson);

        IsCredentialIdUniqueToUserAsyncDelegate uniqueCheck = async (args, ct) =>
            !await db.Fido2Credentials.AnyAsync(c => c.CredentialId == args.CredentialId, ct);

        var result = await fido2.MakeNewCredentialAsync(request.Response, options, uniqueCheck);
        await cache.RemoveAsync(key);

        if (result.Result is null)
            return Results.BadRequest(new { error = "attestation_failed", detail = result.ErrorMessage });

        var credential = new Fido2Credential
        {
            UserId = user.Id,
            CredentialId = result.Result.CredentialId,
            PublicKey = result.Result.PublicKey,
            UserHandle = result.Result.User.Id,
            SignatureCounter = result.Result.Counter,
            CredType = result.Result.CredType,
            AaGuid = result.Result.Aaguid,
            Nickname = string.IsNullOrWhiteSpace(request.Nickname) ? "Passkey" : request.Nickname.Trim(),
        };

        db.Fido2Credentials.Add(credential);
        await db.SaveChangesAsync();

        await alerts.NotifyPasskeyRegisteredAsync(user, credential.Nickname ?? "Passkey");

        return Results.Ok(new { id = credential.Id, nickname = credential.Nickname });
    }

    // ─── Login (assertion) ──────────────────────────────────────

    public record LoginOptionsRequest(string Email);

    private static async Task<IResult> LoginOptions(
        LoginOptionsRequest request,
        UserManager<User> userManager,
        IFido2 fido2,
        AigentsDbContext db,
        IDistributedCache cache)
    {
        // Allow username-less / discoverable-credential flow if email is empty.
        var existingDescriptors = new List<PublicKeyCredentialDescriptor>();
        Guid? userId = null;

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var user = await userManager.FindByEmailAsync(request.Email);
            if (user is not null)
            {
                userId = user.Id;
                existingDescriptors = await db.Fido2Credentials
                    .Where(c => c.UserId == user.Id)
                    .Select(c => new PublicKeyCredentialDescriptor(c.CredentialId))
                    .ToListAsync();
            }
        }

        var options = fido2.GetAssertionOptions(
            existingDescriptors,
            UserVerificationRequirement.Preferred);

        var cacheKey = $"passkey:login:{Guid.NewGuid():N}";
        await cache.SetStringAsync(cacheKey, options.ToJson(), new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        });

        return Results.Ok(new
        {
            sessionId = cacheKey,
            options = JsonDocument.Parse(options.ToJson()).RootElement,
        });
    }

    public record LoginCompleteRequest(string SessionId, AuthenticatorAssertionRawResponse Response);

    private static async Task<IResult> LoginComplete(
        LoginCompleteRequest request,
        UserManager<User> userManager,
        IFido2 fido2,
        AigentsDbContext db,
        IDistributedCache cache,
        ILoginAlertService alerts,
        IConfiguration config,
        HttpContext http)
    {
        var optionsJson = await cache.GetStringAsync(request.SessionId);
        if (optionsJson is null) return Results.BadRequest(new { error = "challenge_expired" });
        var options = AssertionOptions.FromJson(optionsJson);

        var credential = await db.Fido2Credentials
            .FirstOrDefaultAsync(c => c.CredentialId == request.Response.Id);
        if (credential is null) return Results.Unauthorized();

        var user = await userManager.FindByIdAsync(credential.UserId.ToString());
        if (user is null) return Results.Unauthorized();

        IsUserHandleOwnerOfCredentialIdAsync ownsCredential = async (args, ct) =>
            await db.Fido2Credentials.AnyAsync(c =>
                c.CredentialId == args.CredentialId &&
                c.UserHandle == args.UserHandle, ct);

        var verify = await fido2.MakeAssertionAsync(
            request.Response,
            options,
            credential.PublicKey,
            credential.SignatureCounter,
            ownsCredential);

        await cache.RemoveAsync(request.SessionId);

        credential.SignatureCounter = verify.Counter;
        credential.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var ua = http.Request.Headers.UserAgent.ToString();
        await alerts.RecordLoginAsync(user, ip, ua);

        var (token, expires) = IssueJwt(user, config);
        return Results.Ok(new
        {
            accessToken = token,
            expiresAt = expires,
            user = new { user.Id, user.Name, user.Email, mode = user.PreferredMode.ToString() },
        });
    }

    // ─── List / delete ──────────────────────────────────────────

    [Authorize]
    private static async Task<IResult> ListCredentials(
        ClaimsPrincipal principal,
        UserManager<User> userManager,
        AigentsDbContext db)
    {
        var idClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (idClaim is null) return Results.Unauthorized();
        var userId = Guid.Parse(idClaim);

        var creds = await db.Fido2Credentials
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new { c.Id, c.Nickname, c.CreatedAt, c.LastUsedAt })
            .ToListAsync();

        return Results.Ok(creds);
    }

    [Authorize]
    private static async Task<IResult> DeleteCredential(
        Guid id,
        ClaimsPrincipal principal,
        AigentsDbContext db)
    {
        var idClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (idClaim is null) return Results.Unauthorized();
        var userId = Guid.Parse(idClaim);

        var rows = await db.Fido2Credentials
            .Where(c => c.Id == id && c.UserId == userId)
            .ExecuteDeleteAsync();

        return rows > 0 ? Results.NoContent() : Results.NotFound();
    }

    // ─── Helpers ────────────────────────────────────────────────

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
            new Claim("amr", "passkey"),
        };

        var jwt = new JwtSecurityToken(issuer, audience, claims, expires: expires, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }
}
