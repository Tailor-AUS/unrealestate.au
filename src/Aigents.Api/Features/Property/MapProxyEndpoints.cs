using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Aigents.Api.Features.Property;

/// <summary>
/// Proxy endpoints for QLD Government WMS services that are CORS-blocked
/// </summary>
public static class MapProxyEndpoints
{
    private static readonly HttpClient _httpClient = new();

    public static IEndpointRouteBuilder MapMapProxyEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/map-proxy")
            .WithTags("Map Proxy");

        // Proxy QLD Cadastre WMS tiles
        group.MapGet(
                "/qld-cadastre",
                (Func<HttpContext, Task<IResult>>)ProxyCadastreTile)
            .WithName("ProxyCadastreTile")
            .WithSummary("Proxy QLD cadastre WMS tiles (bypasses CORS)")
            .AllowAnonymous();

        return routes;
    }

    private static async Task<IResult> ProxyCadastreTile(HttpContext context)
    {
        try
        {
            // Get WMS parameters from query string
            var query = context.Request.QueryString.Value ?? "";

            // Build the QLD WMS URL
            var wmsUrl = $"https://spatial-gis.information.qld.gov.au/arcgis/services/PlanningCadastre/LandParcelPropertyFramework/MapServer/WMSServer{query}";

            // Fetch from QLD Government
            var response = await _httpClient.GetAsync(wmsUrl);

            if (!response.IsSuccessStatusCode)
            {
                return Results.StatusCode((int)response.StatusCode);
            }

            var content = await response.Content.ReadAsByteArrayAsync();
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "image/png";

            return Results.File(content, contentType);
        }
        catch (Exception ex)
        {
            return Results.Problem($"Proxy error: {ex.Message}");
        }
    }
}
