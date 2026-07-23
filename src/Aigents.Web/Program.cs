// ═══════════════════════════════════════════════════════════════
// unrealestate.au — Blazor Server frontend
// ═══════════════════════════════════════════════════════════════

using Aigents.Domain.Entities;
using Aigents.Infrastructure.Data;
using Aigents.Infrastructure.Growth;
using Aigents.Infrastructure.Services.Auth;
using Aigents.Infrastructure.Services.AI;
using Aigents.Infrastructure.Services.Chat;
using Aigents.Web.Components;
using Aigents.Web.Components.Account;
using Aigents.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Xml.Linq;

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
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IProductEventRecorder, ProductEventRecorder>();
builder.Services.Configure<AzureAiOptions>(
    builder.Configuration.GetSection(AzureAiOptions.SectionName));
builder.Services.AddScoped<IAiService, AzureAiService>();
builder.Services.AddScoped<IChatConversationService, ChatConversationService>();

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
// AloomU uptime probe. Accepts both GET and HEAD so wget-style probes that
// issue HEAD don't get a 405 (which led to a healthcheck false-negative on
// first deploy — the app was fine, the probe was lying).
app.MapMethods("/healthz", new[] { "GET", "HEAD" }, () => Results.Ok(new { status = "healthy", ts = DateTime.UtcNow }));

var publicBaseUrl = (builder.Configuration["App:PublicUrl"] ?? "https://unrealestate.au")
    .TrimEnd('/');

app.MapGet("/robots.txt", () => Results.Text(
    $"User-agent: *\nAllow: /\n"
    + "Disallow: /Account/\n"
    + "Disallow: /my-listings\n"
    + "Disallow: /my-offers\n"
    + "Disallow: /seller/\n"
    + "Disallow: /listing-created\n"
    + $"Sitemap: {publicBaseUrl}/sitemap.xml\n",
    "text/plain; charset=utf-8"));

app.MapGet("/sitemap.xml", async (
    AigentsDbContext db,
    CancellationToken cancellationToken) =>
{
    var listings = await db.Listings
        .AsNoTracking()
        .Where(listing => listing.Status == ListingStatus.Active)
        .OrderByDescending(listing => listing.UpdatedAt)
        .Select(listing => new { listing.Id, listing.UpdatedAt })
        .ToListAsync(cancellationToken);

    XNamespace sitemap = "http://www.sitemaps.org/schemas/sitemap/0.9";
    var staticUrls = new[] { "", "/explore", "/list", "/agents" };
    var urlElements = staticUrls.Select(path =>
        new XElement(sitemap + "url",
            new XElement(sitemap + "loc", $"{publicBaseUrl}{path}")));

    var listingElements = listings.Select(listing =>
        new XElement(sitemap + "url",
            new XElement(sitemap + "loc", $"{publicBaseUrl}/property/{listing.Id}"),
            new XElement(
                sitemap + "lastmod",
                listing.UpdatedAt.ToUniversalTime().ToString("yyyy-MM-dd"))));

    var document = new XDocument(
        new XElement(sitemap + "urlset", urlElements.Concat(listingElements)));

    return Results.Text(
        document.ToString(SaveOptions.DisableFormatting),
        "application/xml; charset=utf-8");
}).CacheOutput(policy => policy.Expire(TimeSpan.FromMinutes(15)));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Identity helper endpoints (sign-out, etc.).
app.MapAdditionalIdentityEndpoints();

app.Run();
