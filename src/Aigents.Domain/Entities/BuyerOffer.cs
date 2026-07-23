namespace Aigents.Domain.Entities;

/// <summary>
/// Represents a purchase offer from a buyer for a property
/// </summary>
public class BuyerOffer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListingId { get; set; }
    public Guid? BuyerId { get; set; }              // If registered user
    public Guid? SubmittedByAgentId { get; set; }   // If via agent

    // Buyer Details (if not registered)
    public string BuyerName { get; set; } = string.Empty;
    public string BuyerEmail { get; set; } = string.Empty;
    public string BuyerPhone { get; set; } = string.Empty;

    // Offer Details
    public decimal OfferAmount { get; set; }
    public int SettlementDays { get; set; } = 30;
    public OfferConditions Conditions { get; set; } = OfferConditions.None;
    public string ConditionNotes { get; set; } = string.Empty;
    public DateTime OfferExpiresAt { get; set; } = DateTime.UtcNow.AddDays(3);

    // Status
    public OfferStatus Status { get; set; } = OfferStatus.Pending;
    public string? ResponseNotes { get; set; }

    // Timestamps
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAt { get; set; }

    // Navigation
    public Listing Listing { get; set; } = null!;
    public User? Buyer { get; set; }
    public Agent? SubmittedByAgent { get; set; }
}

[Flags]
public enum OfferConditions
{
    None = 0,
    SubjectToFinance = 1,
    SubjectToInspection = 2,
    SubjectToSale = 4,
    CashUnconditional = 8
}

public enum OfferStatus
{
    Pending,
    Accepted,
    Rejected,
    Countered,
    Withdrawn,
    Expired
}
