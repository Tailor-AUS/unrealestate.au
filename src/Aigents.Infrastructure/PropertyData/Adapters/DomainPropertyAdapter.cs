using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Aigents.Infrastructure.PropertyData.Adapters;

/// <summary>
/// Adapter for Domain.com.au API
/// </summary>
public class DomainPropertyAdapter : IPropertyDataProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DomainPropertyAdapter> _logger;
    private const string BaseUrl = "https://api.domain.com.au/v1";

    public string Name => "Domain";

    public DomainPropertyAdapter(HttpClient httpClient, ILogger<DomainPropertyAdapter> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task<SearchResults> SearchAsync(PropertySearchFilter filter, CancellationToken ct = default)
    {
        // Construct Domain Listing Search Request
        var request = new
        {
            listingType = "Sale",
            pageNumber = filter.Page,
            pageSize = filter.PageSize,
            minBedrooms = filter.MinBedrooms,
            minBathrooms = filter.MinBathrooms,
            minCarspaces = filter.MinCarSpaces,
            minPrice = filter.MinPrice,
            maxPrice = filter.MaxPrice,
            locations = new[]
            {
                new { suburb = filter.Suburb ?? "Brisbane", state = filter.State ?? "QLD", postcode = filter.Postcode }
            }
        };

        try
        {
            // Note: In a real implementation, we would POST to /listings/residential/_search
            // For now, we'll return mock data to avoid 401s until API keys are configured
            // var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/listings/residential/_search", request, ct);

            _logger.LogInformation("Searching Domain for {Suburb}", filter.Suburb);

            // Mock response for dev
            return Task.FromResult(CreateMockSearchResults(filter));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Domain search failed");
            return Task.FromResult(new SearchResults());
        }
    }

    public Task<BuyerProperty?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        try
        {
            // GET /listings/{id}
            // Mocking for now
            return Task.FromResult<BuyerProperty?>(new BuyerProperty
            {
                Id = id,
                Source = "Domain",
                Address = "123 Sample Street, Brisbane QLD 4000",
                Headline = "Stunning City Views",
                Description = "This beautiful apartment offers...",
                Type = PropertyType.Apartment,
                Bedrooms = 2,
                Bathrooms = 2,
                CarSpaces = 1,
                IsOnMarket = true,
                Status = ListingStatus.ForSale,
                PriceDisplay = "Offers over $600k",
                MainImageUrl = "https://images.unsplash.com/photo-1564013799919-ab600027ffc6?w=800&q=80"
            });
        }
        catch
        {
            return Task.FromResult<BuyerProperty?>(null);
        }
    }

    public Task<BuyerProperty?> GetByAddressAsync(string address, CancellationToken ct = default)
    {
        // Domain isn't great for address lookups unless you use AddressLocators
        // Returning null as Domain is primarily listing-centric
        return Task.FromResult<BuyerProperty?>(null);
    }

    public Task<SuburbProfile?> GetSuburbProfileAsync(string suburb, string state, CancellationToken ct = default)
    {
        // GET /suburbPerformanceStatistics
        return Task.FromResult<SuburbProfile?>(new SuburbProfile
        {
            Suburb = suburb,
            State = state,
            Postcode = "4000",
            MedianHousePrice = 1100000,
            MedianUnitPrice = 550000,
            HouseGrowthRate = 5.2,
            UnitGrowthRate = 2.1,
            ClearanceRate = 0.65,
            DaysOnMarket = 28
        });
    }

    private SearchResults CreateMockSearchResults(PropertySearchFilter filter)
    {
        var results = new List<BuyerProperty>();
        var count = filter.PageSize;

        for (int i = 0; i < count; i++)
        {
            results.Add(new BuyerProperty
            {
                Id = $"dom-{i}",
                Source = "Domain",
                Address = $"{10 + i} {filter.Suburb ?? "Brisbane"} St, {filter.Suburb ?? "Brisbane"} {filter.State ?? "QLD"}",
                Type = PropertyType.House,
                Bedrooms = 3 + (i % 3),
                Bathrooms = 2,
                CarSpaces = 2,
                IsOnMarket = true,
                Status = ListingStatus.ForSale,
                PriceDisplay = $"${800 + (i * 50)}k",
                PriceLow = (800 + (i * 50)) * 1000,
                PriceHigh = (850 + (i * 50)) * 1000,
                Headline = "Rare Opportunity in Prime Location",
                AgencyName = "Ray White Brisbane",
                MainImageUrl = $"https://images.unsplash.com/photo-1570129477492-45c003edd2be?w=800&q=80&fit=crop&seed={i}"
            });
        }

        return new SearchResults
        {
            Items = results,
            TotalCount = 150,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
    }
}
