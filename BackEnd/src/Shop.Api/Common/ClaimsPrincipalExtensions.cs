using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Shop.Api.Common;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out Guid id)
            ? id
            : throw new InvalidOperationException("Authenticated principal has no valid 'sub' claim.");
}
