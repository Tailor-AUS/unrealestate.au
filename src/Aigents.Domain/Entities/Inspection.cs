namespace Aigents.Domain.Entities;

/// <summary>
/// An open home or private inspection event
/// </summary>
public class Inspection
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public Guid? ListingId { get; set; }

    // External IDs
    public string? CrmInspectionId { get; set; }
    public string? CrmSource { get; set; }

    // Property details
    public required string PropertyAddress { get; set; }
    public string? PropertySuburb { get; set; }

    // Timing
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public InspectionType Type { get; set; } = InspectionType.OpenHome;

    // Check-in
    public required string CheckInToken { get; set; }
    public string? QrCodeUrl { get; set; }

    // Stats
    public int RsvpCount { get; set; }
    public int CheckInCount { get; set; }

    // Status
    public InspectionStatus Status { get; set; } = InspectionStatus.Scheduled;
    public string? Notes { get; set; }

    // Timestamps
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public Agent? Agent { get; set; }
    public Listing? Listing { get; set; }
    public List<InspectionAttendee> Attendees { get; set; } = new();
}

public enum InspectionType
{
    OpenHome,
    PrivateInspection,
    Twilight,
    AuctionPreview
}

public enum InspectionStatus
{
    Scheduled,
    InProgress,
    Completed,
    Cancelled
}

/// <summary>
/// A person who checked in at an inspection
/// </summary>
public class InspectionAttendee
{
    public Guid Id { get; set; }
    public Guid InspectionId { get; set; }
    public Guid? ContactId { get; set; }

    // Attendee info
    public required string Name { get; set; }
    public required string Phone { get; set; }
    public string? Email { get; set; }

    // Qualification questions
    public bool? PreApproved { get; set; }
    public string? Timeline { get; set; } // "Now", "1-3 months", etc.
    public string? Budget { get; set; }
    public bool? FirstHomeBuyer { get; set; }

    // Marketing
    public bool OptedInAlerts { get; set; }
    public bool OptedInMarketing { get; set; }

    // Additional answers (flexible schema)
    public Dictionary<string, string> CustomAnswers { get; set; } = new();

    // Check-in metadata
    public DateTimeOffset CheckInTime { get; set; } = DateTimeOffset.UtcNow;
    public string? DeviceInfo { get; set; }
    public string? IpAddress { get; set; }

    // CRM sync
    public bool SyncedToCrm { get; set; }
    public string? CrmContactId { get; set; }

    // Follow-up
    public FollowUpStatus FollowUpStatus { get; set; } = FollowUpStatus.Pending;
    public DateTimeOffset? FollowedUpAt { get; set; }
    public string? FollowUpNotes { get; set; }

    // Navigation
    public Inspection? Inspection { get; set; }
    public Contact? Contact { get; set; }
}

public enum FollowUpStatus
{
    Pending,
    Contacted,
    NotInterested,
    Interested,
    Converted
}
