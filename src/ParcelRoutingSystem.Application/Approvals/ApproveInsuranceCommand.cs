using ParcelRoutingSystem.Application.Common;

namespace ParcelRoutingSystem.Application.Approvals;

/// <summary>
/// Carries one insurance approval request with server-boundary identity,
/// correlation, and idempotency metadata.
/// </summary>
public sealed record ApproveInsuranceCommand(
    Guid DecisionId,
    string IdempotencyKey,
    OperationMetadata Metadata);
