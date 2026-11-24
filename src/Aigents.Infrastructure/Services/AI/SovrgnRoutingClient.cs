using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aigents.Infrastructure.Services.AI;

public class SovrgnAiOptions
{
    public const string SectionName = "Sovrgn";

    /// <summary>
    /// Tailor Routing / SOVRGN base URL (default: https://api.sovrgn.ai/v1).
    /// </summary>
    public string Endpoint { get; set; } = SovrgnRoutingClient.DefaultBaseUrl;

    /// <summary>
    /// Bearer API key minted on api.sovrgn.ai (SOVRGN_API_KEY or Sovrgn__ApiKey).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Routing tier or model name (default: tailor-fast).
    /// </summary>
    public string Model { get; set; } = SovrgnRoutingClient.DefaultModel;

    /// <summary>
    /// Tenant attribution header sent as X-Sovrgn-Tenant (default: aigents).
    /// </summary>
    public string Tenant { get; set; } = SovrgnRoutingClient.DefaultTenant;
}

public sealed record SovrgnResolvedConfig(
    Uri EndpointUri,
    string ApiKey,
    string Model,
    string Tenant);

public static class SovrgnRoutingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="SovrgnAiOptions"/>, <see cref="SovrgnRoutingClient"/>, and its dedicated
    /// no-redirect <see cref="HttpClient"/> with Aspire global resilience/service-discovery handlers
    /// stripped so non-idempotent POST LLM completions are never aborted at 10s or retried up to 3x.
    /// </summary>
    public static IServiceCollection AddSovrgnRouting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<SovrgnAiOptions>(configuration.GetSection(SovrgnAiOptions.SectionName));
        services.AddHttpClient(SovrgnRoutingClient.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(45);
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
        });

        // Strip global Aspire AddStandardResilienceHandler() (10s attempt timeout + 3x POST retries)
        // and AddServiceDiscovery() added via ConfigureHttpClientDefaults() in ServiceDefaults.
        services.PostConfigure<HttpClientFactoryOptions>(
            SovrgnRoutingClient.HttpClientName,
            options =>
            {
                options.HttpMessageHandlerBuilderActions.Add(builder =>
                {
                    for (var i = builder.AdditionalHandlers.Count - 1; i >= 0; i--)
                    {
                        var handlerType = builder.AdditionalHandlers[i].GetType();
                        var fullName = handlerType.FullName ?? handlerType.Name;
                        if (fullName.Contains("Resilience", StringComparison.OrdinalIgnoreCase)
                            || fullName.Contains("ServiceDiscovery", StringComparison.OrdinalIgnoreCase))
                        {
                            builder.AdditionalHandlers.RemoveAt(i);
                        }
                    }
                });
            });

        services.AddScoped<SovrgnRoutingClient>();
        return services;
    }
}

/// <summary>
/// OpenAI-compatible HTTP client targeting Tailor Routing (<c>https://api.sovrgn.ai/v1/chat/completions</c>).
/// Supports explicit <c>SOVRGN_*</c> / <c>Sovrgn:*</c> configuration as well as pointing existing
/// <c>AzureAI:Endpoint</c> / <c>AzureAI:ApiKey</c> secrets at an OpenAI-compatible HTTPS endpoint
/// (enabling zero-mount secret cutovers on AloomU).
/// Fail-closed: rejects non-HTTPS, credentialed, or query/fragment URLs and uses a dedicated
/// no-redirect <see cref="HttpClient"/> so 3xx responses never leak Bearer tokens.
/// </summary>
public sealed class SovrgnRoutingClient
{
    public const string HttpClientName = "SovrgnRouting";
    public const string DefaultBaseUrl = "https://api.sovrgn.ai/v1";
    public const string DefaultModel = "tailor-fast";
    public const string DefaultTenant = "aigents";

