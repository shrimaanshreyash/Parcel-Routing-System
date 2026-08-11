using ParcelRoutingSystem.Domain.Routing;

namespace ParcelRoutingSystem.Application.Routing;

/// <summary>
/// Captures the immutable persistence representation of one explainable domain
/// decision without recipient names, addresses, or raw uploaded content.
/// </summary>
public sealed record RoutingDecisionRecord(
    Guid Id,
    string IdempotencyKey,
    string RequestFingerprint,
    decimal WeightKilograms,
    decimal DeclaredValueEuros,
    string DestinationCountry,
    RoutingDepartment IntendedDepartment,
    ApprovalState ApprovalState,
    int RuleSetVersion,
    IReadOnlyList<string> MatchedRuleIds,
    IReadOnlyList<string> Reasons,
    DateTimeOffset DecidedAtUtc,
    string CorrelationId,
    Guid? BatchId,
    Guid? BatchRowId);

/// <summary>
/// Reports whether an idempotent decision write created a new immutable record
/// or returned the outcome of an earlier request.
/// </summary>
public sealed record DecisionWriteResult(RoutingDecisionRecord Decision, bool WasCreated);

/// <summary>
/// Returns the routing decision and explicitly identifies idempotent replay for
/// observability and future transport responses.
/// </summary>
public sealed record RouteParcelResult(RoutingDecisionRecord Decision, bool WasReplay);
