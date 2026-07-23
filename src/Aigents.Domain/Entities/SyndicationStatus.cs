namespace Aigents.Domain.Entities;

/// <summary>
/// Tracks the syndication status of a listing on external platforms
/// </summary>
public class SyndicationStatus
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListingId { get; set; }

    public SyndicationPlatform Platform { get; set; }
    public SyndicationState State { get; set; } = SyndicationState.Pending;

    public string? ExternalListingId { get; set; }  // ID on the external platform
    public string? ExternalUrl { get; set; }        // Link to view on platform
    public string? ErrorMessage { get; set; }       // If failed

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }
    public DateTime? LastSyncAt { get; set; }

    // Navigation
    public Listing Listing { get; set; } = null!;
}

public enum SyndicationPlatform
{
    FacebookMarketplace,
    Gumtree,
    Domain,
    RealEstateComAu,
    RPData,
    OpenAgent,
    Homely,
    Soho,
    RealtyComAu,
    Juwai,
    Zillow,
    Ebay,
    Allhomes,
    Reiwa,
    RealEstateView
}

public enum SyndicationState
{
    Pending,    // Queued for syndication
    Processing, // Currently syndicating
    Live,       // Successfully published
    Failed,     // Failed to publish
    Removed     // Delisted from platform
}
