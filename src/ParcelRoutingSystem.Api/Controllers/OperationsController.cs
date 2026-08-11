using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ParcelRoutingSystem.Api.Contracts;
using ParcelRoutingSystem.Api.Security;
using ParcelRoutingSystem.Application.Operations;

namespace ParcelRoutingSystem.Api.Controllers;

/// <summary>
/// Exposes bounded privacy-safe overview and audit reads to authenticated
/// operators and support reviewers.
/// </summary>
[ApiController]
[Route("api/operations")]
[Authorize(Policy = AuthorizationPolicies.Authenticated)]
public sealed class OperationsController : ControllerBase
{
    private readonly OperationsQueryUseCase _operations;

    /// <summary>
    /// Creates the operations controller around the bounded application query
    /// coordinator.
    /// </summary>
    /// <param name="operations">The privacy-safe read use case.</param>
    public OperationsController(OperationsQueryUseCase operations)
    {
        _operations = operations;
    }

    /// <summary>
    /// Returns current counters and one bounded server-filtered decision page.
    /// </summary>
    /// <param name="range">The allow-listed relative history window.</param>
    /// <param name="page">The requested one-based page.</param>
    /// <param name="filter">The allow-listed department or approval filter.</param>
    /// <param name="cancellationToken">Cancels the read queries.</param>
    /// <returns>The current operational overview.</returns>
    [HttpGet("overview")]
    [EnableRateLimiting(ApiRateLimitPolicies.Query)]
    [ProducesResponseType<OperationsOverviewResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OperationsOverviewResponse>> GetOverviewAsync(
        [FromQuery] OperationsTimeRange range = OperationsTimeRange.Recent,
        [FromQuery] int page = 1,
        [FromQuery] RoutingDecisionFilter filter = RoutingDecisionFilter.All,
        CancellationToken cancellationToken = default)
    {
        OperationsOverview overview = await _operations.GetOverviewAsync(
            range,
            page,
            filter,
            cancellationToken);
        return Ok(ApiContractMapper.ToResponse(overview));
    }

    /// <summary>
    /// Returns one newest-first privacy-safe audit page for a constrained range.
    /// </summary>
    /// <param name="range">The allow-listed relative history window.</param>
    /// <param name="page">The requested one-based page.</param>
    /// <param name="category">The allow-listed operational event category.</param>
    /// <param name="cancellationToken">Cancels the read query.</param>
    /// <returns>Newest audit events first.</returns>
    [HttpGet("activity")]
    [EnableRateLimiting(ApiRateLimitPolicies.Query)]
    [ProducesResponseType<PagedResponse<ActivityResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<ActivityResponse>>> GetActivityAsync(
        [FromQuery] OperationsTimeRange range = OperationsTimeRange.Recent,
        [FromQuery] int page = 1,
        [FromQuery] ActivityCategory category = ActivityCategory.All,
        CancellationToken cancellationToken = default)
    {
        PagedResults<ActivityRecord> activity =
            await _operations.GetActivityAsync(
                range,
                page,
                category,
                cancellationToken);
        return Ok(
            ApiContractMapper.ToResponse(
                activity,
                ApiContractMapper.ToResponse));
    }

    /// <summary>
    /// Returns the concrete durable rows represented by the Overview import
    /// issue or batch queue KPI.
    /// </summary>
    /// <param name="kind">Whether to inspect today's issues or current queue.</param>
    /// <param name="page">The requested one-based page.</param>
    /// <param name="cancellationToken">Cancels the bounded read.</param>
    /// <returns>One page of privacy-safe actionable import rows.</returns>
    [HttpGet("import-attention")]
    [EnableRateLimiting(ApiRateLimitPolicies.Query)]
    [ProducesResponseType<PagedResponse<ImportAttentionResponse>>(
        StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<ImportAttentionResponse>>>
        GetImportAttentionAsync(
            [FromQuery] ImportAttentionKind kind = ImportAttentionKind.Issues,
            [FromQuery] int page = 1,
            CancellationToken cancellationToken = default)
    {
        PagedResults<ImportAttentionItem> items =
            await _operations.GetImportAttentionAsync(
                kind,
                page,
                cancellationToken);
        return Ok(
            ApiContractMapper.ToResponse(
                items,
                ApiContractMapper.ToResponse));
    }

    /// <summary>
    /// Returns one explainable immutable decision with separate approval
    /// evidence and optional related batch identity.
    /// </summary>
    [HttpGet("decisions/{decisionId:guid}")]
    [EnableRateLimiting(ApiRateLimitPolicies.Query)]
    [ProducesResponseType<RoutingDecisionDetailsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RoutingDecisionDetailsResponse>>
        GetDecisionAsync(
            Guid decisionId,
            CancellationToken cancellationToken)
    {
        RoutingDecisionDetails details = await _operations.GetDecisionAsync(
            decisionId,
            cancellationToken);
        return Ok(ApiContractMapper.ToResponse(details));
    }

    /// <summary>
    /// Returns the bounded oldest-first unresolved insurance work queue.
    /// Server authorization on the approval write remains the final authority.
    /// </summary>
    [HttpGet("insurance/awaiting")]
    [EnableRateLimiting(ApiRateLimitPolicies.Query)]
    [ProducesResponseType<PagedResponse<RoutingDecisionResponse>>(
        StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<RoutingDecisionResponse>>>
        GetAwaitingInsuranceAsync(
            [FromQuery] int page = 1,
            CancellationToken cancellationToken = default)
    {
        PagedResults<RoutingDecisionSummary> decisions =
            await _operations.GetAwaitingInsuranceAsync(
                page,
                cancellationToken);
        return Ok(
            ApiContractMapper.ToResponse(
                decisions,
                ApiContractMapper.ToDecision));
    }
}
