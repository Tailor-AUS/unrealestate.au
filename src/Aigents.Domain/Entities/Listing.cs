namespace Aigents.Domain.Entities;

/// <summary>
/// Represents a property listing created by a seller
/// </summary>
public class Listing
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }

    // Property Details
    public string Address { get; set; } = string.Empty;
    public string Suburb { get; set; } = string.Empty;
    public string State { get; set; } = "QLD";
    public string Postcode { get; set; } = string.Empty;

    // Property Attributes (captured or AI-estimated)
    public int? Bedrooms { get; set; }
    public int? Bathrooms { get; set; }
    public int? CarSpaces { get; set; }
    public int? LandSize { get; set; } // sqm
    public string PropertyType { get; set; } = "House"; // House, Unit, Townhouse, Land

    // AI-Generated Content
    public string Headline { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Features { get; set; } = string.Empty; // JSON array
    public string TargetBuyers { get; set; } = string.Empty; // AI-suggested buyer profiles

    // Pricing
    public decimal? EstimatedValue { get; set; }
    public decimal? AskingPrice { get; set; }
    public string PriceDisplay { get; set; } = string.Empty; // "Offers over $1.2M"

    // Images
    public string ImageUrls { get; set; } = "[]"; // JSON array of URLs

    // Status
    public ListingStatus Status { get; set; } = ListingStatus.Draft;
    public bool AgreementSigned { get; set; }
    public DateTime? AgreementSignedAt { get; set; }
    public string? AgreementSignature { get; set; } // Base64 or name

    // Listing Mode (Open to all agents vs Exclusive to one)
    public ListingMode Mode { get; set; } = ListingMode.Open;
    public Guid? ExclusiveAgentId { get; set; }
    public Agent? ExclusiveAgent { get; set; }

    // Distribution
    public bool DistributedToAgents { get; set; }
    public DateTime? DistributedAt { get; set; }
    public int AgentsNotified { get; set; }

    // Timestamps
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }

    // Navigation
    public User User { get; set; } = null!;
    public ICollection<ListingInquiry> Inquiries { get; set; } = new List<ListingInquiry>();
    public ICollection<SyndicationStatus> SyndicationStatuses { get; set; } = new List<SyndicationStatus>();
}

/// <summary>
/// Determines if listing is open to all agents or exclusive to one
/// </summary>
public enum ListingMode
{
    Open,      // Available to all agents
    Exclusive  // Appointed single agent
}

public enum ListingStatus
{
    Draft,           // Just created, editing
    PendingSignature, // Waiting for open listing agreement
    Active,          // Live, distributed to agents
    UnderOffer,      // Agent has found a buyer
    Sold,            // Deal completed
    Withdrawn,       // Seller pulled listing
    Expired          // Time limit reached
}

/// <summary>
/// Represents an inquiry from an agent about a listing
/// </summary>
public class ListingInquiry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListingId { get; set; }
    public Guid? AgentId { get; set; }

    // Buyer-originated enquiries do not need a fabricated Agent row. These
    // fields are stored in the application database but never copied into
    // product-event telemetry.
    public string? BuyerName { get; set; }
    public string? BuyerEmail { get; set; }
    public string? BuyerPhone { get; set; }
    public decimal? OfferAmount { get; set; }
    public BuyerInquiryType InquiryType { get; set; } = BuyerInquiryType.Question;

    public string Message { get; set; } = string.Empty;
    public InquiryStatus Status { get; set; } = InquiryStatus.New;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAt { get; set; }

    // Navigation
    public Listing Listing { get; set; } = null!;
    public Agent? Agent { get; set; }
}

public enum BuyerInquiryType
{
    Question,
    Inspection,
    Offer,
    Agent
}

public enum InquiryStatus
{
    New,
    Viewed,
    Responded,
    InspectionBooked,
    OfferMade
}
