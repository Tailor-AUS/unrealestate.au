using Aigents.Infrastructure.Growth;
using Carter;

namespace Aigents.Api.Features.Growth;

/// <summary>
/// Operator-facing aggregate growth metrics. Returns counts only — never
/// user-level event rows or PII.
/// </summary>
public sealed class GrowthEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/growth/metrics", async (
            IProductEventRecorder productEvents,
            CancellationToken cancellationToken) =>
        {
            var metrics = await productEvents.GetGrowthMetricsAsync(cancellationToken);
            return Results.Ok(new
            {
                metrics.MonthlyActiveUsers,
                metrics.WeeklyActiveUsers,
                metrics.CalculatedAt,
                definition =
                    "Rolling authenticated MAU/WAU from ProductEvents. See docs/metrics.md."
            });
        })
        .RequireAuthorization()
        .WithName("GetGrowthMetrics")
        .WithTags("Growth")
        .WithOpenApi();
    }
}
