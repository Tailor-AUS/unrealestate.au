using System.ClientModel;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace Aigents.Infrastructure.Services.AI;

/// <summary>
/// Service for interacting with Azure AI Foundry (Azure OpenAI)
/// </summary>
public interface IAiService
{
    Task<AiResponse> ChatAsync(IEnumerable<ChatMessage> messages, string mode, CancellationToken ct = default);
    
    /// <summary>
    /// Search for property intelligence using Azure AI with web grounding
    /// </summary>
    Task<PropertyIntelligenceResponse> SearchPropertyIntelligenceAsync(string address, CancellationToken ct = default);
}

public class AzureAiService : IAiService
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<AzureAiService> _logger;
    private readonly AzureAiOptions _options;

    private static readonly Dictionary<string, string> SystemPrompts = new()
    {
        ["buy"] = """
            You are an expert AI buyer's agent for unrealestate.au, specializing in Brisbane and Gold Coast real estate.

            Your capabilities:
            - Search on-market properties via Domain and REA
            - Access off-market properties via CoreLogic/RP Data
            - Get automated valuations (AVM) with comparable sales
            - Find properties matching specific criteria
            - Identify high-growth suburbs and investment opportunities
            - Book inspections and submit offers

            Your personality:
            - Professional yet warm and approachable
            - Concise and direct - no fluff
            - Data-driven with local market expertise
            - Proactive in suggesting relevant options

            Coverage: 130+ suburbs across Brisbane and Gold Coast.
            Keep responses concise. Use line breaks for readability.
            """,
        
        ["sell"] = """
            You are an expert AI listing generator for unrealestate.au, helping homeowners create compelling property listings.
            
            IMPORTANT: Your FIRST goal is to ask the user for their property address.
            Once you have the address, you should:
            1. Acknowledge the location.
            2. Simulate generating a detailed property report (mentioning you are pulling data from CoreLogic/RP Data).
            3. Ask follow-up questions about the property's condition or unique features to refine the report.

            Your capabilities:
            - Generate engaging property headlines
            - Write compelling property descriptions
            - Suggest key features based on property type and location
            - Estimate property values based on suburb data
            - Identify target buyer profiles
            - Create marketing copy that sells

            Your style:
            - Professional yet warm
            - Descriptive without being over-the-top
            - Focus on lifestyle benefits
            - Highlight location advantages
            - Use emotional triggers appropriately

            Coverage: Brisbane and Gold Coast suburbs.
            Always be realistic with valuations.
            Keep descriptions under 200 words but impactful.
            """,
    };

    public AzureAiService(IOptions<AzureAiOptions> options, ILogger<AzureAiService> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Create Azure OpenAI client
        AzureOpenAIClient azureClient;
        
        if (!string.IsNullOrEmpty(_options.ApiKey))
        {
            // Use API key authentication
            azureClient = new AzureOpenAIClient(
                new Uri(_options.Endpoint),
                new System.ClientModel.ApiKeyCredential(_options.ApiKey));
        }
        else
        {
            // Use Managed Identity (recommended for production)
            azureClient = new AzureOpenAIClient(
                new Uri(_options.Endpoint),
                new DefaultAzureCredential());
        }

        _chatClient = azureClient.GetChatClient(_options.DeploymentName);
    }

    public async Task<AiResponse> ChatAsync(IEnumerable<ChatMessage> messages, string mode, CancellationToken ct = default)
    {
        try
        {
            var systemPrompt = SystemPrompts.GetValueOrDefault(mode.ToLower(), SystemPrompts["buy"]);
            
            // Build messages list
            var chatMessages = new List<OpenAI.Chat.ChatMessage>
            {
                new SystemChatMessage(systemPrompt)
            };

            foreach (var msg in messages)
            {
                if (msg.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
                {
                    chatMessages.Add(new UserChatMessage(msg.Content));
                }
                else if (msg.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
                {
                    chatMessages.Add(new AssistantChatMessage(msg.Content));
                }
            }

            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = 1024,
                Temperature = 0.7f,
            };

            var response = await _chatClient.CompleteChatAsync(chatMessages, options, ct);
            
            var content = response.Value.Content.FirstOrDefault()?.Text ?? "No response generated.";
            var usage = response.Value.Usage;
            
            _logger.LogInformation(
                "Azure AI response generated. Tokens: {InputTokens}/{OutputTokens}", 
                usage?.InputTokenCount, 
                usage?.OutputTokenCount);

            return new AiResponse
            {
                Content = content,
                TokensUsed = (usage?.InputTokenCount ?? 0) + (usage?.OutputTokenCount ?? 0),
                Model = _options.DeploymentName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Azure AI Foundry");
            throw;
        }
    }

    public async Task<PropertyIntelligenceResponse> SearchPropertyIntelligenceAsync(string address, CancellationToken ct = default)
    {
        try
        {
            var systemPrompt = """
                You are a property data assistant. Given an Australian property address, provide accurate property details.
                
                Use your knowledge of Australian real estate data from Domain.com.au, Realestate.com.au, and public records.
                
                Respond ONLY with valid JSON in this exact format (no markdown, no extra text):
                {
                    "bedrooms": 4,
                    "bathrooms": 2,
                    "carSpaces": 2,
                    "landSizeM2": 650,
                    "propertyType": "house",
                    "estimatedValueLow": 800000,
                    "estimatedValueHigh": 950000,
                    "lastSalePrice": 720000,
                    "lastSaleYear": "2019",
                    "suburb": "Paddington",
                    "state": "QLD",
                    "postcode": "4064",
                    "confidence": "high",
                    "sources": ["Domain.com.au historic listing", "PropTrack AVM"]
                }
                
                If you don't have data, use reasonable estimates based on the suburb and property type.
                Set confidence to "low" if estimating.
                """;

            var userPrompt = $"Provide property details for: {address}";

            var chatMessages = new List<OpenAI.Chat.ChatMessage>
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(userPrompt)
            };

            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = 512,
                Temperature = 0.3f, // Lower temperature for more consistent JSON
            };

            var response = await _chatClient.CompleteChatAsync(chatMessages, options, ct);
            var content = response.Value.Content.FirstOrDefault()?.Text ?? "{}";
            
            _logger.LogInformation("Property intelligence response for {Address}: {Response}", address, content);

            // Parse the JSON response
            try
            {
                // Clean up the response (remove markdown code blocks if present)
                content = content.Trim();
                if (content.StartsWith("```"))
                {
                    content = content.Split('\n', 2).Length > 1 
                        ? content.Split('\n', 2)[1] 
                        : content;
                    content = content.TrimEnd('`').Trim();
                }

                var result = System.Text.Json.JsonSerializer.Deserialize<PropertyIntelligenceResponse>(
                    content, 
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                return result ?? new PropertyIntelligenceResponse { Confidence = "failed" };
            }
            catch (System.Text.Json.JsonException jsonEx)
            {
                _logger.LogWarning(jsonEx, "Failed to parse property intelligence JSON: {Content}", content);
                return new PropertyIntelligenceResponse { Confidence = "failed" };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching property intelligence for {Address}", address);
            return new PropertyIntelligenceResponse { Confidence = "error" };
        }
    }
}

public class ChatMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
}