    private static readonly HashSet<string> NonReasoningModels = new(StringComparer.OrdinalIgnoreCase)
    {
        "gpt-4.1",
        "gpt-4.1-mini",
        "gpt-4.1-nano",
        "gpt-4o",
        "gpt-4o-mini",
        "au-inregion-gpt-4o",
        "au-inregion-gpt-41-mini",
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SovrgnAiOptions _sovrgnOptions;
    private readonly AzureAiOptions _azureOptions;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SovrgnRoutingClient> _logger;

    public SovrgnRoutingClient(
        IHttpClientFactory httpClientFactory,
        IOptions<SovrgnAiOptions> sovrgnOptions,
        IOptions<AzureAiOptions> azureOptions,
        IConfiguration configuration,
        ILogger<SovrgnRoutingClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _sovrgnOptions = sovrgnOptions.Value;
        _azureOptions = azureOptions.Value;
        _configuration = configuration;
        _logger = logger;
    }

    public static Uri? BuildChatCompletionsUri(string? rawUrl)
    {
        var input = string.IsNullOrWhiteSpace(rawUrl) ? DefaultBaseUrl : rawUrl.Trim();
        if (input.Contains('?') || input.Contains('#') || input.Contains('@'))
            return null;
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri))
            return null;
        if (uri.Scheme != Uri.UriSchemeHttps)
            return null;
        if (string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            return null;
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return null;

        var trimmed = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return new Uri(trimmed);
        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return new Uri($"{trimmed}/chat/completions");

        return new Uri($"{trimmed}/v1/chat/completions");
    }

