using Microsoft.Extensions.Logging;

namespace Aigents.Infrastructure.PropertyData;

public interface IPropertyDataService
{
    Task<SearchResults> SearchPropertiesAsync(PropertySearchFilter filter, CancellationToken ct = default);
    Task<BuyerProperty?> GetPropertyDetailsAsync(string id, CancellationToken ct = default);
    Task<BuyerProperty?> EnrichPropertyDataAsync(BuyerProperty property, CancellationToken ct = default);
    Task<SuburbProfile?> GetSuburbInsightsAsync(string suburb, string state, CancellationToken ct = default);
}

public class PropertyDataService : IPropertyDataService
{
    private readonly IEnumerable<IPropertyDataProvider> _providers;
    private readonly ILogger<PropertyDataService> _logger;

    public PropertyDataService(IEnumerable<IPropertyDataProvider> providers, ILogger<PropertyDataService> logger)
    {
        _providers = providers;
        _logger = logger;
    }

    public async Task<SearchResults> SearchPropertiesAsync(PropertySearchFilter filter, CancellationToken ct = default)
    {
        // Strategy:
        // 1. If searching on-market, rely primarily on Domain adapter
        // 2. If searching off-market, rely on CoreLogic adapter
        // 3. Merge results if needed (advanced)

        var provider = GetProvider(filter.IncludeOffMarket ? "corelogic" : "domain");
        if (provider == null)
        {
            // Fallback to first available
            provider = _providers.FirstOrDefault();
        }

        if (provider == null)
        {
            return new SearchResults();
        }

        return await provider.SearchAsync(filter, ct);
    }

    public async Task<BuyerProperty?> GetPropertyDetailsAsync(string id, CancellationToken ct = default)
    {
        // Try to find the source provider from the ID prefix or metadata
        // For now, iterate
        foreach (var provider in _providers)
        {
            var result = await provider.GetByIdAsync(id, ct);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    public async Task<BuyerProperty?> EnrichPropertyDataAsync(BuyerProperty property, CancellationToken ct = default)
    {
        // If we have a listing from Domain, try to get AVM from CoreLogic
        if (property.Source == "Domain")
        {
            var coreLogic = GetProvider("corelogic");
            if (coreLogic != null)
            {
                try
                {
                    var enriched = await coreLogic.GetByAddressAsync(property.Address, ct);
                    if (enriched != null)
                    {
                        // Merge fields
                        property.EstimatedValue = enriched.EstimatedValue ?? property.EstimatedValue;
                        property.EstimatedValueLow = enriched.EstimatedValueLow ?? property.EstimatedValueLow;
                        property.EstimatedValueHigh = enriched.EstimatedValueHigh ?? property.EstimatedValueHigh;
                        property.LastSalePrice = enriched.LastSalePrice ?? property.LastSalePrice;
                        property.LastSaleDate = enriched.LastSaleDate ?? property.LastSaleDate;
                        property.LandAreaSqm = enriched.LandAreaSqm ?? property.LandAreaSqm;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to enrich property data for {Address}", property.Address);
                }
            }
        }
        return property;
    }

    public async Task<SuburbProfile?> GetSuburbInsightsAsync(string suburb, string state, CancellationToken ct = default)
    {
        var provider = GetProvider("domain") ?? _providers.FirstOrDefault();
        if (provider == null) return null;

        return await provider.GetSuburbProfileAsync(suburb, state, ct);
    }

    private IPropertyDataProvider? GetProvider(string name)
    {
        return _providers.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
