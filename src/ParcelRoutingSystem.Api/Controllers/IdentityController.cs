using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ParcelRoutingSystem.Api.Contracts;
using ParcelRoutingSystem.Api.Security;

namespace ParcelRoutingSystem.Api.Controllers;

/// <summary>
/// Exposes the already authenticated server identity so role-aware controls
/// match authorization without creating a local account system.
/// </summary>
[ApiController]
[Route("api/identity")]
[Authorize(Policy = AuthorizationPolicies.Authenticated)]
public sealed class IdentityController : ControllerBase
{
    /// <summary>
    /// Returns bounded display identity and allow-listed roles from validated
    /// claims; it never returns bearer tokens or provider payloads.
    /// </summary>
    [HttpGet("current")]
    [EnableRateLimiting(ApiRateLimitPolicies.Query)]
    [ProducesResponseType<CurrentIdentityResponse>(StatusCodes.Status200OK)]
    public ActionResult<CurrentIdentityResponse> GetCurrent()
    {
        string actorId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? "authenticated-user";
        string displayName = User.Identity?.Name ?? actorId;
        string[] roles =
        [
            .. User.Claims
                .Where(
                    claim => claim.Type is ClaimTypes.Role or "roles")
                .Select(claim => claim.Value)
                .Where(
                    role => role is AuthorizationPolicies.OperatorRole
                        or AuthorizationPolicies.InsuranceApproverRole
                        or AuthorizationPolicies.RuleAdministratorRole)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        bool isDevelopment = string.Equals(
            User.Identity?.AuthenticationType,
            DevelopmentAuthenticationHandler.SchemeName,
            StringComparison.Ordinal);

        return Ok(
            new CurrentIdentityResponse(
                actorId,
                displayName,
                roles,
                isDevelopment));
    }
}
