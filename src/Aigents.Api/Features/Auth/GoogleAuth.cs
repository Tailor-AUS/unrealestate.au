// ═══════════════════════════════════════════════════════════════
// AUTH FEATURE - VERTICAL SLICE
// ═══════════════════════════════════════════════════════════════
// Handles Google OAuth and user creation/lookup.
// ═══════════════════════════════════════════════════════════════

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Aigents.Domain.Entities;
using Aigents.Infrastructure.Data;
using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Aigents.Api.Features.Auth;

// ───────────────────────────────────────────────────────────────
// ENDPOINTS
// ───────────────────────────────────────────────────────────────

public class AuthEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/google", async (GoogleAuthRequest request, ISender sender) =>
        {
            var result = await sender.Send(new GoogleAuthCommand(
                request.IdToken,
                request.Mode
            ));
            
            return Results.Ok(result);
        })
        .WithName("GoogleAuth")
        .WithOpenApi()
        .Produces<AuthResponse>();

        app.MapGet("/api/auth/me", async (ClaimsPrincipal user, ISender sender) =>
        {
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var result = await sender.Send(new GetUserQuery(Guid.Parse(userId)));
            return result is not null ? Results.Ok(result) : Results.NotFound();
        })
        .WithName("GetCurrentUser")
        .WithOpenApi()
        .RequireAuthorization()
        .Produces<UserDto>();
    }
}

// ───────────────────────────────────────────────────────────────
// GOOGLE AUTH
// ───────────────────────────────────────────────────────────────

public record GoogleAuthRequest(string IdToken, string Mode);

public record AuthResponse(
    string AccessToken,
    UserDto User
);

public record UserDto(
    Guid Id,
    string Name,
    string Email,
    string? AvatarUrl,
    string Mode
);

public record GoogleAuthCommand(string IdToken, string Mode) : IRequest<AuthResponse>;

public class GoogleAuthHandler : IRequestHandler<GoogleAuthCommand, AuthResponse>
{
    private readonly AigentsDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<GoogleAuthHandler> _logger;
    private readonly HttpClient _httpClient;

    public GoogleAuthHandler(
        AigentsDbContext db,
        IConfiguration config,
        ILogger<GoogleAuthHandler> logger,
        IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _config = config;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
    }

    public async Task<AuthResponse> Handle(GoogleAuthCommand request, CancellationToken ct)
    {
        // Verify Google token
        var googleUser = await VerifyGoogleTokenAsync(request.IdToken, ct);
        
        // Find or create user
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Provider == "google" && u.ProviderId == googleUser.Sub, ct);

        if (user is null)
        {
            user = new User
            {
                Email = googleUser.Email,
                Name = googleUser.Name,
                AvatarUrl = googleUser.Picture,
                Provider = "google",
                ProviderId = googleUser.Sub,
                PreferredMode = request.Mode.ToLower() == "sell" ? AgentMode.Sell : AgentMode.Buy
            };
            
            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);
            
            _logger.LogInformation("New user created: {Email}", user.Email);
        }
        else
        {
            user.LastActiveAt = DateTime.UtcNow;
            user.AvatarUrl = googleUser.Picture; // Update in case it changed
            await _db.SaveChangesAsync(ct);
        }

        // Generate JWT
        var token = GenerateJwtToken(user);

        return new AuthResponse(
            token,
            new UserDto(user.Id, user.Name, user.Email, user.AvatarUrl, user.PreferredMode.ToString())
        );
    }

    private async Task<GoogleUserInfo> VerifyGoogleTokenAsync(string idToken, CancellationToken ct)
    {
        var response = await _httpClient.GetAsync(
            $"https://oauth2.googleapis.com/tokeninfo?id_token={idToken}", ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Invalid Google token");

        var content = await response.Content.ReadFromJsonAsync<GoogleUserInfo>(ct);
        return content ?? throw new InvalidOperationException("Failed to parse Google response");
    }

    private string GenerateJwtToken(User user)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:Secret"] ?? "your-256-bit-secret-key-here-min-32-chars"));
        
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim("mode", user.PreferredMode.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"] ?? "unrealestate.au",
            audience: _config["Jwt:Audience"] ?? "unrealestate.au",
            claims: claims,
            expires: DateTime.UtcNow.AddDays(30),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public record GoogleUserInfo(
    string Sub,
    string Email,
    string Name,
    string Picture
);

// ───────────────────────────────────────────────────────────────
// GET USER QUERY
// ───────────────────────────────────────────────────────────────

public record GetUserQuery(Guid UserId) : IRequest<UserDto?>;

public class GetUserHandler : IRequestHandler<GetUserQuery, UserDto?>
{
    private readonly AigentsDbContext _db;

    public GetUserHandler(AigentsDbContext db)
    {
        _db = db;
    }

    public async Task<UserDto?> Handle(GetUserQuery request, CancellationToken ct)
    {
        var user = await _db.Users.FindAsync([request.UserId], ct);
        
        return user is null ? null : new UserDto(
            user.Id,
            user.Name,
            user.Email,
            user.AvatarUrl,
            user.PreferredMode.ToString()
        );
    }
}