    public static bool IsAzureOpenAiEndpoint(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
            return false;
        var trimmed = rawUrl.Trim();
        if (trimmed.Contains('?') || trimmed.Contains('#') || trimmed.Contains('@'))
            return false;
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme != Uri.UriSchemeHttps)
            return false;
        if (string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            return false;
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return false;

        return uri.Host.EndsWith(".openai.azure.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".cognitiveservices.azure.com", StringComparison.OrdinalIgnoreCase);
    }

    public static bool SupportsCustomTemperature(string? model) =>
        !string.IsNullOrWhiteSpace(model) && NonReasoningModels.Contains(model.Trim());

    public bool TryResolveConfig(out SovrgnResolvedConfig? resolved, out string? configError)
    {
        resolved = null;
        configError = null;

        var explicitKey = FirstNonEmpty(_configuration["SOVRGN_API_KEY"], _sovrgnOptions.ApiKey);
        if (!string.IsNullOrEmpty(explicitKey))
        {
            var rawUrl = FirstNonEmpty(_configuration["SOVRGN_API_URL"], _sovrgnOptions.Endpoint, DefaultBaseUrl);
            var endpointUri = BuildChatCompletionsUri(rawUrl);
            if (endpointUri is null)
            {
                configError = "SOVRGN_API_KEY is configured but the Tailor Routing endpoint URL is invalid (HTTPS required, no embedded credentials, query, or fragment).";
                return false;
            }

            var model = FirstNonEmpty(_configuration["SOVRGN_MODEL"], _sovrgnOptions.Model, DefaultModel)!;
            var tenant = FirstNonEmpty(_configuration["SOVRGN_TENANT"], _sovrgnOptions.Tenant, DefaultTenant)!;
            resolved = new SovrgnResolvedConfig(endpointUri, explicitKey, model, tenant);
            return true;
        }

        // Zero-mount migration path: existing AzureAI__Endpoint / AzureAI__ApiKey pointed at an
        // OpenAI-compatible HTTPS endpoint (e.g. https://api.sovrgn.ai/v1). Always fail closed
        // for any non-Azure-OpenAI (or non-HTTPS Azure) URL so DefaultAzureCredential can never
        // send Entra ID tokens to an arbitrary host.
        if (!string.IsNullOrWhiteSpace(_azureOptions.Endpoint)
            && !IsAzureOpenAiEndpoint(_azureOptions.Endpoint))
        {
            var endpointUri = BuildChatCompletionsUri(_azureOptions.Endpoint);
            if (endpointUri is null)
            {
                configError = "AzureAI:Endpoint is not a valid HTTPS URL (no embedded credentials, query, or fragment).";
                return false;
            }

            if (string.IsNullOrWhiteSpace(_azureOptions.ApiKey))
            {
                configError = "AzureAI:Endpoint points to a non-Azure OpenAI host but AzureAI:ApiKey is missing; refusing to fall back to Entra ID credentials.";
                return false;
            }

            var model = FirstNonEmpty(_configuration["SOVRGN_MODEL"], _azureOptions.DeploymentName, DefaultModel)!;
            var tenant = FirstNonEmpty(_configuration["SOVRGN_TENANT"], _sovrgnOptions.Tenant, DefaultTenant)!;
            resolved = new SovrgnResolvedConfig(endpointUri, _azureOptions.ApiKey.Trim(), model, tenant);
            return true;
        }

        return false;
    }

    public Task<AiResponse> CompleteChatAsync(
        IReadOnlyList<(string Role, string Content)> messages,
        int maxTokens,
        double? temperature,
        bool jsonObjectResponse,
        CancellationToken ct = default)
    {
        if (!TryResolveConfig(out var cfg, out var error) || cfg is null)
            throw new InvalidOperationException(error ?? "Tailor Routing (SOVRGN) is not configured.");

        return CompleteChatAsync(cfg, messages, maxTokens, temperature, jsonObjectResponse, ct);
    }

    public async Task<AiResponse> CompleteChatAsync(
        SovrgnResolvedConfig cfg,
        IReadOnlyList<(string Role, string Content)> messages,
        int maxTokens,
        double? temperature,
        bool jsonObjectResponse,
        CancellationToken ct = default)
    {
        var payload = BuildPayload(cfg.Model, messages, maxTokens, temperature, jsonObjectResponse);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var req = new HttpRequestMessage(HttpMethod.Post, cfg.EndpointUri)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiKey);
        req.Headers.TryAddWithoutValidation("X-Sovrgn-Tenant", cfg.Tenant);

        using var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Tailor Routing returned HTTP {Status} from {Host} for model {Model}",
                (int)resp.StatusCode,
                cfg.EndpointUri.Host,
                cfg.Model);
            throw new HttpRequestException(
                $"Tailor Routing upstream returned HTTP {(int)resp.StatusCode}.",
                null,
                resp.StatusCode);
        }

        var body = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        string? rawContent = null;
        string? finishReason = null;
        if (root.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
                finishReason = fr.GetString();
            if (first.TryGetProperty("message", out var msg)
                && msg.ValueKind == JsonValueKind.Object
                && msg.TryGetProperty("content", out var contentProp)
                && contentProp.ValueKind == JsonValueKind.String)
            {
                rawContent = contentProp.GetString();
            }
        }

        if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Tailor Routing completion hit max_tokens length limit from {Host} for model {Model}",
                cfg.EndpointUri.Host,
                cfg.Model);
        }

        var content = string.IsNullOrWhiteSpace(rawContent)
            ? (jsonObjectResponse ? "{}" : "No response generated.")
            : rawContent;

        var tokensUsed = 0;
        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            if (usage.TryGetProperty("total_tokens", out var totalProp) && totalProp.TryGetInt32(out var total))
            {
                tokensUsed = total;
            }
            else
            {
                var input = usage.TryGetProperty("prompt_tokens", out var inProp) && inProp.TryGetInt32(out var i) ? i : 0;
                var output = usage.TryGetProperty("completion_tokens", out var outProp) && outProp.TryGetInt32(out var o) ? o : 0;
                tokensUsed = input + output;
            }
        }

        var servedModel = root.TryGetProperty("model", out var modelProp) && modelProp.ValueKind == JsonValueKind.String
            ? (modelProp.GetString() ?? cfg.Model)
            : cfg.Model;

        _logger.LogInformation(
            "Tailor Routing response generated via {Host} (model={Model}, tokens={Tokens})",
            cfg.EndpointUri.Host,
            servedModel,
            tokensUsed);

        return new AiResponse
        {
            Content = content,
            TokensUsed = tokensUsed,
            Model = servedModel,
        };
    }

    public static string BuildPayload(
        string model,
        IReadOnlyList<(string Role, string Content)> messages,
        int maxTokens,
        double? temperature,
        bool jsonObjectResponse)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("model", model);
            w.WritePropertyName("messages");
            w.WriteStartArray();
            foreach (var (role, content) in messages)
            {
                w.WriteStartObject();
                w.WriteString("role", role);
                w.WriteString("content", content);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteNumber("max_tokens", Math.Clamp(maxTokens, 256, 4096));

            if (temperature.HasValue && SupportsCustomTemperature(model))
                w.WriteNumber("temperature", temperature.Value);

            if (jsonObjectResponse)
            {
                w.WritePropertyName("response_format");
                w.WriteStartObject();
                w.WriteString("type", "json_object");
                w.WriteEndObject();
            }

            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (var c in candidates)
        {
            if (!string.IsNullOrWhiteSpace(c))
                return c.Trim();
        }
        return null;
    }
}
