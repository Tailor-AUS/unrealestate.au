using Aigents.Infrastructure.Data;
using Aigents.Infrastructure.Services.AI;
using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Aigents.Api.Features.Listings;

public class GetPropertyReport : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/listings/report", async (ISender sender, [FromQuery] string address) =>
        {
            var result = await sender.Send(new Query(address));
            return Results.Ok(result);
        });
    }

    public record Query(string Address) : IRequest<Response>;

    public record Response(
        string Address,
        string EstimatedValue,
        int Confidence,
        List<ComparableSale> Comparables,
        List<string> KeyFeatures,
        string DevelopmentPotential,
        string AiSummary
    );

    public record ComparableSale(string Address, string Price, string Date, string Features);

    public class Handler : IRequestHandler<Query, Response>
    {
        private readonly IAiService _aiService;

        public Handler(IAiService aiService)
        {
            _aiService = aiService;
        }

        public async Task<Response> Handle(Query request, CancellationToken cancellationToken)
        {
            // Simulate AI Analysis delay
            await Task.Delay(1500, cancellationToken);

            // In a real app, this would query CoreLogic / RP Data
            // For now, we mock realistic data based on the input address

            var comps = new List<ComparableSale>
            {
                new("24 Park Avenue", "$1,450,000", "Sold 2 weeks ago", "4 Bed • 2 Bath • 607m²"),
                new("88 Smith Street", "$1,380,000", "Sold 1 month ago", "3 Bed • 2 Bath • Renovated"),
                new("12 Highland Drive", "$1,525,000", "Sold 3 months ago", "5 Bed • 3 Bath • Pool")
            };

            var features = new List<string>
            {
                "High Ceilings",
                "North-East Aspect",
                "Wide Frontage (20m)",
                "School Catchment Zone"
            };

            return new Response(
                Address: request.Address,
                EstimatedValue: "$1.4M - $1.55M",
                Confidence: 85,
                Comparables: comps,
                KeyFeatures: features,
                DevelopmentPotential: "LMR2 Zoning - Potential for townhouse subdivision (STCA). 20m frontage allows for splitter block potential.",
                AiSummary: $"Based on recent market activity in {ExtractSuburb(request.Address)}, your property is positioned in a high-demand bracket. " +
                           "Buyers are currently paying a premium for renovated turn-key homes in this area. " +
                           "The 20m frontage is a significant value-add for developers."
            );
        }

        private string ExtractSuburb(string address)
        {
            var parts = address.Split(',');
            return parts.Length > 1 ? parts[^1].Trim() : "Robina"; // Default fallback
        }
    }
}
