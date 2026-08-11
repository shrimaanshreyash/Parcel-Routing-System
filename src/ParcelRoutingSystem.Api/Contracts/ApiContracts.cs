using System.ComponentModel.DataAnnotations;

namespace ParcelRoutingSystem.Api.Contracts;

/// <summary>
/// Defines the public single-parcel facts accepted by the routing endpoint.
/// Browser-only references become constrained optional domain attributes.
/// </summary>
public sealed record RouteParcelRequest(
    [param: Range(typeof(decimal), "0.000000000001", "79228162514264337593543950335")]
    decimal WeightKilograms,
    [param: Range(typeof(decimal), "0", "79228162514264337593543950335")]
    decimal DeclaredValueEuros,
    [param: Required, StringLength(2, MinimumLength = 2)]
    string DestinationCountry,
    [param: StringLength(60)]
    string? OperatorReference);

/// <summary>
/// Returns one explainable immutable decision without exposing persistence or
/// fingerprint internals.
/// </summary>
public sealed record RoutingDecisionResponse(
    Guid Id,
    decimal WeightKilograms,
    decimal DeclaredValueEuros,
    string DestinationCountry,
    string IntendedDepartment,
    string ApprovalState,
    bool IsInsuranceApproved,
    int RuleSetVersion,
    IReadOnlyList<string> MatchedRuleIds,
    IReadOnlyList<string> Reasons,
    DateTimeOffset DecidedAtUtc,
    string CorrelationId,
    Guid? BatchId);

/// <summary>
/// Returns the append-only evidence that released one insurance hold.
/// </summary>
public sealed record InsuranceApprovalEvidenceResponse(
    Guid Id,
    string ApprovedBy,
    DateTimeOffset ApprovedAtUtc,
    string CorrelationId);

/// <summary>
/// Returns one immutable decision together with its separate approval evidence.
/// </summary>
public sealed record RoutingDecisionDetailsResponse(
    RoutingDecisionResponse Decision,
    InsuranceApprovalEvidenceResponse? Approval);

/// <summary>
/// Reports one newly created or idempotently replayed routing result.
/// </summary>
public sealed record RouteParcelResponse(
    RoutingDecisionResponse Decision,
    bool WasReplay);

/// <summary>
/// Returns the append-only approval created or replayed for a high-value
/// decision.
/// </summary>
public sealed record InsuranceApprovalResponse(
    Guid Id,
    Guid DecisionId,
    string ApprovedBy,
    DateTimeOffset ApprovedAtUtc,
    string CorrelationId);

/// <summary>
/// Returns one constrained active rule row in display-ready form.
/// </summary>
public sealed record ActiveRuleResponse(
    string RuleId,
    string Input,
    string Condition,
    string Outcome,
    int Priority);

/// <summary>
/// Returns the single active immutable rule-set version and its controlled
/// display rows.
/// </summary>
public sealed record ActiveRuleSetResponse(
    int Version,
    string Status,
    DateTimeOffset CreatedAtUtc,
    string CreatedBy,
    DateTimeOffset? ActivatedAtUtc,
    decimal MailUpperKilograms,
    decimal RegularUpperKilograms,
    decimal InsuranceThresholdEuros,
    IReadOnlyList<ActiveRuleResponse> Rules);

/// <summary>
/// Accepts only the three continuous weight-band boundaries and the independent
/// insurance threshold; arbitrary expressions and scripts are impossible.
/// </summary>
public sealed record CreateRuleDraftRequest(
    [param: Range(2, int.MaxValue)] int Version,
    [param: Range(typeof(decimal), "0.000000000001", "79228162514264337593543950335")]
    decimal MailUpperKilograms,
    [param: Range(typeof(decimal), "0.000000000001", "79228162514264337593543950335")]
    decimal RegularUpperKilograms,
    [param: Range(typeof(decimal), "0", "79228162514264337593543950335")]
    decimal InsuranceThresholdEuros);

