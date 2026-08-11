using ParcelRoutingSystem.Application.Batches;
using ParcelRoutingSystem.Application.Rules;
using ParcelRoutingSystem.Domain.Routing;

namespace ParcelRoutingSystem.Infrastructure.Persistence;

/// <summary>
/// Stores one immutable rule-set version and its lifecycle metadata.
/// </summary>
internal sealed class RuleSetEntity
{
    internal int Version { get; set; }

    internal RuleSetLifecycleStatus Status { get; set; }

    internal DateTimeOffset CreatedAtUtc { get; set; }

    internal string CreatedBy { get; set; } = string.Empty;

    internal DateTimeOffset? ActivatedAtUtc { get; set; }

    internal List<WeightBandRuleEntity> WeightBands { get; set; } = [];

    internal InsuranceRuleEntity? InsuranceRule { get; set; }
}

/// <summary>
/// Stores one constrained weight band belonging to an immutable rule version.
/// </summary>
internal sealed class WeightBandRuleEntity
{
    internal long Id { get; set; }

    internal int RuleSetVersion { get; set; }

    internal string RuleId { get; set; } = string.Empty;

    internal int Priority { get; set; }

    internal decimal LowerBoundExclusive { get; set; }

    internal decimal? UpperBoundInclusive { get; set; }

    internal RoutingDepartment Department { get; set; }

    internal RuleSetEntity RuleSet { get; set; } = null!;
}

/// <summary>
/// Stores the independent insurance threshold for one immutable rule version.
/// </summary>
internal sealed class InsuranceRuleEntity
{
    internal int RuleSetVersion { get; set; }

    internal string RuleId { get; set; } = string.Empty;

    internal int Priority { get; set; }

    internal decimal ThresholdExclusiveEuros { get; set; }

    internal RuleSetEntity RuleSet { get; set; } = null!;
}

/// <summary>
/// Stores one immutable, privacy-minimized routing decision.
/// </summary>
internal sealed class RoutingDecisionEntity
{
    internal Guid Id { get; set; }

    internal string IdempotencyKey { get; set; } = string.Empty;

    internal string RequestFingerprint { get; set; } = string.Empty;

    internal decimal WeightKilograms { get; set; }

    internal decimal DeclaredValueEuros { get; set; }

    internal string DestinationCountry { get; set; } = string.Empty;

    internal RoutingDepartment IntendedDepartment { get; set; }

    internal ApprovalState ApprovalState { get; set; }

    internal int RuleSetVersion { get; set; }

    internal string[] MatchedRuleIds { get; set; } = [];

    internal string[] Reasons { get; set; } = [];

    internal DateTimeOffset DecidedAtUtc { get; set; }

    internal string CorrelationId { get; set; } = string.Empty;

    internal Guid? BatchId { get; set; }

    internal Guid? BatchRowId { get; set; }
}

/// <summary>
/// Stores one append-only insurance approval linked to an immutable decision.
/// </summary>
internal sealed class InsuranceApprovalEntity
{
    internal Guid Id { get; set; }

    internal Guid DecisionId { get; set; }

    internal string IdempotencyKey { get; set; } = string.Empty;

    internal string ApprovedBy { get; set; } = string.Empty;

    internal DateTimeOffset ApprovedAtUtc { get; set; }

    internal string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Stores one append-only, privacy-safe audit event.
/// </summary>
internal sealed class AuditEventEntity
{
    internal Guid Id { get; set; }

    internal string EventType { get; set; } = string.Empty;

    internal string SubjectType { get; set; } = string.Empty;

    internal string SubjectId { get; set; } = string.Empty;

    internal string ActorId { get; set; } = string.Empty;

    internal string CorrelationId { get; set; } = string.Empty;

    internal string IdempotencyKey { get; set; } = string.Empty;

    internal DateTimeOffset OccurredAtUtc { get; set; }

    internal string DetailsJson { get; set; } = "{}";
}

/// <summary>
/// Stores one durable batch and its aggregate progress counters.
/// </summary>
internal sealed class BatchEntity
{
    internal Guid Id { get; set; }

    internal string IdempotencyKey { get; set; } = string.Empty;

    internal string RequestFingerprint { get; set; } = string.Empty;

    internal string? FallbackDestinationCountry { get; set; }

    internal BatchStatus Status { get; set; }

    internal int TotalRows { get; set; }

    internal int CompletedRows { get; set; }

    internal int FailedRows { get; set; }

    internal DateTimeOffset CreatedAtUtc { get; set; }

    internal string CreatedBy { get; set; } = string.Empty;

    internal List<BatchRowEntity> Rows { get; set; } = [];
}

/// <summary>
/// Stores one independently claimable and recoverable batch row without
/// recipient personal data.
/// </summary>
internal sealed class BatchRowEntity
{
    internal Guid Id { get; set; }

    internal Guid BatchId { get; set; }

    internal int RowNumber { get; set; }

    internal decimal WeightKilograms { get; set; }

    internal decimal DeclaredValueEuros { get; set; }

    internal string DestinationCountry { get; set; } = string.Empty;

    internal BatchCountrySource CountrySource { get; set; }

    internal BatchRowStatus Status { get; set; }

    internal string? ErrorCode { get; set; }

    internal string? ErrorMessage { get; set; }

    internal int AttemptCount { get; set; }

    internal Guid? DecisionId { get; set; }

    internal Guid? ClaimToken { get; set; }

    internal DateTimeOffset? LeaseExpiresAtUtc { get; set; }

    internal BatchEntity Batch { get; set; } = null!;
}
