using ParcelRoutingSystem.Domain.Parcels;

namespace ParcelRoutingSystem.Domain.Routing;

/// <summary>
/// Defines the declared-value threshold that adds an insurance workflow hold.
/// This rule never replaces or changes the parcel's intended department.
/// </summary>
public sealed class InsuranceApprovalRule
{
    private InsuranceApprovalRule(
        RuleId id,
        int priority,
        DeclaredValue thresholdExclusive)
    {
        Id = id;
        Priority = priority;
        ThresholdExclusive = thresholdExclusive;
    }

    /// <summary>
    /// Gets the stable identifier recorded when insurance approval is required.
    /// </summary>
    public RuleId Id { get; }

    /// <summary>
    /// Gets the explicit priority used for deterministic explanation ordering.
    /// </summary>
    public int Priority { get; }

    /// <summary>
    /// Gets the exclusive declared-value threshold in EUR.
    /// </summary>
    public DeclaredValue ThresholdExclusive { get; }

    /// <summary>
    /// Creates the constrained rule that controls the insurance approval hold.
    /// </summary>
    /// <param name="id">The stable identifier exposed in decisions.</param>
    /// <param name="priority">A positive explicit evaluation priority.</param>
    /// <param name="thresholdExclusive">The positive exclusive EUR threshold.</param>
    /// <returns>An immutable insurance approval rule.</returns>
    /// <exception cref="DomainValidationException">
    /// Thrown when the priority or threshold cannot form a safe rule.
    /// </exception>
    public static InsuranceApprovalRule Create(
        RuleId id,
        int priority,
        DeclaredValue thresholdExclusive)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
        {
            throw new DomainValidationException(
                DomainErrorCodes.RuleIdInvalid,
                "Rule identifier is required.",
                nameof(id));
        }

        if (priority <= 0)
        {
            throw new DomainValidationException(
                DomainErrorCodes.RoutingRuleInvalid,
                "Insurance rule priority must be greater than zero.",
                nameof(priority));
        }

        if (thresholdExclusive.Euros <= 0m)
        {
            throw new DomainValidationException(
                DomainErrorCodes.RoutingRuleInvalid,
                "Insurance approval threshold must be greater than zero EUR.",
                nameof(thresholdExclusive));
        }

        return new InsuranceApprovalRule(id, priority, thresholdExclusive);
    }

    /// <summary>
    /// Determines whether the declared value is strictly above the approval
    /// threshold, preserving the exact EUR 1,000 boundary.
    /// </summary>
    /// <param name="declaredValue">The validated EUR value to compare.</param>
    /// <returns>True when routing must wait for insurance approval.</returns>
    public bool Matches(DeclaredValue declaredValue)
    {
        return declaredValue.Euros > ThresholdExclusive.Euros;
    }

    /// <summary>
    /// Explains the approval outcome without implying that insurance is a
    /// routing department.
    /// </summary>
    /// <param name="declaredValue">The validated value evaluated by the rule.</param>
    /// <param name="approvalRequired">Whether the exclusive threshold matched.</param>
    /// <returns>A stable plain-language workflow explanation.</returns>
    public string Explain(DeclaredValue declaredValue, bool approvalRequired)
    {
        if (approvalRequired)
        {
            return $"Declared value {declaredValue} is above {ThresholdExclusive}; "
                + "insurance approval is required before routing.";
        }

        return $"Declared value {declaredValue} is not above {ThresholdExclusive}; "
            + "insurance approval is not required.";
    }
}
