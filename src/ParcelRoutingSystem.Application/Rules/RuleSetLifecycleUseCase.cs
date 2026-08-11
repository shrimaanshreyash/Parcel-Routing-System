using ParcelRoutingSystem.Application.Common;
using ParcelRoutingSystem.Domain.Parcels;
using ParcelRoutingSystem.Domain.Routing;

namespace ParcelRoutingSystem.Application.Rules;

/// <summary>
/// Coordinates safe draft creation, decision-diff simulation, activation, and
/// rollback while leaving semantic rule validation in the pure domain.
/// </summary>
public sealed class RuleSetLifecycleUseCase
{
    private const int MaximumVersionHistory = 50;
    private const int MaximumSimulationSamples = 100;
    private readonly IRuleSetRepository _repository;
    private readonly IApplicationClock _clock;
    private readonly IIdentifierGenerator _identifiers;

    /// <summary>
    /// Creates the rule lifecycle coordinator with application-owned ports.
    /// </summary>
    /// <param name="repository">The transactional immutable-rule repository.</param>
    /// <param name="clock">The server-owned UTC clock.</param>
    /// <param name="identifiers">The server-owned audit identifier generator.</param>
    public RuleSetLifecycleUseCase(
        IRuleSetRepository repository,
        IApplicationClock clock,
        IIdentifierGenerator identifiers)
    {
        _repository = repository;
        _clock = clock;
        _identifiers = identifiers;
    }

    /// <summary>
    /// Validates a proposed constrained definition with the pure domain and
    /// persists it as a non-active immutable draft.
    /// </summary>
    /// <param name="definition">The complete proposed rule-set definition.</param>
    /// <param name="idempotencyKey">The replay key for draft creation.</param>
    /// <param name="metadata">The actor and correlation metadata.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored draft and replay status.</returns>
    public Task<RuleSetWriteResult> CreateDraftAsync(
        RuleSetDefinition definition,
        string idempotencyKey,
        OperationMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(metadata);

        string safeKey = NormalizeIdempotencyKey(idempotencyKey);
        _ = definition.ToDomain();
        DateTimeOffset occurredAtUtc = _clock.UtcNow.ToUniversalTime();
        var draft = new StoredRuleSet(
            definition,
            RuleSetLifecycleStatus.Draft,
            occurredAtUtc,
            metadata.ActorId,
            ActivatedAtUtc: null);
        AuditEventRecord auditEvent = AuditEventRecord.Create(
            _identifiers.NewId(),
            "rule-set.draft-created",
            "rule-set",
            definition.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            metadata,
            safeKey,
            occurredAtUtc);

        return SaveDraftAndVerifyAsync(
            draft,
            auditEvent,
            safeKey,
            cancellationToken);
    }

    /// <summary>
    /// Compares a stored candidate with the active version against supplied
    /// privacy-safe parcel facts without mutating either rule set.
    /// </summary>
    /// <param name="candidateVersion">The stored candidate version.</param>
    /// <param name="samples">Representative non-personal parcel facts.</param>
    /// <param name="correlationId">The deterministic simulation trace identifier.</param>
    /// <param name="cancellationToken">Cancels repository reads.</param>
    /// <returns>One result per sample whose department or approval state changes.</returns>
    public async Task<IReadOnlyList<RuleDecisionDifference>> SimulateAsync(
        int candidateVersion,
        IReadOnlyList<RuleSimulationParcel> samples,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count is < 1 or > MaximumSimulationSamples)
        {
            throw new ApplicationOperationException(
                ApplicationErrorCodes.OperationMetadataInvalid,
                $"Simulation requires between 1 and {MaximumSimulationSamples} samples.");
        }

