namespace Aigents.Infrastructure.PropertyData.Adapters;

/// <summary>
/// MOCK implementation of CoreLogic/RP Data.
/// Generates realistic data for development and demos without needing expensive enterprise API keys.
/// </summary>
public class MockCoreLogicAdapter : IPropertyDataProvider
{
    public string Name => "CoreLogic";

    public Task<SearchResults> SearchAsync(PropertySearchFilter filter, CancellationToken ct = default)
    {
        // Generate deterministic mock results based on query
        var results = new List<BuyerProperty>();
        var random = new Random(filter.GetHashCode());

        for (int i = 0; i < filter.PageSize; i++)
        {
            results.Add(GenerateMockProperty(i, filter.Query ?? "Unknown", random));
        }

        return Task.FromResult(new SearchResults
        {
            Items = results,
            TotalCount = 100,
            Page = filter.Page,
            PageSize = filter.PageSize
        });
    }

    public Task<BuyerProperty?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        return Task.FromResult<BuyerProperty?>(GenerateMockProperty(id.GetHashCode(), "Mock St", new Random(id.GetHashCode())));
    }

    public Task<BuyerProperty?> GetByAddressAsync(string address, CancellationToken ct = default)
    {
        return Task.FromResult<BuyerProperty?>(GenerateMockProperty(address.GetHashCode(), address, new Random(address.GetHashCode())));
    }

    public Task<SuburbProfile?> GetSuburbProfileAsync(string suburb, string state, CancellationToken ct = default)
    {
        var random = new Random(suburb.GetHashCode());
        return Task.FromResult<SuburbProfile?>(new SuburbProfile
        {
            Suburb = suburb,
            State = state,
            Postcode = "4000",
            MedianHousePrice = random.Next(800000, 2500000),
            MedianUnitPrice = random.Next(400000, 900000),
            HouseGrowthRate = (random.NextDouble() * 10) - 2, // -2% to +8%
            UnitGrowthRate = (random.NextDouble() * 8) - 1,
            DaysOnMarket = random.Next(14, 60),
            ClearanceRate = 0.60 + (random.NextDouble() * 0.3),
            DemographicsSummary = "Family-oriented demographic with high owner-occupier ratio."
        });
    }

    private BuyerProperty GenerateMockProperty(int seed, string context, Random random)
    {
        var bedrooms = random.Next(2, 6);
        var baseValue = 500000 + (bedrooms * 200000) + random.Next(-100000, 100000);

        return new BuyerProperty
        {
            Id = $"cl-{Math.Abs(seed)}",
            Source = "CoreLogic",
            Address = $"{random.Next(1, 200)} {context.Split(' ').LastOrDefault() ?? "Main"} St, {context.Split(',').FirstOrDefault() ?? "Brisbane"}",
            StreetNumber = random.Next(1, 200).ToString(),
            StreetName = context.Split(' ').LastOrDefault() ?? "Main",
            Suburb = "Brisbane",
            State = "QLD",
            Postcode = "4000",

            Type = (PropertyType)random.Next(0, 4),
            Bedrooms = bedrooms,
            Bathrooms = Math.Max(1, bedrooms - 1),
            CarSpaces = Math.Max(1, bedrooms - 2),
            LandAreaSqm = random.Next(300, 1200),
            FloorAreaSqm = random.Next(100, 400),

            IsOnMarket = random.NextDouble() > 0.8, // 20% chance on market
            Status = ListingStatus.OffMarket,

            EstimatedValue = baseValue,
            EstimatedValueLow = baseValue * 0.9m,
            EstimatedValueHigh = baseValue * 1.1m,
            EstimatedValueConfidence = "High",

            LastSalePrice = baseValue * 0.7m,
            LastSaleDate = DateTimeOffset.UtcNow.AddYears(-random.Next(2, 10)),

            RentalEstimate = baseValue * 0.004m, // Approx yield logic
            RentalYield = 4.2m
        };
    }
}


