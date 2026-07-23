namespace Aigents.Domain.Entities;

/// <summary>
/// A voice note recorded by an agent, linked to contacts/properties
/// </summary>
public class VoiceNote
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? ListingId { get; set; }
    public Guid? InspectionId { get; set; }

    // Recording
    public required string AudioUrl { get; set; }
    public int DurationSeconds { get; set; }
    public string? AudioFormat { get; set; } // "m4a", "wav", "webm"

    // Transcription
    public string? Transcript { get; set; }
    public TranscriptionStatus TranscriptionStatus { get; set; } = TranscriptionStatus.None;

    // Context (auto-detected or manual)
    public VoiceNoteContext Context { get; set; } = VoiceNoteContext.General;
    public string? ContextLabel { get; set; } // "Post-inspection", "Follow-up reminder"

    // Location (if recorded at a property)
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? LocationAddress { get; set; }

    // Timestamps
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public Agent? Agent { get; set; }
    public Contact? Contact { get; set; }
    public Listing? Listing { get; set; }
    public Inspection? Inspection { get; set; }
}

public enum VoiceNoteContext
{
    General,
    PostCall,
    PostInspection,
    PropertyNote,
    ContactNote,
    Reminder,
    Idea
}