        StoredRuleSet active = await RequireActiveAsync(cancellationToken);
        StoredRuleSet candidate = await _repository.GetVersionAsync(
                candidateVersion,
                cancellationToken)
            ?? throw new ApplicationOperationException(
                ApplicationErrorCodes.RuleSetNotFound,
                $"Rule-set version {candidateVersion} does not exist.");
        RoutingRuleSet activeDomain = active.Definition.ToDomain();
        RoutingRuleSet candidateDomain = candidate.Definition.ToDomain();
        DateTimeOffset simulationTime = _clock.UtcNow.ToUniversalTime();
        string safeCorrelationId = ApplicationGuard.RequiredText(
            correlationId,
            100,
            ApplicationErrorCodes.OperationMetadataInvalid,
            "Correlation identifier");
        var differences = new List<RuleDecisionDifference>();

        foreach (RuleSimulationParcel sample in samples)
        {
            Parcel parcel = sample.ToDomain();
            RoutingDecisionContext context = RoutingDecisionContext.Create(
                simulationTime,
                $"{safeCorrelationId}:{sample.SampleId}");
            RoutingDecision current = activeDomain.Route(parcel, context);
            RoutingDecision proposed = candidateDomain.Route(parcel, context);

            if (current.IntendedDepartment != proposed.IntendedDepartment
                || current.ApprovalState != proposed.ApprovalState)
            {
                differences.Add(
                    new RuleDecisionDifference(
                        sample.SampleId,
                        current.IntendedDepartment,
                        proposed.IntendedDepartment,
                        current.ApprovalState,
                        proposed.ApprovalState));
            }
        }

