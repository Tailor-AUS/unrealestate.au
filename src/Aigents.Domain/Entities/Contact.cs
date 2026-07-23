namespace Aigents.Domain.Entities;

/// <summary>
/// Represents an agent's contact from their CRM or phone
/// </summary>
public class Contact
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }

    // External IDs
    public string? CrmId { get; set; }
    public string? CrmSource { get; set; } // "rex", "agentbox", "vaultre"
    public string? PhoneContactId { get; set; } // Device contact ID

    // Contact Info
    public required string FullName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Mobile { get; set; }
    public string? PhotoUrl { get; set; }

    // Classification
    public ContactClassification Classification { get; set; } = ContactClassification.Unknown;
    public LeadStatus LeadStatus { get; set; } = LeadStatus.New;
    public int LeadScore { get; set; } = 0; // 0-100

    // Source tracking
    public string? Source { get; set; } // "CRM Import", "Open Home", "Direct Call"
    public DateTimeOffset? FirstContactDate { get; set; }
    public DateTimeOffset? LastContactDate { get; set; }

    // Property interests
    public List<ContactPropertyInterest> PropertyInterests { get; set; } = new();

    // Notes
    public string? Notes { get; set; }
    public string? LastAiSummary { get; set; }

    // Timestamps
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SyncedAt { get; set; } // Last CRM sync

    // Navigation
    public Agent? Agent { get; set; }
    public List<CallRecord> Calls { get; set; } = new();
    public List<InspectionAttendee> InspectionAttendances { get; set; } = new();
}

public enum ContactClassification
{
    Unknown,
    Buyer,
    Seller,
    Investor,
    Tenant,
    Landlord,
    Vendor,
    OtherAgent
}


/// <summary>
/// Tracks a contact's interest in a specific property
/// </summary>
public class ContactPropertyInterest
{
    public Guid Id { get; set; }
    public Guid ContactId { get; set; }
    public Guid? ListingId { get; set; }
    public string? ListingAddress { get; set; }
    public string? ExternalListingId { get; set; } // CRM listing ID

    public InterestLevel InterestLevel { get; set; } = InterestLevel.Interested;
    public string? Notes { get; set; }

    public DateTimeOffset? FirstInteraction { get; set; }
    public DateTimeOffset? LastInteraction { get; set; }

    // Actions taken
    public bool AttendedInspection { get; set; }
    public bool RequestedInfo { get; set; }
    public bool MadeOffer { get; set; }
    public decimal? OfferAmount { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public Contact? Contact { get; set; }
    public Listing? Listing { get; set; }
}

public enum InterestLevel
{
    Browsing,
    Interested,
    VeryInterested,
    ReadyToBuy
}
