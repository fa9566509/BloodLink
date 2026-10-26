using System.Security.Claims;

namespace AF.Blood.Api.Endpoints;

public static class ClaimsPrincipalExtensions
{
    /// <summary>Extracts the authenticated user's id from the JWT sub claim.</summary>
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id)
            ? id
            : throw new InvalidOperationException("Authenticated principal has no valid subject claim.");
    }
}
