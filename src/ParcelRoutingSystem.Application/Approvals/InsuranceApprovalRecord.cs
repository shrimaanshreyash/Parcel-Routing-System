namespace ParcelRoutingSystem.Application.Approvals;

/// <summary>
/// Captures an append-only approval that releases the insurance workflow hold
/// without modifying the original routing decision.
/// </summary>
public sealed record InsuranceApprovalRecord(
    Guid Id,
    Guid DecisionId,
    string IdempotencyKey,
    string ApprovedBy,
    DateTimeOffset ApprovedAtUtc,
    string CorrelationId);

/// <summary>
/// Identifies the transactional result of an approval persistence request.
/// </summary>
public enum InsuranceApprovalWriteStatus
{
    /// <summary>A new approval and audit event were persisted.</summary>
    Created = 1,

    /// <summary>The original result was returned for an idempotent replay.</summary>
    Replayed = 2,

    /// <summary>The requested routing decision does not exist.</summary>
    DecisionNotFound = 3,

    /// <summary>The requested decision did not require insurance approval.</summary>
    ApprovalNotRequired = 4,
}

/// <summary>
/// Returns the stored approval when successful and the explicit persistence
/// outcome in every case.
/// </summary>
public sealed record InsuranceApprovalWriteResult(
    InsuranceApprovalWriteStatus Status,
    InsuranceApprovalRecord? Approval);
