using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ParcelRoutingSystem.Api.Contracts;
using ParcelRoutingSystem.Api.Http;
using ParcelRoutingSystem.Api.Security;
using ParcelRoutingSystem.Application.Common;
using ParcelRoutingSystem.Application.Routing;

namespace ParcelRoutingSystem.Api.Controllers;

/// <summary>
/// Exposes the authenticated single-parcel routing boundary while leaving all
/// business decisions and persistence inside the application use case.
/// </summary>
[ApiController]
[Route("api/parcels")]
[Authorize(Policy = AuthorizationPolicies.Operator)]
public sealed class ParcelsController : ControllerBase
{
    private readonly RouteParcelUseCase _routeParcel;

    /// <summary>
    /// Creates the thin routing controller around the application coordinator.
    /// </summary>
    /// <param name="routeParcel">The idempotent parcel-routing use case.</param>
    public ParcelsController(RouteParcelUseCase routeParcel)
    {
        _routeParcel = routeParcel;
    }

    /// <summary>
    /// Routes, stores, and explains one validated parcel or replays the original
    /// result when the same key and normalized facts are retried.
    /// </summary>
    /// <param name="request">The public parcel facts.</param>
    /// <param name="cancellationToken">Cancels request and database work.</param>
    /// <returns>The immutable explainable routing response.</returns>
    [HttpPost("route")]
    [EnableRateLimiting(ApiRateLimitPolicies.Routing)]
    [ProducesResponseType<RouteParcelResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RouteParcelResponse>> RouteAsync(
        RouteParcelRequest request,
        CancellationToken cancellationToken)
    {
        var attributes = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(request.OperatorReference))
        {
            attributes["operatorReference"] = request.OperatorReference.Trim();
        }

        var command = new RouteParcelCommand(
            HttpRequestMetadata.GetIdempotencyKey(HttpContext),
            request.WeightKilograms,
            request.DeclaredValueEuros,
            request.DestinationCountry,
            attributes,
            HttpRequestMetadata.Create(HttpContext),
            BatchId: null,
            BatchRowId: null);
        RouteParcelResult result = await _routeParcel.ExecuteAsync(
            command,
            cancellationToken);

        return Ok(ApiContractMapper.ToResponse(result));
    }
}
