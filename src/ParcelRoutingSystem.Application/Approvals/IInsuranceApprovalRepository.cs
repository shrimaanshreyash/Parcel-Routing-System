using ParcelRoutingSystem.Application.Common;

namespace ParcelRoutingSystem.Application.Approvals;

/// <summary>
/// Defines the atomic approval boundary. Implementations must verify the
/// immutable decision state and write the approval and audit event together.
/// </summary>
public interface IInsuranceApprovalRepository
{
    /// <summary>
    /// Applies one idempotent approval transaction without mutating the original
    /// routing decision.
    /// </summary>
    /// <param name="approval">The proposed append-only approval.</param>
    /// <param name="auditEvent">The corresponding privacy-safe audit event.</param>
    /// <param name="cancellationToken">Cancels the transaction.</param>
    /// <returns>The explicit approval persistence outcome.</returns>
    Task<InsuranceApprovalWriteResult> ApproveAsync(
        InsuranceApprovalRecord approval,
        AuditEventRecord auditEvent,
        CancellationToken cancellationToken);
}
