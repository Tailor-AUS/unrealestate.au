using Microsoft.Extensions.Logging;

namespace Aigents.Infrastructure.Services.AI;

/// <summary>
/// AI service for call intelligence - transcription analysis and summarization
/// </summary>
public interface ICallIntelligenceService
{
    /// <summary>
    /// Summarize a call transcript and extract action items
    /// </summary>
    Task<CallAnalysis> AnalyzeCallAsync(string transcript, CallContext context, CancellationToken ct = default);

    /// <summary>
    /// Extract property mentions from a transcript
    /// </summary>
    Task<List<PropertyMention>> ExtractPropertiesAsync(string transcript, CancellationToken ct = default);

    /// <summary>
    /// Score a lead based on call history
    /// </summary>
    Task<LeadScoreResult> ScoreLeadAsync(LeadScoreContext context, CancellationToken ct = default);
}

/// <summary>
/// Implementation using Azure AI
/// </summary>
public class CallIntelligenceService : ICallIntelligenceService
{
    private readonly IAiService _aiService;
    private readonly ILogger<CallIntelligenceService> _logger;

    public CallIntelligenceService(IAiService aiService, ILogger<CallIntelligenceService> logger)
    {
        _aiService = aiService;
        _logger = logger;
    }

    public async Task<CallAnalysis> AnalyzeCallAsync(string transcript, CallContext context, CancellationToken ct = default)
    {
        var systemPrompt = """
            You are an expert real estate call analyst. Analyze the following call transcript between a real estate agent and a prospect.
            
            Extract:
            1. A concise summary (2-3 sentences)
            2. Overall sentiment (very_negative, negative, neutral, positive, very_positive)
            3. Key points discussed (bullet points)
            4. Action items for the agent with due dates if mentioned
            5. Any properties or addresses mentioned
            6. Buyer readiness signals (timeline, budget, pre-approval status)
            
            Respond in JSON format:
            {
                "summary": "...",
                "sentiment": "positive",
                "keyPoints": ["point 1", "point 2"],
                "actionItems": [{"description": "...", "dueDate": "2025-12-15", "priority": "high"}],
                "propertiesMentioned": ["45 Ocean St, Manly"],
                "buyerSignals": {
                    "timeline": "1-3 months",
                    "budget": "$1.5M-$2M",
                    "preApproved": true,
                    "motivation": "downsizing"
                }
            }
            """;

        var messages = new List<ChatMessage>
        {
            new()
            {
                Role = "user",
                Content =
                    $"{systemPrompt}\n\nCall between agent and contact named "
                    + $"'{context.ContactName}':\n\n{transcript}"
            }
        };

        try
        {
            var response = await _aiService.ChatAsync(messages, "agent", ct);
            var analysis = ParseCallAnalysis(response.Content);

            _logger.LogInformation("Call analyzed successfully. Sentiment: {Sentiment}, Actions: {ActionCount}",
                analysis.Sentiment, analysis.ActionItems.Count);

            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze call transcript");
            return new CallAnalysis
            {
                Summary = "Unable to analyze call",
                Sentiment = "neutral",
                KeyPoints = new List<string>(),
                ActionItems = new List<ActionItemResult>()
            };
        }
    }

