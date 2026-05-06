// ═══════════════════════════════════════════════════════════════
// unrealestate.au — Blazor Server frontend
// ═══════════════════════════════════════════════════════════════

// Expand FOO_FILE env vars (Docker secrets convention used on AloomU).
foreach (var key in Environment.GetEnvironmentVariables().Keys.Cast<string>().ToList())
{
    if (!key.EndsWith("_FILE")) continue;
    var filePath = Environment.GetEnvironmentVariable(key);
    if (filePath is null || !File.Exists(filePath)) continue;
    Environment.SetEnvironmentVariable(key[..^5], File.ReadAllText(filePath).Trim());
}

using Aigents.Infrastructure.Data;
using Aigents.Web.Components;
using Aigents.Web.Services;

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
    // In a real scenario, this might point to a specific scraper service or use a proxy
    client.Timeout = TimeSpan.FromSeconds(30);
    // client.BaseAddress = ... 
});

// ───────────────────────────────────────────────────────────────
// AUTHENTICATION (optional - requires Google credentials)
// ───────────────────────────────────────────────────────────────

var googleClientId = builder.Configuration["Google:ClientId"];
var googleClientSecret = builder.Configuration["Google:ClientSecret"];

if (!string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret))
{
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = "Cookies";
        options.DefaultChallengeScheme = "Google";
    })
    .AddCookie("Cookies")
    .AddGoogle("Google", options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        options.CallbackPath = "/signin-google";
    });
    
    builder.Services.AddCascadingAuthenticationState();
}

builder.Services.AddScoped<ISiteContext, SiteContext>();

var app = builder.Build();

// ───────────────────────────────────────────────────────────────
// MIDDLEWARE
// ───────────────────────────────────────────────────────────────

// Note: HTTPS redirect disabled for local Aspire development
// Enable in production with proper HTTPS configuration
app.MapStaticAssets();
app.UseAntiforgery();
app.UseOutputCache();

// ───────────────────────────────────────────────────────────────
// ENDPOINTS
// ───────────────────────────────────────────────────────────────

app.MapDefaultEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
