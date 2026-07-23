namespace Aigents.Domain.Entities;

/// <summary>
/// A privacy-minimised, durable record of a meaningful product action.
/// UserId and optional ListingId are opaque identifiers; event rows never
/// contain names, email addresses, search text, messages, or other PII.
/// </summary>
public sealed class ProductEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ListingId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}

/// <summary>
/// Stable product-event vocabulary used to calculate rolling active users.
/// Renaming an event is a data migration, not a cosmetic refactor.
/// </summary>
public static class ProductEventNames
{
    public const string SearchPerformed = "search.performed";
    public const string ListingViewed = "listing.viewed";
    public const string ListingCreated = "listing.created";
    public const string ListingUpdated = "listing.updated";
    public const string AiChatStarted = "ai_chat.started";
    public const string EnquirySubmitted = "enquiry.submitted";
    public const string InspectionBooked = "inspection.booked";
    public const string OfferSubmitted = "offer.submitted";
    public const string AgentProposalSubmitted = "agent_proposal.submitted";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        StringComparer.Ordinal)
    {
        SearchPerformed,
        ListingViewed,
        ListingCreated,
        ListingUpdated,
        AiChatStarted,
        EnquirySubmitted,
        InspectionBooked,
        OfferSubmitted,
        AgentProposalSubmitted
    };
}
