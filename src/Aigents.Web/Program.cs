// ═══════════════════════════════════════════════════════════════
// unrealestate.au — Blazor Server frontend
// ═══════════════════════════════════════════════════════════════

using Aigents.Domain.Entities;
using Aigents.Infrastructure.Data;
using Aigents.Infrastructure.Services.Auth;
using Aigents.Web.Components;
using Aigents.Web.Components.Account;
using Aigents.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;

// Expand FOO_FILE env vars (Docker secrets convention used on AloomU).
foreach (var key in Environment.GetEnvironmentVariables().Keys.Cast<string>().ToList())
{
    if (!key.EndsWith("_FILE")) continue;
    var filePath = Environment.GetEnvironmentVariable(key);
    if (filePath is null || !File.Exists(filePath)) continue;
    Environment.SetEnvironmentVariable(key[..^5], File.ReadAllText(filePath).Trim());
}

var builder = WebApplication.CreateBuilder(args);

// ───────────────────────────────────────────────────────────────
// ASPIRE SERVICE DEFAULTS
// ───────────────────────────────────────────────────────────────

builder.AddServiceDefaults();

// Force static web assets to load (crucial for scoped CSS and JS when running via Aspire/dotnet run)
builder.WebHost.UseStaticWebAssets();

// ───────────────────────────────────────────────────────────────
// DATABASE
// ───────────────────────────────────────────────────────────────

builder.AddNpgsqlDbContext<AigentsDbContext>("unrealestate");

// ───────────────────────────────────────────────────────────────
// BLAZOR
// ───────────────────────────────────────────────────────────────

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();

// ───────────────────────────────────────────────────────────────
// OUTPUT CACHING (REDIS)
// ───────────────────────────────────────────────────────────────

builder.AddRedisOutputCache("redis");

// ───────────────────────────────────────────────────────────────
// API CLIENT
// ───────────────────────────────────────────────────────────────

// Aspire service discovery uses "https+http://api"; on AloomU (plain Docker)
// set API_BASE_URL=http://unrealestate-api:8080 in the compose env block.
var apiBaseUrl = builder.Configuration["API_BASE_URL"] ?? "https+http://api";
builder.Services.AddHttpClient("api", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
});

// Default HttpClient for Blazor components (used by CreateListing wizard)
builder.Services.AddScoped(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    return factory.CreateClient("api");
});

// ───────────────────────────────────────────────────────────────
// DOMAIN SERVICES
// ───────────────────────────────────────────────────────────────

// Database-backed listing service (Scoped for EF Core compatibility)
builder.Services.AddScoped<IListingService, ListingService>();

// Web Intelligence Service (Scrapes/Searches for property data)
builder.Services.AddHttpClient<Aigents.Infrastructure.PropertyData.IPropertyIntelligenceService, Aigents.Infrastructure.PropertyData.PropertyIntelligenceService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// ───────────────────────────────────────────────────────────────
// AUTHENTICATION — sovereign-from-day-1, no third-party IdP
// ───────────────────────────────────────────────────────────────

builder.Services.AddAigentsAuthCore(builder.Configuration);

builder.Services.AddIdentity<User, IdentityRole<Guid>>(options =>
    {
        options.SignIn.RequireConfirmedEmail = true;
        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<AigentsDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "unrealestate.auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
});

builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<ISiteContext, SiteContext>();

var app = builder.Build();

// ───────────────────────────────────────────────────────────────
// MIDDLEWARE
// ───────────────────────────────────────────────────────────────

app.MapStaticAssets();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();
app.UseOutputCache();

// ───────────────────────────────────────────────────────────────
// ENDPOINTS
// ───────────────────────────────────────────────────────────────

app.MapDefaultEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Identity helper endpoints (sign-out, etc.).
app.MapAdditionalIdentityEndpoints();

app.Run();
