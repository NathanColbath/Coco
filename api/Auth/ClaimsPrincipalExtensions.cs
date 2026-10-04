using System.Security.Claims;

namespace ComicStudio.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    public static string GetUserId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.FindFirstValue("sub")
        ?? throw new InvalidOperationException("The authenticated principal has no user id claim.");
}
