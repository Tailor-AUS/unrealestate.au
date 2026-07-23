using Aigents.Infrastructure.PropertyData;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Aigents.Api.Features.Property;

/// <summary>
/// API endpoints for QLD MapsOnline property reports
/// </summary>
public static class MapsOnlineEndpoints
{
    public static IEndpointRouteBuilder MapMapsOnlineEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/maps")
            .WithTags("Property Maps & Reports");

        // Get available report types
        group.MapGet("/report-types", GetReportTypes)
            .WithName("GetReportTypes")
            .WithSummary("Get available QLD MapsOnline report types")
            .AllowAnonymous();

        // Get available feature types (ways to specify location)
        group.MapGet("/feature-types", GetFeatureTypes)
            .WithName("GetFeatureTypes")
            .WithSummary("Get available feature types for location specification")
            .AllowAnonymous();

        // Request a report by Lot/Plan
        group.MapPost("/report/lot-plan", RequestReportByLotPlan)
            .WithName("RequestReportByLotPlan")
            .WithSummary("Request a property report by Lot/Plan (e.g., 1/RP123456)")
            .RequireAuthorization();

        // Request a report by coordinates
        group.MapPost("/report/coordinates", RequestReportByCoordinates)
            .WithName("RequestReportByCoordinates")
            .WithSummary("Request a property report by coordinates")
            .RequireAuthorization();

        return routes;
    }

    private static async Task<IResult> GetReportTypes(IMapsOnlineService mapsOnlineService)
    {
        var reportTypes = await mapsOnlineService.GetReportTypesAsync();

        // Return simplified list for UI consumption
        var simplified = reportTypes.Select(r => new
        {
            r.Type,
            r.Name,
            r.Description,
            r.AllowedFeatureTypes
        });

        return Results.Ok(simplified);
    }

    private static async Task<IResult> GetFeatureTypes(IMapsOnlineService mapsOnlineService)
    {
        var featureTypes = await mapsOnlineService.GetFeatureTypesAsync();

        var simplified = featureTypes.Select(f => new
        {
            f.Type,
            f.Name,
            f.Description,
            f.GeomType
        });

        return Results.Ok(simplified);
    }

    private static async Task<IResult> RequestReportByLotPlan(
        IMapsOnlineService mapsOnlineService,
        ReportByLotPlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LotPlan))
        {
            return Results.BadRequest("LotPlan is required (e.g., '1/RP123456' or '1,RP123456')");
        }

        if (string.IsNullOrWhiteSpace(request.EmailAddress))
        {
            return Results.BadRequest("EmailAddress is required for report delivery");
        }

        var reportType = request.ReportType ?? MapsOnlineReportTypes.VegetationManagement;

        var result = await mapsOnlineService.RequestReportAsync(
            request.LotPlan,
            reportType,
            request.EmailAddress
        );

        if (result.Success)
        {
            return Results.Ok(new
            {
                Success = true,
                Message = $"Report requested successfully. It will be emailed to {request.EmailAddress}",
                LotPlan = request.LotPlan,
                ReportType = reportType
            });
        }

        return Results.BadRequest(new
        {
            Success = false,
            result.Message,
            Errors = result.Output
        });
    }

    private static async Task<IResult> RequestReportByCoordinates(
        IMapsOnlineService mapsOnlineService,
        ReportByCoordinatesRequest request)
    {
        if (request.Longitude == 0 || request.Latitude == 0)
        {
            return Results.BadRequest("Valid Longitude and Latitude are required");
        }

        // Validate coordinates are in Queensland
        if (request.Longitude < 138 || request.Longitude > 154 ||
            request.Latitude < -29 || request.Latitude > -10)
        {
            return Results.BadRequest("Coordinates must be within Queensland");
        }

        if (string.IsNullOrWhiteSpace(request.EmailAddress))
        {
            return Results.BadRequest("EmailAddress is required for report delivery");
        }

        var reportType = request.ReportType ?? MapsOnlineReportTypes.VegetationManagement;

        var result = await mapsOnlineService.RequestReportByCoordinatesAsync(
            request.Longitude,
            request.Latitude,
            reportType,
            request.EmailAddress
        );

        if (result.Success)
        {
            return Results.Ok(new
            {
                Success = true,
                Message = $"Report requested successfully. It will be emailed to {request.EmailAddress}",
                Coordinates = new { request.Longitude, request.Latitude },
                ReportType = reportType
            });
        }

        return Results.BadRequest(new
        {
            Success = false,
            result.Message,
            Errors = result.Output
        });
    }
}

public record ReportByLotPlanRequest(
    string LotPlan,
    string EmailAddress,
    string? ReportType = null
);

public record ReportByCoordinatesRequest(
    double Longitude,
    double Latitude,
    string EmailAddress,
    string? ReportType = null
);
