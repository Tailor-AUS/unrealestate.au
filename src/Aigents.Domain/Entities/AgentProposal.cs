namespace Aigents.Domain.Entities;

/// <summary>
/// Represents an agent's proposal to exclusively represent a property.
/// This is a commission-based offer, not a purchase offer.
/// </summary>
public class AgentProposal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListingId { get; set; }
    public Guid AgentId { get; set; }

    // Commission Proposal
    public decimal CommissionRate { get; set; }       // e.g., 1.8 (percentage)
    public decimal? CommissionFlat { get; set; }      // Alternative fixed fee
    public string CommissionNotes { get; set; } = string.Empty;

    // Proposal Details
    public string MarketingPlan { get; set; } = string.Empty;
    public string Strategy { get; set; } = "Private Treaty"; // Auction, Private Treaty, Offers over, etc.
    public string SuggestedPriceRange { get; set; } = string.Empty;
    public string Region { get; set; } = "Local"; // Local, National, International, etc.
    public ExclusivityScope Exclusivity { get; set; } = ExclusivityScope.LocalOnly;
    public string SellingPoints { get; set; } = string.Empty;
    public int ProposedCampaignDays { get; set; } = 30;

    // Status
    public ProposalStatus Status { get; set; } = ProposalStatus.Pending;
    public string? RejectionReason { get; set; }

    // Timestamps
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAt { get; set; }
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(7);

    // Navigation
    public Listing Listing { get; set; } = null!;
    public Agent Agent { get; set; } = null!;
}

public enum ProposalStatus
{
    Pending,
    Accepted,
    Rejected,
    Expired,
    Withdrawn
}

public enum ExclusivityScope
{
    Full,       // Agent handles everything (traditional)
    LocalOnly,  // Agent handles local state/region, Platform handles rest
    NonExclusive // Open listing
}
