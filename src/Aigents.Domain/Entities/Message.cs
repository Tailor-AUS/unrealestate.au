namespace Aigents.Domain.Entities;

/// <summary>
/// Represents a single message in a conversation
/// </summary>
public class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;

    // AI metadata
    public int? TokensUsed { get; set; }
    public string? ModelUsed { get; set; }

    // Timestamps
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Conversation Conversation { get; set; } = null!;
}

public enum MessageRole
{
    User,
    Assistant,
    System
}
