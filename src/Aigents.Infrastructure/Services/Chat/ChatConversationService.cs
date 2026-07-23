using Aigents.Domain.Entities;
using Aigents.Infrastructure.Data;
using Aigents.Infrastructure.Growth;
using Aigents.Infrastructure.Services.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aigents.Infrastructure.Services.Chat;

public sealed record ConversationInputMessage(string Role, string Content);

public sealed record ConversationReply(
    Guid ConversationId,
    string Content,
    int TokensUsed);

public interface IChatConversationService
{
    Task<ConversationReply> SendAsync(
        Guid userId,
        Guid? conversationId,
        IReadOnlyList<ConversationInputMessage> messages,
        string mode,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Authenticated chat application service shared by the cookie-authenticated
/// Blazor host and JWT-authenticated API. Identity is always supplied by the
/// trusted host, never by a request body.
/// </summary>
public sealed class ChatConversationService : IChatConversationService
{
    private readonly AigentsDbContext _db;
    private readonly IAiService _aiService;
    private readonly IProductEventRecorder _productEvents;
    private readonly ILogger<ChatConversationService> _logger;

    public ChatConversationService(
        AigentsDbContext db,
        IAiService aiService,
        IProductEventRecorder productEvents,
        ILogger<ChatConversationService> logger)
    {
        _db = db;
        _aiService = aiService;
        _productEvents = productEvents;
        _logger = logger;
    }

    public async Task<ConversationReply> SendAsync(
        Guid userId,
        Guid? conversationId,
        IReadOnlyList<ConversationInputMessage> messages,
        string mode,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("A user is required.", nameof(userId));
        if (messages.Count is 0 or > 50)
            throw new ArgumentException("Provide between 1 and 50 messages.", nameof(messages));
        if (messages.Any(message =>
                message is null
                || string.IsNullOrWhiteSpace(message.Content)
                || message.Content.Length > 10_000))
            throw new ArgumentException(
                "Messages must contain 1 to 10,000 characters.",
                nameof(messages));
        if (messages.Any(message =>
                !string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "Message roles must be user or assistant.",
                nameof(messages));
        }
        if (!string.Equals(
                messages[^1].Role,
                "user",
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                "The final message must be from the user.",
                nameof(messages));

        if (string.IsNullOrWhiteSpace(mode))
            throw new ArgumentException("Mode must be buy or sell.", nameof(mode));

        var normalizedMode = mode.Trim().ToLowerInvariant();
        if (normalizedMode is not ("buy" or "sell"))
            throw new ArgumentException("Mode must be buy or sell.", nameof(mode));

        Conversation conversation;
        var isNewConversation = !conversationId.HasValue;
        if (conversationId.HasValue)
        {
            conversation = await _db.Conversations
                .FirstOrDefaultAsync(
                    candidate => candidate.Id == conversationId.Value
                        && candidate.UserId == userId,
                    cancellationToken)
                ?? throw new InvalidOperationException("Conversation not found.");
            var requestedMode =
                normalizedMode == "sell" ? AgentMode.Sell : AgentMode.Buy;
            if (conversation.Mode != requestedMode)
            {
                throw new ArgumentException(
                    "Start a new conversation to change mode.",
                    nameof(mode));
            }
        }
        else
        {
            conversation = new Conversation
            {
                UserId = userId,
                Mode = normalizedMode == "sell" ? AgentMode.Sell : AgentMode.Buy
            };
        }

        var aiMessages = messages.Select(message => new ChatMessage
        {
            Role = message.Role,
            Content = message.Content
        });
        var aiResponse = await _aiService.ChatAsync(
            aiMessages,
            normalizedMode,
            cancellationToken);

        // Persist only after the model succeeds, so a transient AI failure
        // cannot leave an unsent user message in the conversation history.
        if (isNewConversation)
        {
            _db.Conversations.Add(conversation);
        }

        var lastMessage = messages[^1];
        _db.Messages.AddRange(
            new Message
            {
                ConversationId = conversation.Id,
                Role = MessageRole.User,
                Content = lastMessage.Content
            },
            new Message
            {
                ConversationId = conversation.Id,
                Role = MessageRole.Assistant,
                Content = aiResponse.Content,
                TokensUsed = aiResponse.TokensUsed,
                ModelUsed = aiResponse.Model
            });
        conversation.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        await _productEvents.RecordAsync(
            userId,
            ProductEventNames.AiChatStarted,
            deduplicationWindow: TimeSpan.FromMinutes(30),
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Chat message processed for conversation {ConversationId}; tokens {Tokens}",
            conversation.Id,
            aiResponse.TokensUsed);

        return new ConversationReply(
            conversation.Id,
            aiResponse.Content,
            aiResponse.TokensUsed);
    }
}
