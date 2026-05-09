using Aigents.Domain.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;

namespace Aigents.Web.Components.Account;

/// <summary>
/// Tiny helper endpoints invoked by the account pages — sign-out and any
/// future external-callback hooks. Keeps the .razor pages free of
/// HttpContext-mutating code.
/// </summary>
internal static class IdentityComponentsEndpointRouteBuilderExtensions
{
    public static IEndpointConventionBuilder MapAdditionalIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("");

        group.MapPost("/Account/Logout", async (
            HttpContext context,
            SignInManager<User> signInManager,
            [Microsoft.AspNetCore.Mvc.FromForm] string returnUrl) =>
        {
            await signInManager.SignOutAsync();
            return TypedResults.LocalRedirect($"~/{returnUrl}");
        });

        return group;
    }
}