    public async Task<List<PropertyMention>> ExtractPropertiesAsync(string transcript, CancellationToken ct = default)
    {
        var prompt = """
            Extract all property addresses mentioned in this conversation.
            Return as JSON array:
            [{"address": "45 Ocean St, Manly", "context": "interested in viewing"}]
            
            Transcript:
            """;

        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = prompt + transcript }
        };

        try
        {
            var response = await _aiService.ChatAsync(messages, "agent", ct);
            return ParsePropertyMentions(response.Content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract properties from transcript");
            return new List<PropertyMention>();
        }
    }

    public async Task<LeadScoreResult> ScoreLeadAsync(LeadScoreContext context, CancellationToken ct = default)
    {
        var prompt = $"""
            Score this lead on a scale of 0-100 based on their likelihood to transact.
            
            Contact: {context.ContactName}
            Classification: {context.Classification}
            Recent Calls: {context.RecentCallCount}
            Inspections Attended: {context.InspectionsAttended}
            Properties Interested In: {context.PropertiesInterestedIn}
            Pre-approved: {context.PreApproved}
            Timeline: {context.Timeline}
            Budget: {context.Budget}
            Last Contact: {context.DaysSinceLastContact} days ago
            
            Recent interactions:
            {context.RecentInteractionSummary}
            
            Return JSON with format: score (number), tier (string), reasoning (string), suggestedActions (array)
            
            Tiers: cold (0-25), warm (26-50), qualified (51-75), hot (76-100)
            """;

        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = prompt }
        };

        try
        {
            var response = await _aiService.ChatAsync(messages, "agent", ct);
            return ParseLeadScore(response.Content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to score lead");
            return new LeadScoreResult { Score = 50, Tier = "warm", Reasoning = "Unable to determine" };
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // PARSING HELPERS
    // ═══════════════════════════════════════════════════════════════

    private static CallAnalysis ParseCallAnalysis(string json)
    {
        try
        {
            // Extract JSON from response (may have markdown)
            var jsonStart = json.IndexOf('{');
            var jsonEnd = json.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                json = json.Substring(jsonStart, jsonEnd - jsonStart + 1);
            }

            return System.Text.Json.JsonSerializer.Deserialize<CallAnalysis>(json)
                ?? new CallAnalysis { Summary = "Parse error" };
        }
        catch
        {
            return new CallAnalysis { Summary = json }; // Return raw if can't parse
        }
    }

    private static List<PropertyMention> ParsePropertyMentions(string json)
    {
        try
        {
            var jsonStart = json.IndexOf('[');
            var jsonEnd = json.LastIndexOf(']');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                json = json.Substring(jsonStart, jsonEnd - jsonStart + 1);
            }

            return System.Text.Json.JsonSerializer.Deserialize<List<PropertyMention>>(json)
                ?? new List<PropertyMention>();
        }
        catch
        {
            return new List<PropertyMention>();
        }
    }

    private static LeadScoreResult ParseLeadScore(string json)
    {
        try
        {
            var jsonStart = json.IndexOf('{');
            var jsonEnd = json.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                json = json.Substring(jsonStart, jsonEnd - jsonStart + 1);
            }

            return System.Text.Json.JsonSerializer.Deserialize<LeadScoreResult>(json)
                ?? new LeadScoreResult { Score = 50, Tier = "warm" };
        }
        catch
        {
            return new LeadScoreResult { Score = 50, Tier = "warm" };
        }
    }
}

// ═══════════════════════════════════════════════════════════════
// MODELS
// ═══════════════════════════════════════════════════════════════

public class CallContext
{
    public string? ContactName { get; set; }
    public string? ContactClassification { get; set; }
    public string? PropertyAddress { get; set; }
    public int CallDurationSeconds { get; set; }
}

public class CallAnalysis
{
    public string Summary { get; set; } = string.Empty;
    public string Sentiment { get; set; } = "neutral";
    public List<string> KeyPoints { get; set; } = new();
    public List<ActionItemResult> ActionItems { get; set; } = new();
    public List<string> PropertiesMentioned { get; set; } = new();
    public BuyerSignals? BuyerSignals { get; set; }
}

public class ActionItemResult
{
    public string Description { get; set; } = string.Empty;
    public string? DueDate { get; set; }
    public string Priority { get; set; } = "normal";
}

public class BuyerSignals
{
    public string? Timeline { get; set; }
    public string? Budget { get; set; }
    public bool? PreApproved { get; set; }
    public string? Motivation { get; set; }
}

public class PropertyMention
{
    public string Address { get; set; } = string.Empty;
    public string? Context { get; set; }
}

public class LeadScoreContext
{
    public string? ContactName { get; set; }
    public string? Classification { get; set; }
    public int RecentCallCount { get; set; }
    public int InspectionsAttended { get; set; }
    public int PropertiesInterestedIn { get; set; }
    public bool? PreApproved { get; set; }
    public string? Timeline { get; set; }
    public string? Budget { get; set; }
    public int DaysSinceLastContact { get; set; }
    public string? RecentInteractionSummary { get; set; }
}

public class LeadScoreResult
{
    public int Score { get; set; }
    public string Tier { get; set; } = "warm";
    public string? Reasoning { get; set; }
    public List<string>? SuggestedActions { get; set; }
}
