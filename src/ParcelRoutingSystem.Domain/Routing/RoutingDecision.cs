using System.Collections.ObjectModel;

namespace ParcelRoutingSystem.Domain.Routing;

/// <summary>
/// Captures the complete deterministic business result for one parcel,
/// including its intended department, approval hold, matched rules, version,
/// and ordered human-readable reasons.
/// </summary>
public sealed class RoutingDecision
{
    private RoutingDecision(
        RoutingDepartment intendedDepartment,
        ApprovalState approvalState,
        RuleSetVersion ruleSetVersion,
        RuleId[] matchedRuleIds,
        string[] reasons,
        RoutingDecisionContext context)
    {
        IntendedDepartment = intendedDepartment;
        ApprovalState = approvalState;
        RuleSetVersion = ruleSetVersion;
        MatchedRuleIds = new ReadOnlyCollection<RuleId>(matchedRuleIds);
        Reasons = new ReadOnlyCollection<string>(reasons);
        DecidedAtUtc = context.DecidedAtUtc;
        CorrelationId = context.CorrelationId;
    }

    /// <summary>
    /// Gets the physical department selected by the weight rules.
    /// </summary>
    public RoutingDepartment IntendedDepartment { get; }

    /// <summary>
    /// Gets whether the parcel can proceed or must wait for insurance approval.
    /// </summary>
    public ApprovalState ApprovalState { get; }

    /// <summary>
    /// Gets the immutable rule-set version used for the decision.
    /// </summary>
    public RuleSetVersion RuleSetVersion { get; }

    /// <summary>
    /// Gets the ordered stable identifiers of every rule that affected the
    /// decision.
    /// </summary>
    public IReadOnlyList<RuleId> MatchedRuleIds { get; }

    /// <summary>
    /// Gets the ordered plain-language reasons for the routing and approval
    /// outcomes.
    /// </summary>
    public IReadOnlyList<string> Reasons { get; }

    /// <summary>
    /// Gets the caller-supplied UTC decision timestamp without reading a clock
    /// inside the pure domain.
    /// </summary>
    public DateTimeOffset DecidedAtUtc { get; }

    /// <summary>
    /// Gets the caller-supplied correlation identifier used to trace the
    /// decision across later application boundaries.
    /// </summary>
    public string CorrelationId { get; }

    /// <summary>
    /// Creates the immutable decision after a validated rule set has produced
    /// exactly one department outcome and one approval outcome.
    /// </summary>
    /// <param name="intendedDepartment">The physical department selected by weight.</param>
    /// <param name="approvalState">The independent insurance workflow state.</param>
    /// <param name="ruleSetVersion">The version that produced this decision.</param>
    /// <param name="matchedRuleIds">The ordered identifiers of matched rules.</param>
    /// <param name="reasons">The ordered human-readable decision reasons.</param>
    /// <param name="context">Caller-controlled trace and time metadata.</param>
    /// <returns>A complete immutable and explainable decision.</returns>
    internal static RoutingDecision Create(
        RoutingDepartment intendedDepartment,
        ApprovalState approvalState,
        RuleSetVersion ruleSetVersion,
        IEnumerable<RuleId> matchedRuleIds,
        IEnumerable<string> reasons,
        RoutingDecisionContext context)
    {
        RuleId[] materializedIds = matchedRuleIds.ToArray();
        string[] materializedReasons = reasons.ToArray();

        if (materializedIds.Length == 0 || materializedReasons.Length == 0)
        {
            throw new UnsafeRoutingDecisionException(
                "A routing decision must contain matched rules and explanations.");
        }

        return new RoutingDecision(
            intendedDepartment,
            approvalState,
            ruleSetVersion,
            materializedIds,
            materializedReasons,
            context);
    }
}