/// <summary>
/// Provides one privacy-safe representative parcel for rule simulation.
/// </summary>
public sealed record RuleSimulationSampleRequest(
    [param: Required, StringLength(100)] string SampleId,
    [param: Range(typeof(decimal), "0.000000000001", "79228162514264337593543950335")]
    decimal WeightKilograms,
    [param: Range(typeof(decimal), "0", "79228162514264337593543950335")]
    decimal DeclaredValueEuros,
    [param: Required, StringLength(2, MinimumLength = 2)]
    string DestinationCountry);

/// <summary>
/// Carries a bounded typed simulation set for one stored candidate version.
/// </summary>
public sealed record SimulateRuleSetRequest(
    [param: Required, MinLength(1), MaxLength(100)]
    IReadOnlyList<RuleSimulationSampleRequest> Samples);

/// <summary>
/// Describes one sample whose department or approval outcome would change.
/// </summary>
public sealed record RuleDecisionDifferenceResponse(
    string SampleId,
    string CurrentDepartment,
    string ProposedDepartment,
    string CurrentApprovalState,
    string ProposedApprovalState);

/// <summary>
/// Returns a human-readable simulation summary before activation.
/// </summary>
public sealed record RuleSimulationResponse(
    int CandidateVersion,
    int SampleCount,
    int ChangedCount,
    IReadOnlyList<RuleDecisionDifferenceResponse> Differences);

/// <summary>
/// Returns one bounded page and stable navigation metadata so clients never
/// request an unlimited operational history.
/// </summary>
public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

/// <summary>
/// Returns current operational counters and one bounded decision-history page.
/// </summary>
public sealed record OperationsOverviewResponse(
    int TotalDecisions,
    int ProcessedToday,
    int AwaitingInsuranceApproval,
    int ImportIssues,
    int PendingBatchRows,
    string DecisionRange,
    string DecisionFilter,
    PagedResponse<RoutingDecisionResponse> DecisionHistory);

/// <summary>
/// Returns one privacy-safe audit event with controlled details only.
/// </summary>
public sealed record ActivityResponse(
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
/// Returns one privacy-safe import row represented by the issue or durable
/// queue KPI, without raw XML or recipient information.
/// </summary>
public sealed record ImportAttentionResponse(
    Guid RowId,
    Guid BatchId,
    int RowNumber,
    string Status,
    string? ErrorCode,
    string? ErrorMessage,
    int AttemptCount,
    DateTimeOffset BatchCreatedAtUtc);

/// <summary>
/// Returns one bounded durable-import history item without its complete rows.
/// </summary>
public sealed record BatchSummaryResponse(
    Guid Id,
    string? FallbackDestinationCountry,
    string Status,
    int TotalRows,
    int CompletedRows,
    int FailedRows,
    int AwaitingInsuranceApproval,
    DateTimeOffset CreatedAtUtc,
    string CreatedBy);

/// <summary>
/// Returns the current authenticated identity and server-authoritative roles so
/// the browser can mirror, but never replace, authorization decisions.
/// </summary>
public sealed record CurrentIdentityResponse(
    string ActorId,
    string DisplayName,
    IReadOnlyList<string> Roles,
    bool IsDevelopmentIdentity);

/// <summary>
/// Returns one independently durable batch-row state and its optional immutable
/// routing outcome.
/// </summary>
public sealed record BatchRowResponse(
    Guid Id,
    int RowNumber,
    decimal WeightKilograms,
    decimal DeclaredValueEuros,
    string DestinationCountry,
    string CountrySource,
    string Status,
    string? ErrorCode,
    string? ErrorMessage,
    int AttemptCount,
    RoutingDecisionResponse? Decision);

/// <summary>
/// Returns durable batch progress and ordered row outcomes suitable for polling
/// after an asynchronous XML import.
/// </summary>
public sealed record BatchResponse(
    Guid Id,
    bool WasCreated,
    string? FallbackDestinationCountry,
    string Status,
    int TotalRows,
    int CompletedRows,
    int FailedRows,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<BatchRowResponse> Rows);
