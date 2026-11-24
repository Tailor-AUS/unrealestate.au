using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
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
        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri))
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

        // Zero-mount migration path: existing AzureAI__Endpoint / AzureAI__ApiKey pointed at https://api.sovrgn.ai/v1
        if (!string.IsNullOrWhiteSpace(_azureOptions.Endpoint)
            && !IsAzureOpenAiEndpoint(_azureOptions.Endpoint)
            && !string.IsNullOrWhiteSpace(_azureOptions.ApiKey))
        {
            var endpointUri = BuildChatCompletionsUri(_azureOptions.Endpoint);
            if (endpointUri is null)
            {
                configError = "AzureAI:Endpoint is configured for a non-Azure OpenAI host but is not a valid HTTPS URL without credentials, query, or fragment.";
                return false;
            }

            var model = FirstNonEmpty(_azureOptions.DeploymentName, DefaultModel)!;
            var tenant = FirstNonEmpty(_configuration["SOVRGN_TENANT"], _sovrgnOptions.Tenant, DefaultTenant)!;
            resolved = new SovrgnResolvedConfig(endpointUri, _azureOptions.ApiKey.Trim(), model, tenant);
            return true;
        }

        return false;
    }

    public async Task<AiResponse> CompleteChatAsync(
        IReadOnlyList<(string Role, string Content)> messages,
        int maxTokens,
        double? temperature,
        bool jsonObjectResponse,
        CancellationToken ct = default)
    {
        if (!TryResolveConfig(out var cfg, out var error) || cfg is null)
            throw new InvalidOperationException(error ?? "Tailor Routing (SOVRGN) is not configured.");

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

        var content = "No response generated.";
        if (root.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var msg)
            && msg.TryGetProperty("content", out var contentProp)
            && contentProp.ValueKind == JsonValueKind.String)
        {
            content = contentProp.GetString() ?? "No response generated.";
        }

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
