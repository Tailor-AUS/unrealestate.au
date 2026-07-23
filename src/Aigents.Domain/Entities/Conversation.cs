namespace Aigents.Domain.Entities;

/// <summary>
/// Represents a chat conversation between a user and the AI agent
/// </summary>
public class Conversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public AgentMode Mode { get; set; }
    public string? Summary { get; set; }
    public ConversationStatus Status { get; set; } = ConversationStatus.Active;

    // Timestamps
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User User { get; set; } = null!;
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}

public enum ConversationStatus
{
    Active,
    Completed,
    HandedOff,
    Archived
}
