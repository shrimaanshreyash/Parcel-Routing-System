using ParcelRoutingSystem.Application.Common;

namespace ParcelRoutingSystem.Application.Approvals;

/// <summary>
/// Coordinates an idempotent insurance approval while preserving the original
/// routing decision as immutable historical evidence.
/// </summary>
public sealed class ApproveInsuranceUseCase
{
    private readonly IInsuranceApprovalRepository _repository;
    private readonly IApplicationClock _clock;
    private readonly IIdentifierGenerator _identifiers;

    /// <summary>
    /// Creates the approval coordinator with transactional persistence and
    /// server-owned time and identifier ports.
    /// </summary>
    /// <param name="repository">The atomic approval repository.</param>
    /// <param name="clock">The server-owned UTC clock.</param>
    /// <param name="identifiers">The server-owned identifier generator.</param>
    public ApproveInsuranceUseCase(
        IInsuranceApprovalRepository repository,
        IApplicationClock clock,
        IIdentifierGenerator identifiers)
    {
        _repository = repository;
        _clock = clock;
        _identifiers = identifiers;
    }

    /// <summary>
    /// Persists or replays one insurance approval and translates expected
    /// decision-state conflicts into stable application errors.
    /// </summary>
    /// <param name="command">The decision, replay key, actor, and correlation facts.</param>
    /// <param name="cancellationToken">Cancels the transaction.</param>
    /// <returns>The newly stored or previously stored approval.</returns>
    public async Task<InsuranceApprovalRecord> ExecuteAsync(
        ApproveInsuranceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Metadata);

        if (command.DecisionId == Guid.Empty)
        {
            throw new ApplicationOperationException(
                ApplicationErrorCodes.DecisionNotFound,
                "A routing decision identifier is required.");
        }

        string idempotencyKey = ApplicationGuard.RequiredText(
            command.IdempotencyKey,
            100,
            ApplicationErrorCodes.IdempotencyKeyInvalid,
            "Idempotency key");
        DateTimeOffset approvedAtUtc = _clock.UtcNow.ToUniversalTime();
        var approval = new InsuranceApprovalRecord(
            _identifiers.NewId(),
            command.DecisionId,
            idempotencyKey,
            command.Metadata.ActorId,
            approvedAtUtc,
            command.Metadata.CorrelationId);
        AuditEventRecord auditEvent = AuditEventRecord.Create(
            _identifiers.NewId(),
            "insurance.approved",
            "routing-decision",
            command.DecisionId.ToString("D"),
            command.Metadata,
            idempotencyKey,
            approvedAtUtc);
        InsuranceApprovalWriteResult result = await _repository.ApproveAsync(
            approval,
            auditEvent,
            cancellationToken);

        InsuranceApprovalRecord stored = result.Status switch
        {
            InsuranceApprovalWriteStatus.Created
                or InsuranceApprovalWriteStatus.Replayed => result.Approval!,
            InsuranceApprovalWriteStatus.DecisionNotFound =>
                throw new ApplicationOperationException(
                    ApplicationErrorCodes.DecisionNotFound,
                    "The routing decision does not exist."),
            InsuranceApprovalWriteStatus.ApprovalNotRequired =>
                throw new ApplicationOperationException(
                    ApplicationErrorCodes.InsuranceApprovalNotRequired,
                    "The routing decision does not require insurance approval."),
            _ => throw new InvalidOperationException(
                "The approval repository returned an unsupported outcome."),
        };

        if (stored.DecisionId != command.DecisionId)
        {
            throw new ApplicationOperationException(
                ApplicationErrorCodes.IdempotencyConflict,
                "The idempotency key was already used for another decision.");
        }

        return stored;
    }
}
