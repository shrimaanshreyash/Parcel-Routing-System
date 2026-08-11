using ParcelRoutingSystem.Application.Common;
using ParcelRoutingSystem.Application.Rules;
using ParcelRoutingSystem.Domain.Parcels;
using ParcelRoutingSystem.Domain.Routing;

namespace ParcelRoutingSystem.Application.Routing;

/// <summary>
/// Coordinates one idempotent parcel-routing operation around the pure domain,
/// active rule repository, immutable decision store, and audit boundary.
/// </summary>
public sealed class RouteParcelUseCase
{
    private readonly IRoutingDecisionRepository _decisions;
    private readonly IRuleSetRepository _ruleSets;
    private readonly IApplicationClock _clock;
    private readonly IIdentifierGenerator _identifiers;

    /// <summary>
    /// Creates the route-one-parcel coordinator with no dependency on HTTP,
    /// Entity Framework Core, or a concrete database.
    /// </summary>
    /// <param name="decisions">The immutable transactional decision repository.</param>
    /// <param name="ruleSets">The active version source.</param>
    /// <param name="clock">The server-owned UTC clock.</param>
    /// <param name="identifiers">The server-owned record identifier generator.</param>
    public RouteParcelUseCase(
        IRoutingDecisionRepository decisions,
        IRuleSetRepository ruleSets,
        IApplicationClock clock,
        IIdentifierGenerator identifiers)
    {
        _decisions = decisions;
        _ruleSets = ruleSets;
        _clock = clock;
        _identifiers = identifiers;
    }

    /// <summary>
    /// Returns the original result for an idempotent replay or evaluates and
    /// transactionally stores a new explainable decision.
    /// </summary>
    /// <param name="command">Validated-boundary parcel facts and operation metadata.</param>
    /// <param name="cancellationToken">Cancels repository operations.</param>
    /// <returns>The durable decision and replay status.</returns>
    public async Task<RouteParcelResult> ExecuteAsync(
        RouteParcelCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Metadata);

        string idempotencyKey = ApplicationGuard.RequiredText(
            command.IdempotencyKey,
            100,
            ApplicationErrorCodes.IdempotencyKeyInvalid,
            "Idempotency key");
        Parcel parcel = Parcel.Create(
            Weight.FromKilograms(command.WeightKilograms),
            DeclaredValue.FromEuros(command.DeclaredValueEuros),
            CountryCode.FromAlpha2(command.DestinationCountry),
            command.AdditionalAttributes);
        string requestFingerprint = ApplicationRequestFingerprint.ForParcel(parcel);
        RoutingDecisionRecord? existing = await _decisions.FindByIdempotencyKeyAsync(
            idempotencyKey,
            cancellationToken);

        if (existing is not null)
        {
            EnsureMatchingFingerprint(existing.RequestFingerprint, requestFingerprint);
            return new RouteParcelResult(existing, WasReplay: true);
        }

        StoredRuleSet active = await _ruleSets.GetActiveAsync(cancellationToken)
            ?? throw new ApplicationOperationException(
                ApplicationErrorCodes.ActiveRuleSetUnavailable,
                "No active routing rule set is available.");
        DateTimeOffset decidedAtUtc = _clock.UtcNow.ToUniversalTime();
        RoutingDecision decision = active.Definition.ToDomain().Route(
            parcel,
            RoutingDecisionContext.Create(
                decidedAtUtc,
                command.Metadata.CorrelationId));
        var record = new RoutingDecisionRecord(
            _identifiers.NewId(),
            idempotencyKey,
            requestFingerprint,
            parcel.Weight.Kilograms,
            parcel.DeclaredValue.Euros,
            parcel.DestinationCountry.Value,
            decision.IntendedDepartment,
            decision.ApprovalState,
            decision.RuleSetVersion.Value,
            decision.MatchedRuleIds.Select(id => id.Value).ToArray(),
            decision.Reasons.ToArray(),
            decision.DecidedAtUtc,
            decision.CorrelationId,
            command.BatchId,
            command.BatchRowId);
        AuditEventRecord auditEvent = AuditEventRecord.Create(
            _identifiers.NewId(),
            "routing.decision-created",
            "routing-decision",
            record.Id.ToString("D"),
            command.Metadata,
            idempotencyKey,
            decidedAtUtc,
            new Dictionary<string, string>
            {
                ["department"] = record.IntendedDepartment.ToString(),
                ["approvalState"] = record.ApprovalState.ToString(),
                ["ruleSetVersion"] = record.RuleSetVersion.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            });
        DecisionWriteResult writeResult = await _decisions.SaveAsync(
            record,
            auditEvent,
            cancellationToken);
        EnsureMatchingFingerprint(
            writeResult.Decision.RequestFingerprint,
            requestFingerprint);

        return new RouteParcelResult(
            writeResult.Decision,
            WasReplay: !writeResult.WasCreated);
    }

    /// <summary>
    /// Rejects key reuse when the durable winner represents different normalized
    /// parcel facts.
    /// </summary>
    /// <param name="stored">The fingerprint already bound to the key.</param>
    /// <param name="requested">The fingerprint of the current normalized request.</param>
    private static void EnsureMatchingFingerprint(string stored, string requested)
    {
        if (!string.Equals(stored, requested, StringComparison.Ordinal))
        {
            throw new ApplicationOperationException(
                ApplicationErrorCodes.IdempotencyConflict,
                "The idempotency key was already used for different parcel input.");
        }
    }
}
