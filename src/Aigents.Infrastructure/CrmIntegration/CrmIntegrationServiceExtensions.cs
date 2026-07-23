using Aigents.Infrastructure.CrmIntegration.Adapters;
using Microsoft.Extensions.DependencyInjection;

namespace Aigents.Infrastructure.CrmIntegration;

/// <summary>
/// Extension methods for registering CRM integration services
/// </summary>
public static class CrmIntegrationServiceExtensions
{
    /// <summary>
    /// Add CRM integration services to the service collection.
    /// Registers all CRM adapters and the integration hub.
    /// </summary>
    public static IServiceCollection AddCrmIntegration(this IServiceCollection services)
    {
        // Register HTTP clients for each CRM
        services.AddHttpClient<RexCrmAdapter>();
        services.AddHttpClient<AgentBoxCrmAdapter>();
        services.AddHttpClient<VaultReCrmAdapter>();

        // Register adapters
        services.AddSingleton<ICrmAdapter, RexCrmAdapter>();
        services.AddSingleton<ICrmAdapter, AgentBoxCrmAdapter>();
        services.AddSingleton<ICrmAdapter, VaultReCrmAdapter>();

        // Register the integration hub
        services.AddScoped<ICrmIntegrationHub, CrmIntegrationHub>();

        // Register default settings repository (in-memory for dev, use distributed for prod)
        services.AddSingleton<IAgentCrmSettingsRepository, InMemoryCrmSettingsRepository>();

        return services;
    }

    /// <summary>
    /// Add CRM integration with a custom settings repository.
    /// Use this when you have your own storage mechanism for credentials.
    /// </summary>
    public static IServiceCollection AddCrmIntegration<TSettingsRepo>(this IServiceCollection services)
        where TSettingsRepo : class, IAgentCrmSettingsRepository
    {
        services.AddCrmIntegration();
        services.AddScoped<IAgentCrmSettingsRepository, TSettingsRepo>();

        return services;
    }
}
