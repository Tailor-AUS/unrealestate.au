// ═══════════════════════════════════════════════════════════════
// AIGENTS - ASPIRE APP HOST
// ═══════════════════════════════════════════════════════════════
// Orchestrates all services with distributed tracing, health checks,
// and service discovery.
// ═══════════════════════════════════════════════════════════════

var builder = DistributedApplication.CreateBuilder(args);

// ───────────────────────────────────────────────────────────────
// INFRASTRUCTURE
// ───────────────────────────────────────────────────────────────

var redis = builder.AddRedis("redis")
    .WithDataVolume("unrealestate-redis-data");

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("unrealestate-postgres-data");

var db = postgres.AddDatabase("unrealestate");

// ───────────────────────────────────────────────────────────────
// SECRETS / PARAMETERS
// ───────────────────────────────────────────────────────────────
// Sovereign-from-day-1: no third-party identity provider, no Google
// OAuth client. Auth is ASP.NET Core Identity native (email + password
// + optional WebAuthn passkey).

var azureAiEndpoint = builder.AddParameter("azure-ai-endpoint", secret: false);
var azureAiDeployment = builder.AddParameter("azure-ai-deployment", secret: false);
var smtpHost = builder.AddParameter("smtp-host", secret: false);
var smtpUsername = builder.AddParameter("smtp-username", secret: false);
var smtpPassword = builder.AddParameter("smtp-password", secret: true);
var jwtSecret = builder.AddParameter("jwt-secret", secret: true);
var googleMapsApiKey = builder.AddParameter("google-maps-api-key", secret: true);

// ───────────────────────────────────────────────────────────────
// API SERVICE
// ───────────────────────────────────────────────────────────────

var api = builder.AddProject<Projects.Aigents_Api>("api")
    .WithReference(redis)
    .WithReference(db)
    .WithEnvironment("AzureAI__Endpoint", azureAiEndpoint)
    .WithEnvironment("AzureAI__DeploymentName", azureAiDeployment)
    .WithEnvironment("Smtp__Host", smtpHost)
    .WithEnvironment("Smtp__Username", smtpUsername)
    .WithEnvironment("Smtp__Password", smtpPassword)
    .WithEnvironment("Jwt__Secret", jwtSecret)
    .WithHttpEndpoint(port: 5001, name: "http")
    .WithExternalHttpEndpoints();

// ───────────────────────────────────────────────────────────────
// WEB FRONTEND
// ───────────────────────────────────────────────────────────────

var web = builder.AddProject<Projects.Aigents_Web>("web")
    .WithReference(api)
    .WithReference(redis)
    .WithReference(db)
    .WithEnvironment("AzureAI__Endpoint", azureAiEndpoint)
    .WithEnvironment("AzureAI__DeploymentName", azureAiDeployment)
    .WithEnvironment("GoogleMaps__ApiKey", googleMapsApiKey)
    .WithEnvironment("Smtp__Host", smtpHost)
    .WithEnvironment("Smtp__Username", smtpUsername)
    .WithEnvironment("Smtp__Password", smtpPassword)
    .WithExternalHttpEndpoints();

// ───────────────────────────────────────────────────────────────
// BUILD & RUN
// ───────────────────────────────────────────────────────────────

await builder.Build().RunAsync();
