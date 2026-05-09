using System.Text;
using Aigents.Api.Common;
using Aigents.Api.Features.Crm;
using Aigents.Api.Features.Calls;
using Aigents.Api.Features.Contacts;
using Aigents.Api.Features.Inspections;
using Aigents.Api.Features.VoiceNotes;
using Aigents.Api.Features.Property;
using Aigents.Api.Features.Buyer;
using Aigents.Api.Features.Seller;
using Aigents.Infrastructure.Data;
using Aigents.Infrastructure.Services.AI;
using Aigents.Infrastructure.Services.Auth;
using Aigents.Infrastructure.CrmIntegration;
using Aigents.Infrastructure.PropertyData;
using Carter;
using Fido2NetLib;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

// Expand FOO_FILE env vars: read the file path, inject the file contents as FOO.
// Lets AloomU (and any Docker secrets setup) pass secrets via mounted files
// without any app-specific secret-reading code in each feature.
foreach (var key in Environment.GetEnvironmentVariables().Keys.Cast<string>().ToList())
{
    if (!key.EndsWith("_FILE")) continue;
    var filePath = Environment.GetEnvironmentVariable(key);
    if (filePath is null || !File.Exists(filePath)) continue;
    var value = File.ReadAllText(filePath).Trim();
    Environment.SetEnvironmentVariable(key[..^5], value); // strip _FILE suffix
}

var builder = WebApplication.CreateBuilder(args);

// ───────────────────────────────────────────────────────────────
// ASPIRE SERVICE DEFAULTS
// ───────────────────────────────────────────────────────────────

builder.AddServiceDefaults();

// ───────────────────────────────────────────────────────────────
// DATABASE
// ───────────────────────────────────────────────────────────────

builder.AddNpgsqlDbContext<AigentsDbContext>("unrealestate");

// ───────────────────────────────────────────────────────────────
// REDIS CACHE
// ───────────────────────────────────────────────────────────────

builder.AddRedisDistributedCache("redis");

// ───────────────────────────────────────────────────────────────
// AI SERVICES
// ───────────────────────────────────────────────────────────────

builder.Services.Configure<AzureAiOptions>(
    builder.Configuration.GetSection(AzureAiOptions.SectionName));
builder.Services.AddScoped<IAiService, AzureAiService>();
builder.Services.AddScoped<ICallIntelligenceService, CallIntelligenceService>();

// ───────────────────────────────────────────────────────────────
// CRM INTEGRATION
// ───────────────────────────────────────────────────────────────

builder.Services.AddCrmIntegration();

// ───────────────────────────────────────────────────────────────
// BUYER DATA INTEGRATION
// ───────────────────────────────────────────────────────────────

builder.Services.AddPropertyDataServices();

// ───────────────────────────────────────────────────────────────
// MEDIATR + VALIDATION
// ───────────────────────────────────────────────────────────────

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
});

builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

// ───────────────────────────────────────────────────────────────
// CARTER (ENDPOINTS)
// ───────────────────────────────────────────────────────────────

builder.Services.AddCarter();

// ───────────────────────────────────────────────────────────────
// IDENTITY + AUTH (sovereign-from-day-1: email + password, optional passkey)
// ───────────────────────────────────────────────────────────────

builder.Services.AddAigentsAuthCore(builder.Configuration);
builder.Services.AddAigentsIdentityCore();

var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "dev-secret-change-me-min-32-bytes-please";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "unrealestate.au";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "unrealestate.au";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        };
    });

builder.Services.AddAuthorization();

// ───────────────────────────────────────────────────────────────
// FIDO2 / WEBAUTHN (passkeys — optional parallel auth)
// ───────────────────────────────────────────────────────────────

builder.Services.Configure<Fido2Configuration>(opts =>
{
    opts.ServerDomain = builder.Configuration["Fido2:ServerDomain"] ?? "unrealestate.au";
    opts.ServerName = builder.Configuration["Fido2:ServerName"] ?? "unrealestate.au";
    opts.Origins = (builder.Configuration["Fido2:Origins"] ?? "https://unrealestate.au")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet();
    opts.TimestampDriftTolerance = 300_000;
});
builder.Services.AddSingleton<IFido2>(sp =>
    new Fido2(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Fido2Configuration>>().Value));

// ───────────────────────────────────────────────────────────────
// CORS
// ───────────────────────────────────────────────────────────────

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

// ───────────────────────────────────────────────────────────────
// SWAGGER
// ───────────────────────────────────────────────────────────────

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Aigents API", Version = "v1" });
});

var app = builder.Build();

// ───────────────────────────────────────────────────────────────
// MIDDLEWARE
// ───────────────────────────────────────────────────────────────

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// ───────────────────────────────────────────────────────────────
// ENDPOINTS
// ───────────────────────────────────────────────────────────────

app.MapDefaultEndpoints(); // Health checks — maps /health and /alive
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", ts = DateTime.UtcNow })); // AloomU uptime probe
app.MapCarter(); // Feature endpoints (Carter modules) — includes /api/auth and /api/auth/passkey

// Agent Mobile API Endpoints
app.MapCrmEndpoints();
app.MapCallEndpoints();
app.MapContactEndpoints();
app.MapInspectionEndpoints();
app.MapVoiceNoteEndpoints();

// Buyer API Endpoints
app.MapPropertyEndpoints();
app.MapBuyerEndpoints();

// Seller API Endpoints
app.MapSellerEndpoints();

// QLD Property Maps & Reports API
app.MapMapsOnlineEndpoints();

// Map Proxy (CORS bypass for QLD WMS)
app.MapMapProxyEndpoints();

// ───────────────────────────────────────────────────────────────
// DATABASE INITIALIZATION (with retry for container startup)
// ───────────────────────────────────────────────────────────────

var maxRetries = 10;
var retryDelay = TimeSpan.FromSeconds(5);

for (int i = 0; i < maxRetries; i++)
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AigentsDbContext>();

        // Apply migrations
        await db.Database.MigrateAsync();
        Console.WriteLine("✅ Database migrations applied successfully");
        break;
    }
    catch (Exception ex) when (i < maxRetries - 1)
    {
        Console.WriteLine($"⏳ Database not ready (attempt {i + 1}/{maxRetries}): {ex.Message}");
        await Task.Delay(retryDelay);
    }
}

app.Run();
