using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Aigents.Infrastructure.PropertyData;

/// <summary>
/// Service for gathering property intelligence via web research
/// </summary>
public interface IPropertyIntelligenceService
{
    Task<PropertyIntelligenceResult> GetPropertyIntelligenceAsync(
        PropertyIntelligenceRequest request,
        CancellationToken ct = default);
}

public class PropertyIntelligenceService : IPropertyIntelligenceService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PropertyIntelligenceService> _logger;

    public PropertyIntelligenceService(HttpClient httpClient, ILogger<PropertyIntelligenceService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<PropertyIntelligenceResult> GetPropertyIntelligenceAsync(
        PropertyIntelligenceRequest request,
        CancellationToken ct = default)
    {
        var result = new PropertyIntelligenceResult();

        try
        {
            // Search 1: Property-specific data from real estate sites
            var propertyData = await SearchPropertyDataAsync(request.Address, ct);
            if (propertyData != null)
            {
                result.Bedrooms = propertyData.Bedrooms;
                result.Bathrooms = propertyData.Bathrooms;
                result.CarSpaces = propertyData.CarSpaces;
                result.LandSize = propertyData.LandSize;
                result.PropertyType = propertyData.PropertyType;
                result.EstimatedValueLow = propertyData.EstimatedValueLow;
                result.EstimatedValueHigh = propertyData.EstimatedValueHigh;
                result.LastSalePrice = propertyData.LastSalePrice;
                result.LastSaleDate = propertyData.LastSaleDate;
                result.Sources.AddRange(propertyData.Sources);
                result.ResearchSummary = propertyData.Summary;
            }

            // Search 2: Suburb market data
            var suburb = ExtractSuburb(request.Address);
            if (!string.IsNullOrEmpty(suburb))
            {
                var marketData = await SearchMarketDataAsync(suburb, ct);
                if (marketData != null)
                {
                    result.MedianSuburbPrice = marketData.MedianPrice;
                    result.RentalYield = marketData.RentalYield;
                    result.DaysOnMarket = marketData.DaysOnMarket;
                    result.SuburbGrowthRate = marketData.GrowthRate;
                    result.Sources.AddRange(marketData.Sources);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching property intelligence for {Address}", request.Address);
            // Return partial results rather than failing completely
        }

        // Ensure we have at least some default sources if nothing found
        if (result.Sources.Count == 0)
        {
            result.Sources.Add(new IntelligenceSource
            {
                Name = "Web Research",
                Details = "Limited data available",
                Year = DateTime.Now.Year.ToString(),
                Icon = "🔍"
            });
        }

        return result;
    }

    private async Task<PropertySearchResult?> SearchPropertyDataAsync(string address, CancellationToken ct)
    {
        try
        {
            // Call the Google search API endpoint (assumes it's configured)
            var searchQuery = $"{address} property bedrooms bathrooms sale history site:domain.com.au OR site:realestate.com.au";

            var response = await _httpClient.GetAsync(
                $"/api/search?q={Uri.EscapeDataString(searchQuery)}", ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Search API returned {StatusCode}", response.StatusCode);
                return null;
            }

            var searchResult = await response.Content.ReadFromJsonAsync<WebSearchResponse>(ct);
            if (searchResult == null) return null;

            return ParsePropertyData(searchResult, address);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to search property data for {Address}", address);
            return null;
        }
    }

    private async Task<MarketSearchResult?> SearchMarketDataAsync(string suburb, CancellationToken ct)
    {
        try
        {
            var searchQuery = $"{suburb} property market report median price 2024";

            var response = await _httpClient.GetAsync(
                $"/api/search?q={Uri.EscapeDataString(searchQuery)}", ct);

            if (!response.IsSuccessStatusCode) return null;

            var searchResult = await response.Content.ReadFromJsonAsync<WebSearchResponse>(ct);
            if (searchResult == null) return null;

            return ParseMarketData(searchResult, suburb);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to search market data for {Suburb}", suburb);
            return null;
        }
    }

    private PropertySearchResult ParsePropertyData(WebSearchResponse response, string address)
    {
        var result = new PropertySearchResult();

        // Parse the summary text for property attributes
        var summary = response.Summary ?? "";
        result.Summary = summary;

        // Extract bedrooms (e.g., "4 bedrooms", "4-bed", "4 bed")
        var bedroomMatch = Regex.Match(summary, @"(\d+)\s*(?:bed(?:room)?s?|Bed(?:room)?s?)", RegexOptions.IgnoreCase);
        if (bedroomMatch.Success && int.TryParse(bedroomMatch.Groups[1].Value, out var beds))
            result.Bedrooms = beds;

        // Extract bathrooms
        var bathroomMatch = Regex.Match(summary, @"(\d+)\s*(?:bath(?:room)?s?|Bath(?:room)?s?)", RegexOptions.IgnoreCase);
        if (bathroomMatch.Success && int.TryParse(bathroomMatch.Groups[1].Value, out var baths))
            result.Bathrooms = baths;

        // Extract car spaces
        var carMatch = Regex.Match(summary, @"(\d+)\s*(?:car\s*(?:space)?s?|Car\s*(?:space)?s?|garage|parking)", RegexOptions.IgnoreCase);
        if (carMatch.Success && int.TryParse(carMatch.Groups[1].Value, out var cars))
            result.CarSpaces = cars;

        // Extract land size (e.g., "650 sqm", "650m²", "650 square metres")
        var landMatch = Regex.Match(summary, @"(\d+(?:,\d+)?)\s*(?:sqm|m²|square\s*met(?:re|er)s?)", RegexOptions.IgnoreCase);
        if (landMatch.Success)
            result.LandSize = landMatch.Groups[1].Value.Replace(",", "");

        // Extract prices (e.g., "$520,000", "$1.2M", "$1,200,000")
        var priceMatches = Regex.Matches(summary, @"\$(\d{1,3}(?:,\d{3})*(?:\.\d+)?)\s*(?:k|K|m|M)?|\$(\d+(?:\.\d+)?)\s*(?:million|Million|M)", RegexOptions.IgnoreCase);
        var prices = new List<decimal>();
        foreach (Match match in priceMatches)
        {
            if (TryParsePrice(match.Value, out var price))
                prices.Add(price);
        }

        if (prices.Count >= 2)
        {
            result.EstimatedValueLow = prices.Min();
            result.EstimatedValueHigh = prices.Max();
        }
        else if (prices.Count == 1)
        {
            result.EstimatedValueLow = prices[0] * 0.95m;
            result.EstimatedValueHigh = prices[0] * 1.05m;
        }

        // Extract last sale info
        var saleMatch = Regex.Match(summary, @"sold\s*(?:for)?\s*\$(\d{1,3}(?:,\d{3})*)\s*(?:in|on)?\s*(\w+\s+\d{4}|\d{4})", RegexOptions.IgnoreCase);
        if (saleMatch.Success)
        {
            if (TryParsePrice("$" + saleMatch.Groups[1].Value, out var salePrice))
                result.LastSalePrice = salePrice;
            result.LastSaleDate = saleMatch.Groups[2].Value;
        }

        // Extract property type
        if (Regex.IsMatch(summary, @"\b(?:house|home)\b", RegexOptions.IgnoreCase))
            result.PropertyType = "house";
        else if (Regex.IsMatch(summary, @"\b(?:apartment|unit|flat)\b", RegexOptions.IgnoreCase))
            result.PropertyType = "apartment";
        else if (Regex.IsMatch(summary, @"\btownhouse\b", RegexOptions.IgnoreCase))
            result.PropertyType = "townhouse";

        // Add sources from the search results
        if (response.Sources != null)
        {
            foreach (var source in response.Sources.Take(5))
            {
                var sourceName = ExtractDomainName(source.Url);
                result.Sources.Add(new IntelligenceSource
                {
                    Name = sourceName,
                    Details = TruncateText(source.Title ?? "Property Listing", 40),
                    Year = ExtractYear(source.Title) ?? DateTime.Now.Year.ToString(),
                    Url = source.Url,
                    Icon = GetIconForSource(sourceName)
                });
            }
        }

        return result;
    }

    private MarketSearchResult ParseMarketData(WebSearchResponse response, string suburb)
    {
        var result = new MarketSearchResult();
        var summary = response.Summary ?? "";

        // Extract median price
        var medianMatch = Regex.Match(summary, @"median\s*(?:house\s*)?price[:\s]*\$(\d{1,3}(?:,\d{3})*(?:\.\d+)?(?:\s*(?:k|m|million))?)", RegexOptions.IgnoreCase);
        if (medianMatch.Success)
            result.MedianPrice = "$" + medianMatch.Groups[1].Value;

        // Extract rental yield
        var yieldMatch = Regex.Match(summary, @"(?:rental\s*)?yield[:\s]*(\d+(?:\.\d+)?)\s*%", RegexOptions.IgnoreCase);
        if (yieldMatch.Success)
            result.RentalYield = yieldMatch.Groups[1].Value + "%";

        // Extract days on market
        var domMatch = Regex.Match(summary, @"(\d+)\s*days?\s*(?:on\s*)?market", RegexOptions.IgnoreCase);
        if (domMatch.Success)
            result.DaysOnMarket = domMatch.Groups[1].Value + " days";

        // Extract growth rate
        var growthMatch = Regex.Match(summary, @"(?:growth|change)[:\s]*([+-]?\d+(?:\.\d+)?)\s*%", RegexOptions.IgnoreCase);
        if (growthMatch.Success)
            result.GrowthRate = growthMatch.Groups[1].Value + "%";

        // Add sources
        if (response.Sources != null)
        {
            foreach (var source in response.Sources.Take(3))
            {
                var sourceName = ExtractDomainName(source.Url);
                result.Sources.Add(new IntelligenceSource
                {
                    Name = sourceName,
                    Details = "Market Report",
                    Year = DateTime.Now.Year.ToString(),
                    Url = source.Url,
                    Icon = "📊"
                });
            }
        }

        return result;
    }

    private static string ExtractSuburb(string address)
    {
        // Try to extract suburb from Australian address format
        // e.g., "123 Main St, Paddington QLD 4064" -> "Paddington QLD"
        var match = Regex.Match(address, @",\s*([A-Za-z\s]+)\s+(?:VIC|NSW|QLD|SA|WA|TAS|NT|ACT)\b", RegexOptions.IgnoreCase);
        if (match.Success)
            return match.Groups[1].Value.Trim() + " " + match.Groups[0].Value.Split(' ').Last();

        // Simpler fallback - take everything after the last comma
        var parts = address.Split(',');
        return parts.Length > 1 ? parts[^1].Trim() : "";
    }

    private static string ExtractDomainName(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "Web Source";
        try
        {
            var uri = new Uri(url);
            var host = uri.Host.Replace("www.", "");
            return char.ToUpper(host[0]) + host[1..];
        }
        catch
        {
            return "Web Source";
        }
    }

    private static string? ExtractYear(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var match = Regex.Match(text, @"\b(20\d{2})\b");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static bool TryParsePrice(string priceText, out decimal price)
    {
        price = 0;
        priceText = priceText.Replace("$", "").Replace(",", "").Trim();

        if (priceText.EndsWith("m", StringComparison.OrdinalIgnoreCase) ||
            priceText.Contains("million", StringComparison.OrdinalIgnoreCase))
        {
            priceText = priceText.Replace("million", "", StringComparison.OrdinalIgnoreCase)
                                  .Replace("m", "", StringComparison.OrdinalIgnoreCase).Trim();
            if (decimal.TryParse(priceText, out var millions))
            {
                price = millions * 1_000_000;
                return true;
            }
        }
        else if (priceText.EndsWith("k", StringComparison.OrdinalIgnoreCase))
        {
            priceText = priceText.Replace("k", "", StringComparison.OrdinalIgnoreCase).Trim();
            if (decimal.TryParse(priceText, out var thousands))
            {
                price = thousands * 1_000;
                return true;
            }
        }
        else
        {
            return decimal.TryParse(priceText, out price);
        }

        return false;
    }

    private static string TruncateText(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
    }

    private static string GetIconForSource(string sourceName)
    {
        var lower = sourceName.ToLowerInvariant();
        if (lower.Contains("domain")) return "🏠";
        if (lower.Contains("realestate")) return "💰";
        if (lower.Contains("corelogic") || lower.Contains("rp")) return "📈";
        if (lower.Contains("council")) return "🗺️";
        if (lower.Contains("google")) return "✨";
        return "📄";
    }

    // Internal DTOs for parsing
    private class PropertySearchResult
    {
        public int? Bedrooms { get; set; }
        public int? Bathrooms { get; set; }
        public int? CarSpaces { get; set; }
        public string? LandSize { get; set; }
        public string? PropertyType { get; set; }
        public decimal? EstimatedValueLow { get; set; }
        public decimal? EstimatedValueHigh { get; set; }
        public decimal? LastSalePrice { get; set; }
        public string? LastSaleDate { get; set; }
        public string? Summary { get; set; }
        public List<IntelligenceSource> Sources { get; set; } = new();
    }

    private class MarketSearchResult
    {
        public string? MedianPrice { get; set; }
        public string? RentalYield { get; set; }
        public string? DaysOnMarket { get; set; }
        public string? GrowthRate { get; set; }
        public List<IntelligenceSource> Sources { get; set; } = new();
    }

    private class WebSearchResponse
    {
        public string? Summary { get; set; }
        public List<WebSource>? Sources { get; set; }
    }

    private class WebSource
    {
        public string? Title { get; set; }
        public string? Url { get; set; }
    }
}
