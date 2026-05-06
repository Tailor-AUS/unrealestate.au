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
// SECRETS (from environment/config)
// ───────────────────────────────────────────────────────────────

var azureAiEndpoint = builder.AddParameter("azure-ai-endpoint", secret: false);
var azureAiDeployment = builder.AddParameter("azure-ai-deployment", secret: false);
var googleClientId = builder.AddParameter("google-client-id", secret: true);
var googleClientSecret = builder.AddParameter("google-client-secret", secret: true);

// ───────────────────────────────────────────────────────────────
// API SERVICE
// ───────────────────────────────────────────────────────────────

var api = builder.AddProject<Projects.Aigents_Api>("api")
    .WithReference(redis)
    .WithReference(db)
    .WithEnvironment("AzureAI__Endpoint", azureAiEndpoint)
    .WithEnvironment("AzureAI__DeploymentName", azureAiDeployment)
    .WithEnvironment("Google__ClientId", googleClientId)
    .WithEnvironment("Google__ClientSecret", googleClientSecret)
    .WithHttpEndpoint(port: 5001, name: "http")
    .WithExternalHttpEndpoints();

// ───────────────────────────────────────────────────────────────
// WEB FRONTEND
// ───────────────────────────────────────────────────────────────

var web = builder.AddProject<Projects.Aigents_Web>("web")
    .WithReference(api)
    .WithReference(redis)
    .WithReference(db)
    .WithEnvironment("Google__ClientId", googleClientId)
    .WithEnvironment("Google__ClientSecret", googleClientSecret)
    .WithExternalHttpEndpoints();

// ───────────────────────────────────────────────────────────────
// BUILD & RUN
// ───────────────────────────────────────────────────────────────

await builder.Build().RunAsync();