public class AiResponse
{
    public string Content { get; set; } = string.Empty;
    public int TokensUsed { get; set; }
    public string? Model { get; set; }
}

public class AzureAiOptions
{
    public const string SectionName = "AzureAI";
    
    /// <summary>
    /// Azure AI Foundry endpoint (e.g., https://your-resource.openai.azure.com/)
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;
    
    /// <summary>
    /// API Key (optional - use Managed Identity in production)
    /// </summary>
    public string? ApiKey { get; set; }
    
    /// <summary>
    /// Model deployment name (e.g., gpt-4o, gpt-4, gpt-35-turbo)
    /// </summary>
    public string DeploymentName { get; set; } = "gpt-4o";
}

/// <summary>
/// Response from Azure AI property intelligence search
/// </summary>
public class PropertyIntelligenceResponse
{
    public int? Bedrooms { get; set; }
    public int? Bathrooms { get; set; }
    public int? CarSpaces { get; set; }
    public int? LandSizeM2 { get; set; }
    public string? PropertyType { get; set; }
    public decimal? EstimatedValueLow { get; set; }
    public decimal? EstimatedValueHigh { get; set; }
    public decimal? LastSalePrice { get; set; }
    public string? LastSaleYear { get; set; }
    public string? Suburb { get; set; }
    public string? State { get; set; }
    public string? Postcode { get; set; }
    public string Confidence { get; set; } = "unknown";
    public List<string> Sources { get; set; } = new();
}
