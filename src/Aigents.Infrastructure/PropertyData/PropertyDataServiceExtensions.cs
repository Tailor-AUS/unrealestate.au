using Aigents.Infrastructure.PropertyData.Adapters;
using Microsoft.Extensions.DependencyInjection;

namespace Aigents.Infrastructure.PropertyData;

public static class PropertyDataServiceExtensions
{
    public static IServiceCollection AddPropertyDataServices(this IServiceCollection services)
    {
        // Adapters
        services.AddHttpClient<DomainPropertyAdapter>();

        services.AddSingleton<IPropertyDataProvider, DomainPropertyAdapter>();
        services.AddSingleton<IPropertyDataProvider, MockCoreLogicAdapter>();

        // Orchestrator
        services.AddScoped<IPropertyDataService, PropertyDataService>();

        // Property Intelligence (web research)
        services.AddHttpClient<IPropertyIntelligenceService, PropertyIntelligenceService>();

        // QLD MapsOnline Service for property reports
        services.AddHttpClient<IMapsOnlineService, MapsOnlineService>();

        return services;
    }
}

