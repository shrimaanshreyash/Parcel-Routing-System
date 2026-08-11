using ParcelRoutingSystem.Application.Batches;
using ParcelRoutingSystem.Domain.Routing;

namespace ParcelRoutingSystem.Application.Operations;

/// <summary>
/// Defines the supported server-owned history windows so clients cannot submit
/// arbitrary or unbounded date expressions.
/// </summary>
public enum OperationsTimeRange
{
    /// <summary>Returns only the newest bounded records.</summary>
    Recent = 1,

    /// <summary>Returns records created during the preceding twenty-four hours.</summary>
    Last24Hours = 2,

    /// <summary>Returns records created during the preceding seven days.</summary>
    Last7Days = 3,

    /// <summary>Returns records created during the preceding thirty days.</summary>
    Last30Days = 4,

    /// <summary>Returns records created during the preceding twelve months.</summary>
    Last12Months = 5,

    /// <summary>Returns all retained records through bounded pages.</summary>
    AllTime = 6,
}

/// <summary>
/// Defines the allow-listed decision-history filters that can be translated
/// into indexed, server-owned persistence queries.
/// </summary>
public enum RoutingDecisionFilter
{
    /// <summary>Returns decisions from every department and approval state.</summary>
    All = 1,

    /// <summary>Returns decisions whose intended department is Mail.</summary>
    Mail = 2,

    /// <summary>Returns decisions whose intended department is Regular.</summary>
    Regular = 3,

    /// <summary>Returns decisions whose intended department is Heavy.</summary>
    Heavy = 4,

    /// <summary>Returns high-value decisions still held for insurance approval.</summary>
    AwaitingApproval = 5,

    /// <summary>Returns decisions with append-only insurance approval evidence.</summary>
    Approved = 6,

    /// <summary>Returns decisions that never required insurance approval.</summary>
    ApprovalNotRequired = 7,
}

/// <summary>
/// Defines operator-facing activity categories so filtering happens before
/// paging rather than hiding records from one already-truncated browser page.
/// </summary>
public enum ActivityCategory
{
    /// <summary>Returns every retained operational event.</summary>
    All = 1,

    /// <summary>Returns XML batch creation and row-processing events.</summary>
    Imports = 2,

    /// <summary>Returns persisted parcel-routing decision events.</summary>
    Routing = 3,

    /// <summary>Returns append-only insurance approval events.</summary>
    Insurance = 4,

    /// <summary>Returns typed rule-set lifecycle events.</summary>
    Rules = 5,
}

/// <summary>
/// Selects one actionable import read model without conflating permanent row
/// issues with transient durable work.
/// </summary>
public enum ImportAttentionKind
{
    /// <summary>Returns rows that failed validation or permanent processing today.</summary>
    Issues = 1,

    /// <summary>Returns rows currently pending or held by a processing lease.</summary>
    Queue = 2,
}

/// <summary>
/// Carries one bounded server-side page and the metadata needed for stable
/// operator navigation without loading an unlimited history into the browser.
/// </summary>
public sealed record PagedResults<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalItems)
{
    /// <summary>
    /// Gets the number of available pages while representing an empty result as
    /// zero pages rather than a misleading page one.
    /// </summary>
    public int TotalPages =>
        TotalItems == 0
            ? 0
            : (int)Math.Ceiling((decimal)TotalItems / PageSize);
}

/// <summary>
/// Summarizes one immutable decision for operator lists without exposing
/// idempotency fingerprints or personal parcel data.
/// </summary>
public sealed record RoutingDecisionSummary(
    Guid Id,
    decimal WeightKilograms,
    decimal DeclaredValueEuros,
    string DestinationCountry,
    RoutingDepartment IntendedDepartment,
    ApprovalState ApprovalState,
    bool IsInsuranceApproved,
    int RuleSetVersion,
    IReadOnlyList<string> MatchedRuleIds,
    IReadOnlyList<string> Reasons,
    DateTimeOffset DecidedAtUtc,
    string CorrelationId,
    Guid? BatchId,
    Guid? BatchRowId);

/// <summary>
/// Describes append-only release evidence joined to a decision detail without
/// changing the original routing result.
/// </summary>
public sealed record InsuranceApprovalSummary(
    Guid Id,
    string ApprovedBy,
    DateTimeOffset ApprovedAtUtc,
    string CorrelationId);

/// <summary>
/// Provides a complete privacy-safe operator detail for one immutable decision.
/// </summary>
public sealed record RoutingDecisionDetails(
    RoutingDecisionSummary Decision,
    InsuranceApprovalSummary? Approval);

/// <summary>
/// Provides the small operational counters and recent decisions needed by the
/// non-technical overview without inventing dashboard metrics.
/// </summary>
public sealed record OperationsOverview(
    int TotalDecisions,
    int ProcessedToday,
    int AwaitingInsuranceApproval,
    int ImportIssues,
    int PendingBatchRows,
    OperationsTimeRange DecisionRange,
    RoutingDecisionFilter DecisionFilter,
    PagedResults<RoutingDecisionSummary> DecisionHistory);

/// <summary>
/// Describes one privacy-safe immutable audit event for support and operator
/// investigation.
/// </summary>
public sealed record ActivityRecord(
    Guid Id,
    string EventType,
    string SubjectType,
    string SubjectId,
    string ActorId,
    string CorrelationId,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyDictionary<string, string> Details,
    Guid? RelatedBatchId,
    Guid? RelatedDecisionId);

/// <summary>
/// Describes one privacy-safe import row that needs operator visibility,
/// retaining only durable identifiers, safe failure details, and queue state.
/// </summary>
public sealed record ImportAttentionItem(
    Guid RowId,
    Guid BatchId,
    int RowNumber,
    BatchRowStatus Status,
    string? ErrorCode,
    string? ErrorMessage,
    int AttemptCount,
    DateTimeOffset BatchCreatedAtUtc);

/// <summary>
/// Joins a durable batch snapshot to any immutable row decisions needed to
/// present clear per-row outcomes.
/// </summary>
public sealed record BatchDetails(
    BatchRecord Batch,
    IReadOnlyList<RoutingDecisionSummary> Decisions);

/// <summary>
/// Summarizes a durable batch for bounded import history without loading every
/// row until the operator opens it.
/// </summary>
public sealed record BatchSummary(
    Guid Id,
    string? FallbackDestinationCountry,
    BatchStatus Status,
    int TotalRows,
    int CompletedRows,
    int FailedRows,
    int AwaitingInsuranceApproval,
    DateTimeOffset CreatedAtUtc,
    string CreatedBy);
