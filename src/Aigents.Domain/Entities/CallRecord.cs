namespace Aigents.Domain.Entities;

/// <summary>
/// Records a phone call between an agent and a contact
/// </summary>
public class CallRecord
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public Guid? ContactId { get; set; }

    // Call details
    public required string PhoneNumber { get; set; }
    public CallDirection Direction { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public int DurationSeconds { get; set; }

    // Related property (if discussed)
    public Guid? PropertyId { get; set; }
    public string? PropertyAddress { get; set; }

    // Recording & Transcription
    public string? RecordingUrl { get; set; }
    public string? TranscriptUrl { get; set; }
    public string? Transcript { get; set; }
    public TranscriptionStatus TranscriptionStatus { get; set; } = TranscriptionStatus.None;

    // AI Analysis
    public string? AiSummary { get; set; }
    public CallSentiment Sentiment { get; set; } = CallSentiment.Neutral;
    public List<CallActionItem> ActionItems { get; set; } = new();
    public List<string> KeyPoints { get; set; } = new();
    public List<string> PropertiesMentioned { get; set; } = new();

    // Agent notes
    public string? AgentNotes { get; set; }

    // CRM sync
    public bool SyncedToCrm { get; set; }
    public string? CrmActivityId { get; set; }
    public DateTimeOffset? SyncedAt { get; set; }

    // Timestamps
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public Agent? Agent { get; set; }
    public Contact? Contact { get; set; }
    public Listing? Property { get; set; }
}

public enum CallDirection
{
    Incoming,
    Outgoing
}

public enum TranscriptionStatus
{
    None,
    Pending,
    Processing,
    Completed,
    Failed
}

public enum CallSentiment
{
    VeryNegative,
    Negative,
    Neutral,
    Positive,
    VeryPositive
}

/// <summary>
/// An action item extracted from a call by AI
/// </summary>
public class CallActionItem
{
    public Guid Id { get; set; }
    public Guid CallRecordId { get; set; }

    public required string Description { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public ActionItemPriority Priority { get; set; } = ActionItemPriority.Normal;
    public ActionItemStatus Status { get; set; } = ActionItemStatus.Pending;

    public string? CrmTaskId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public CallRecord? CallRecord { get; set; }
}

public enum ActionItemPriority
{
    Low,
    Normal,
    High,
    Urgent
}

public enum ActionItemStatus
{
    Pending,
    InProgress,
    Completed,
    Cancelled
}
