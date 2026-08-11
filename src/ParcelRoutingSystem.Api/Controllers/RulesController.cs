using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ParcelRoutingSystem.Api.Contracts;
using ParcelRoutingSystem.Api.Http;
using ParcelRoutingSystem.Api.Security;
using ParcelRoutingSystem.Application.Rules;
using ParcelRoutingSystem.Domain.Routing;

namespace ParcelRoutingSystem.Api.Controllers;

/// <summary>
/// Exposes the active immutable policy as a read-only authenticated contract so
/// the browser never duplicates routing thresholds.
/// </summary>
[ApiController]
[Route("api/rules")]
[Authorize(Policy = AuthorizationPolicies.Authenticated)]
public sealed class RulesController : ControllerBase
{
    private readonly RuleSetLifecycleUseCase _rules;

    /// <summary>
    /// Creates the active-policy reader around the application lifecycle use
    /// case.
    /// </summary>
    /// <param name="rules">The validated rule-set lifecycle coordinator.</param>
    public RulesController(RuleSetLifecycleUseCase rules)
    {
        _rules = rules;
    }

    /// <summary>
    /// Returns the single active validated rule-set version and controlled
    /// display conditions.
    /// </summary>
    /// <param name="cancellationToken">Cancels the persistence read.</param>
    /// <returns>The active immutable policy.</returns>
    [HttpGet("active")]
    [EnableRateLimiting(ApiRateLimitPolicies.Query)]
    [ProducesResponseType<ActiveRuleSetResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ActiveRuleSetResponse>> GetActiveAsync(
        CancellationToken cancellationToken)
    {
        StoredRuleSet active = await _rules.RequireActiveAsync(cancellationToken);
        return Ok(ApiContractMapper.ToResponse(active));
    }

    /// <summary>
    /// Returns bounded immutable version history for monitoring and rollback
    /// selection.
    /// </summary>
    [HttpGet]
    [EnableRateLimiting(ApiRateLimitPolicies.Query)]
    [ProducesResponseType<IReadOnlyList<ActiveRuleSetResponse>>(
        StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ActiveRuleSetResponse>>>
        GetRecentAsync(
            [FromQuery] int limit = 20,
            CancellationToken cancellationToken = default)
    {
        IReadOnlyList<StoredRuleSet> versions = await _rules.GetRecentAsync(
            limit,
            cancellationToken);
        return Ok(versions.Select(ApiContractMapper.ToResponse).ToArray());
    }

    /// <summary>
    /// Validates and stores one constrained immutable draft. Only numeric
    /// boundaries are accepted; arbitrary expressions cannot enter the system.
    /// </summary>
    [HttpPost("drafts")]
    [Authorize(Policy = AuthorizationPolicies.RuleAdministrator)]
    [EnableRateLimiting(ApiRateLimitPolicies.Approval)]
    [ProducesResponseType<ActiveRuleSetResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ActiveRuleSetResponse>> CreateDraftAsync(
        [FromBody] CreateRuleDraftRequest request,
        CancellationToken cancellationToken)
    {
        RuleSetDefinition definition = CreateDefinition(request);
        RuleSetWriteResult result = await _rules.CreateDraftAsync(
            definition,
            HttpRequestMetadata.GetIdempotencyKey(HttpContext),
            HttpRequestMetadata.Create(HttpContext),
            cancellationToken);
        ActiveRuleSetResponse response = ApiContractMapper.ToResponse(
            result.RuleSet);
        return result.WasCreated
            ? Created($"/api/rules/{response.Version}", response)
            : Ok(response);
    }

    /// <summary>
    /// Compares a stored candidate with the active version against bounded,
    /// non-personal representative parcels without changing live policy.
    /// </summary>
    [HttpPost("{version:int}/simulate")]
    [Authorize(Policy = AuthorizationPolicies.RuleAdministrator)]
    [EnableRateLimiting(ApiRateLimitPolicies.Query)]
    [ProducesResponseType<RuleSimulationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RuleSimulationResponse>> SimulateAsync(
        int version,
        [FromBody] SimulateRuleSetRequest request,
        CancellationToken cancellationToken)
    {
        RuleSimulationParcel[] samples = request.Samples
            .Select(
                sample => new RuleSimulationParcel(
                    sample.SampleId,
                    sample.WeightKilograms,
                    sample.DeclaredValueEuros,
                    sample.DestinationCountry))
            .ToArray();
        IReadOnlyList<RuleDecisionDifference> differences =
            await _rules.SimulateAsync(
                version,
                samples,
                HttpContext.TraceIdentifier,
                cancellationToken);
        RuleDecisionDifferenceResponse[] responseDifferences = differences
            .Select(
                difference => new RuleDecisionDifferenceResponse(
                    difference.SampleId,
                    difference.CurrentDepartment.ToString(),
                    difference.ProposedDepartment.ToString(),
                    difference.CurrentApprovalState.ToString(),
                    difference.ProposedApprovalState.ToString()))
            .ToArray();
        return Ok(
            new RuleSimulationResponse(
                version,
                samples.Length,
                responseDifferences.Length,
                responseDifferences));
    }

    /// <summary>
    /// Atomically activates a validated draft and retires the prior active
    /// version while preserving all historical decisions.
    /// </summary>
    [HttpPost("{version:int}/activate")]
    [Authorize(Policy = AuthorizationPolicies.RuleAdministrator)]
    [EnableRateLimiting(ApiRateLimitPolicies.Approval)]
    [ProducesResponseType<ActiveRuleSetResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ActiveRuleSetResponse>> ActivateAsync(
        int version,
        CancellationToken cancellationToken)
    {
        RuleSetActivationResult result = await _rules.ActivateAsync(
            version,
            HttpRequestMetadata.GetIdempotencyKey(HttpContext),
            HttpRequestMetadata.Create(HttpContext),
            cancellationToken);
        return Ok(ApiContractMapper.ToResponse(result.ActiveRuleSet));
    }

    /// <summary>
    /// Reactivates a retained valid historical version through the same atomic
    /// policy switch while recording a distinct rollback audit event.
    /// </summary>
    [HttpPost("{version:int}/rollback")]
    [Authorize(Policy = AuthorizationPolicies.RuleAdministrator)]
    [EnableRateLimiting(ApiRateLimitPolicies.Approval)]
    [ProducesResponseType<ActiveRuleSetResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ActiveRuleSetResponse>> RollbackAsync(
        int version,
        CancellationToken cancellationToken)
    {
        RuleSetActivationResult result = await _rules.RollbackAsync(
            version,
            HttpRequestMetadata.GetIdempotencyKey(HttpContext),
            HttpRequestMetadata.Create(HttpContext),
            cancellationToken);
        return Ok(ApiContractMapper.ToResponse(result.ActiveRuleSet));
    }

    /// <summary>
    /// Translates the public four-number editor into the complete typed rule
    /// definition with stable identifiers and continuous weight bands.
    /// </summary>
    private static RuleSetDefinition CreateDefinition(
        CreateRuleDraftRequest request)
    {
        return new RuleSetDefinition(
            request.Version,
            [
                new WeightBandDefinition(
                    DefaultRoutingRuleIds.MailWeight.Value,
                    100,
                    0m,
                    request.MailUpperKilograms,
                    RoutingDepartment.Mail),
                new WeightBandDefinition(
                    DefaultRoutingRuleIds.RegularWeight.Value,
                    200,
                    request.MailUpperKilograms,
                    request.RegularUpperKilograms,
                    RoutingDepartment.Regular),
                new WeightBandDefinition(
                    DefaultRoutingRuleIds.HeavyWeight.Value,
                    300,
                    request.RegularUpperKilograms,
                    UpperBoundInclusive: null,
                    RoutingDepartment.Heavy),
            ],
            new InsuranceRuleDefinition(
                DefaultRoutingRuleIds.InsuranceValue.Value,
                1_000,
                request.InsuranceThresholdEuros));
    }
}
