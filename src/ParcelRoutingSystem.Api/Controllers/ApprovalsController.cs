using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ParcelRoutingSystem.Api.Contracts;
using ParcelRoutingSystem.Api.Http;
using ParcelRoutingSystem.Api.Security;
using ParcelRoutingSystem.Application.Approvals;

namespace ParcelRoutingSystem.Api.Controllers;

/// <summary>
/// Exposes append-only insurance approval to the dedicated server-enforced
/// approver capability.
/// </summary>
[ApiController]
[Route("api/approvals")]
[Authorize(Policy = AuthorizationPolicies.InsuranceApprover)]
public sealed class ApprovalsController : ControllerBase
{
    private readonly ApproveInsuranceUseCase _approveInsurance;

    /// <summary>
    /// Creates the thin approval controller around the application workflow.
    /// </summary>
    /// <param name="approveInsurance">The append-only approval use case.</param>
    public ApprovalsController(ApproveInsuranceUseCase approveInsurance)
    {
        _approveInsurance = approveInsurance;
    }

    /// <summary>
    /// Approves one pending high-value decision or returns its original approval
    /// when an identical operation is retried.
    /// </summary>
    /// <param name="decisionId">The immutable decision requiring approval.</param>
    /// <param name="cancellationToken">Cancels request and database work.</param>
    /// <returns>The durable append-only approval.</returns>
    [HttpPost("{decisionId:guid}/approve")]
    [EnableRateLimiting(ApiRateLimitPolicies.Approval)]
    [ProducesResponseType<InsuranceApprovalResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<InsuranceApprovalResponse>> ApproveAsync(
        Guid decisionId,
        CancellationToken cancellationToken)
    {
        var command = new ApproveInsuranceCommand(
            decisionId,
            HttpRequestMetadata.GetIdempotencyKey(HttpContext),
            HttpRequestMetadata.Create(HttpContext));
        InsuranceApprovalRecord approval = await _approveInsurance.ExecuteAsync(
            command,
            cancellationToken);
        var response = new InsuranceApprovalResponse(
            approval.Id,
            approval.DecisionId,
            approval.ApprovedBy,
            approval.ApprovedAtUtc,
            approval.CorrelationId);

        return Ok(response);
    }
}
