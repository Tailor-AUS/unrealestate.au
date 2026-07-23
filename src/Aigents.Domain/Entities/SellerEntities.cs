using Aigents.Domain.Entities;

/// <summary>
/// A property claimed by a Seller user
/// </summary>
public class SellerProperty
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }

    // Validated Address (matched to CoreLogic/Domain ID)
    public string PropertyId { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Suburb { get; set; } = string.Empty;

    // User's relationship
    public bool IsOwnerOccupier { get; set; } // vs Investor
    public SellerGoal Goal { get; set; } = SellerGoal.TrackValue;
    public string? Motivation { get; set; }

    // Automated Valuation Tracking
    public decimal? LatestAvm { get; set; }
    public decimal? PurchasePrice { get; set; }
    public DateTime? PurchaseDate { get; set; }

    // Sale Campaign tracking (if active)
    public bool IsActiveCampaign { get; set; }
    public Guid? ListingAgentId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public User? User { get; set; }
    public List<AppraisalRequest> AppraisalRequests { get; set; } = new();
}

public enum SellerGoal
{
    TrackValue,
    ThinkingOfSelling,
    ReadyToSell,
    LeaseOut
}

/// <summary>
/// A request for a digital or physical appraisal from an agent
/// </summary>
public class AppraisalRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SellerPropertyId { get; set; }
    public Guid? AgentId { get; set; } // Null = "Find me the best agent"

    public AppraisalType Type { get; set; } = AppraisalType.Digital;
    public AppraisalStatus Status { get; set; } = AppraisalStatus.Pending;

    // Agent's Response
    public decimal? EstimatedValueLow { get; set; }
    public decimal? EstimatedValueHigh { get; set; }
    public string? AgentNotes { get; set; }
    public DateTimeOffset? AppraisedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public SellerProperty? SellerProperty { get; set; }
    public Agent? Agent { get; set; }
}

public enum AppraisalType
{
    Digital, // AI/Data only + Agent review
    Physical // Agent visits home
}

public enum AppraisalStatus
{
    Pending,
    Accepted, // Agent accepted request
    Scheduled, // For physical
    Completed,
    Rejected
}
