namespace Aigents.Infrastructure.PropertyData;

/// <summary>
/// Request for property intelligence lookup
/// </summary>
public class PropertyIntelligenceRequest
{
    public required string Address { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

/// <summary>
/// Result from property intelligence research with source traceability
/// </summary>
public class PropertyIntelligenceResult
{
    // Property attributes (extracted from search)
    public int? Bedrooms { get; set; }
    public int? Bathrooms { get; set; }
    public int? CarSpaces { get; set; }
    public string? LandSize { get; set; }
    public string? PropertyType { get; set; }

    // Pricing intelligence
    public decimal? EstimatedValueLow { get; set; }
    public decimal? EstimatedValueHigh { get; set; }
    public decimal? LastSalePrice { get; set; }
    public string? LastSaleDate { get; set; }

    // Market context
    public string? MedianSuburbPrice { get; set; }
    public string? RentalYield { get; set; }
    public string? DaysOnMarket { get; set; }
    public string? SuburbGrowthRate { get; set; }

    // Source traceability - the key feature!
    public List<IntelligenceSource> Sources { get; set; } = new();

    // Raw research summary for transparency
    public string? ResearchSummary { get; set; }
}

/// <summary>
/// Source attribution for traceability and auditability
/// </summary>
public class IntelligenceSource
{
    public required string Name { get; set; }       // e.g. "Domain.com.au"
    public required string Details { get; set; }     // e.g. "Past Auction Listing"
    public required string Year { get; set; }        // e.g. "2024"
    public string? Url { get; set; }                 // Clickable link for traceability
    public string Icon { get; set; } = "📄";         // Emoji icon
}