        return differences.AsReadOnly();
    }

    /// <summary>
    /// Loads a bounded newest-first version history for monitoring and rollback
    /// selection without allowing an unbounded administrative query.
    /// </summary>
    public Task<IReadOnlyList<StoredRuleSet>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetRecentAsync(
            Math.Clamp(limit, 1, MaximumVersionHistory),
            cancellationToken);
    }

    /// <summary>
    /// Atomically makes a validated draft the active rule set and retires the
    /// previous version.
    /// </summary>
    /// <param name="version">The draft version to activate.</param>
    /// <param name="idempotencyKey">The activation replay key.</param>
    /// <param name="metadata">The actor and correlation metadata.</param>
    /// <param name="cancellationToken">Cancels the transaction.</param>
    /// <returns>The active version and whether state changed.</returns>
    public Task<RuleSetActivationResult> ActivateAsync(
        int version,
        string idempotencyKey,
        OperationMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        return ChangeActiveVersionAsync(
            version,
            idempotencyKey,
            metadata,
            "rule-set.activated",
            cancellationToken);
    }

    /// <summary>
    /// Reactivates a retained historical version through the same atomic
    /// activation path and records the operation explicitly as a rollback.
    /// </summary>
    /// <param name="version">The historical valid version to reactivate.</param>
    /// <param name="idempotencyKey">The rollback replay key.</param>
    /// <param name="metadata">The actor and correlation metadata.</param>
    /// <param name="cancellationToken">Cancels the transaction.</param>
    /// <returns>The restored active version and whether state changed.</returns>
    public Task<RuleSetActivationResult> RollbackAsync(
        int version,
        string idempotencyKey,
        OperationMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        return ChangeActiveVersionAsync(
            version,
            idempotencyKey,
            metadata,
            "rule-set.rolled-back",
            cancellationToken);
    }

    /// <summary>
    /// Loads the active version or fails closed so no use case can silently
    /// choose a fallback policy.
    /// </summary>
    /// <param name="cancellationToken">Cancels the repository read.</param>
    /// <returns>The single active immutable rule set.</returns>
    public async Task<StoredRuleSet> RequireActiveAsync(
        CancellationToken cancellationToken = default)
    {
        return await _repository.GetActiveAsync(cancellationToken)
            ?? throw new ApplicationOperationException(
                ApplicationErrorCodes.ActiveRuleSetUnavailable,
                "No active routing rule set is available.");
    }

    /// <summary>
    /// Performs activation and rollback through one audited transactional path
    /// while preserving the distinct audit event name.
    /// </summary>
    /// <param name="version">The version that should become active.</param>
    /// <param name="idempotencyKey">The operation replay key.</param>
    /// <param name="metadata">The actor and correlation metadata.</param>
    /// <param name="eventType">The allow-listed activation event name.</param>
    /// <param name="cancellationToken">Cancels the transaction.</param>
    /// <returns>The resulting active version.</returns>
    private async Task<RuleSetActivationResult> ChangeActiveVersionAsync(
        int version,
        string idempotencyKey,
        OperationMetadata metadata,
        string eventType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        string safeKey = NormalizeIdempotencyKey(idempotencyKey);
        DateTimeOffset occurredAtUtc = _clock.UtcNow.ToUniversalTime();
        AuditEventRecord auditEvent = AuditEventRecord.Create(
            _identifiers.NewId(),
            eventType,
            "rule-set",
            version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            metadata,
            safeKey,
            occurredAtUtc);

        RuleSetActivationResult result = await _repository.ActivateAsync(
            version,
            auditEvent,
            safeKey,
            cancellationToken);
        if (result.ActiveRuleSet.Definition.Version != version)
        {
            throw new ApplicationOperationException(
                ApplicationErrorCodes.IdempotencyConflict,
                "The idempotency key was already used for another rule version.");
        }

        return result;
    }

    /// <summary>
    /// Applies the common bounded idempotency-key rule to every lifecycle write.
    /// </summary>
    /// <param name="idempotencyKey">The untrusted operation replay key.</param>
    /// <returns>The trimmed bounded key.</returns>
    private static string NormalizeIdempotencyKey(string idempotencyKey)
    {
        return ApplicationGuard.RequiredText(
            idempotencyKey,
            100,
            ApplicationErrorCodes.IdempotencyKeyInvalid,
            "Idempotency key");
    }

    /// <summary>
    /// Stores or replays a draft and rejects key reuse when the complete
    /// constrained definition differs.
    /// </summary>
    /// <param name="draft">The proposed validated draft.</param>
    /// <param name="auditEvent">The draft creation audit event.</param>
    /// <param name="idempotencyKey">The operation replay key.</param>
    /// <param name="cancellationToken">Cancels the transaction.</param>
    /// <returns>The new or matching replay result.</returns>
    private async Task<RuleSetWriteResult> SaveDraftAndVerifyAsync(
        StoredRuleSet draft,
        AuditEventRecord auditEvent,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        RuleSetWriteResult result = await _repository.SaveDraftAsync(
            draft,
            auditEvent,
            idempotencyKey,
            cancellationToken);
        string requested = ApplicationRequestFingerprint.ForRuleSet(draft.Definition);
        string stored = ApplicationRequestFingerprint.ForRuleSet(
            result.RuleSet.Definition);
        if (!string.Equals(requested, stored, StringComparison.Ordinal))
        {
            throw new ApplicationOperationException(
                ApplicationErrorCodes.IdempotencyConflict,
                "The idempotency key was already used for a different rule draft.");
        }

        return result;
    }
}

/// <summary>
/// Holds one privacy-safe parcel sample used to compare immutable rule versions.
/// </summary>
public sealed record RuleSimulationParcel(
    string SampleId,
    decimal WeightKilograms,
    decimal DeclaredValueEuros,
    string DestinationCountry)
{
    /// <summary>
    /// Converts the simulation facts into the same validated parcel model used
    /// by live routing.
    /// </summary>
    /// <returns>A validated pure-domain parcel.</returns>
    internal Parcel ToDomain()
    {
        string sampleId = ApplicationGuard.RequiredText(
            SampleId,
            100,
            ApplicationErrorCodes.OperationMetadataInvalid,
            "Simulation sample identifier");
        _ = sampleId;

        return Parcel.Create(
            Weight.FromKilograms(WeightKilograms),
            DeclaredValue.FromEuros(DeclaredValueEuros),
            CountryCode.FromAlpha2(DestinationCountry));
    }
}

/// <summary>
/// Describes the business effects that change for one simulation sample.
/// </summary>
public sealed record RuleDecisionDifference(
    string SampleId,
    RoutingDepartment CurrentDepartment,
    RoutingDepartment ProposedDepartment,
    ApprovalState CurrentApprovalState,
    ApprovalState ProposedApprovalState);
