using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;

namespace Aigents.Web.Components.Account;

internal sealed class IdentityRedirectManager(NavigationManager nav)
{
    public const string StatusCookieName = "unrealestate-status-message";

    [DoesNotReturn]
    public void RedirectTo(string? uri)
    {
        uri ??= "/";
        if (!Uri.IsWellFormedUriString(uri, UriKind.Relative))
            uri = nav.ToBaseRelativePath(uri);

        nav.NavigateTo(uri);
        throw new InvalidOperationException("Expected NavigateTo to throw a NavigationException.");
    }

    [DoesNotReturn]
    public void RedirectToWithStatus(string uri, string message, HttpContext context)
    {
        context.Response.Cookies.Append(StatusCookieName, message, new CookieOptions
        {
            IsEssential = true,
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = true,
            MaxAge = TimeSpan.FromSeconds(5),
        });
        RedirectTo(uri);
    }

    [DoesNotReturn]
    public void RedirectToCurrentPage()
    {
        var currentPath = nav.ToAbsoluteUri(nav.Uri).GetLeftPart(UriPartial.Path);
        RedirectTo(currentPath);
    }

    public string GetUriWithQueryParameters(string uri, IReadOnlyDictionary<string, object?> queryParameters)
        => nav.GetUriWithQueryParameters(uri, queryParameters);
}
