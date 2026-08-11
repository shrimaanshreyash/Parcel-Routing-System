using System.Globalization;
using ParcelRoutingSystem.Domain.Parcels;

namespace ParcelRoutingSystem.Domain.Routing;

/// <summary>
/// Defines one constrained weight interval and its intended department. The
/// interval is always lower-exclusive and optionally upper-inclusive, allowing
/// adjacent rules to express exact business boundaries without ambiguity.
/// </summary>
public sealed class WeightBandRule
{
    private WeightBandRule(
        RuleId id,
        int priority,
        decimal lowerBoundExclusive,
        decimal? upperBoundInclusive,
        RoutingDepartment department)
    {
        Id = id;
        Priority = priority;
        LowerBoundExclusive = lowerBoundExclusive;
        UpperBoundInclusive = upperBoundInclusive;
        Department = department;
    }

    /// <summary>
    /// Gets the stable identifier recorded when this rule matches.
    /// </summary>
    public RuleId Id { get; }

    /// <summary>
    /// Gets the explicit ordering value used to keep rule evaluation and
    /// explanations stable.
    /// </summary>
    public int Priority { get; }

    /// <summary>
    /// Gets the exclusive lower weight boundary in kilograms.
    /// </summary>
    public decimal LowerBoundExclusive { get; }

    /// <summary>
    /// Gets the optional inclusive upper weight boundary in kilograms. A null
    /// value makes this the final catch-all band.
    /// </summary>
    public decimal? UpperBoundInclusive { get; }

    /// <summary>
    /// Gets the intended routing department assigned by this band.
    /// </summary>
    public RoutingDepartment Department { get; }

    /// <summary>
    /// Creates a typed weight-band rule after validating its local constraints.
    /// Cross-rule gaps and overlaps are validated by <see cref="RoutingRuleSet"/>.
    /// </summary>
    /// <param name="id">The stable identifier exposed in decisions.</param>
    /// <param name="priority">A positive explicit evaluation priority.</param>
    /// <param name="lowerBoundExclusive">The non-negative exclusive lower boundary.</param>
    /// <param name="upperBoundInclusive">The optional inclusive upper boundary.</param>
    /// <param name="department">The known department assigned by the rule.</param>
    /// <returns>An immutable constrained weight-band rule.</returns>
    /// <exception cref="DomainValidationException">
    /// Thrown when a boundary, priority, or department is invalid.
    /// </exception>
    public static WeightBandRule Create(
        RuleId id,
        int priority,
        decimal lowerBoundExclusive,
        decimal? upperBoundInclusive,
        RoutingDepartment department)
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
                "Routing rule priority must be greater than zero.",
                nameof(priority));
        }

        if (lowerBoundExclusive < 0m)
        {
            throw new DomainValidationException(
                DomainErrorCodes.RoutingRuleInvalid,
                "A weight rule lower boundary cannot be negative.",
                nameof(lowerBoundExclusive));
        }

        if (upperBoundInclusive.HasValue
            && upperBoundInclusive.Value <= lowerBoundExclusive)
        {
            throw new DomainValidationException(
                DomainErrorCodes.RoutingRuleInvalid,
                "A weight rule upper boundary must be greater than its lower boundary.",
                nameof(upperBoundInclusive));
        }

        if (!Enum.IsDefined(department))
        {
            throw new DomainValidationException(
                DomainErrorCodes.RoutingDepartmentInvalid,
                "A weight rule must assign a known routing department.",
                nameof(department));
        }

        return new WeightBandRule(
            id,
            priority,
            lowerBoundExclusive,
            upperBoundInclusive,
            department);
    }

    /// <summary>
    /// Determines whether a validated parcel weight falls inside this rule's
    /// lower-exclusive and upper-inclusive interval.
    /// </summary>
    /// <param name="weight">The validated weight to compare.</param>
    /// <returns>True when this rule is the matching department rule.</returns>
    public bool Matches(Weight weight)
    {
        return weight.Kilograms > LowerBoundExclusive
            && (!UpperBoundInclusive.HasValue
                || weight.Kilograms <= UpperBoundInclusive.Value);
    }

    /// <summary>
    /// Creates a stable plain-language explanation of why the matching weight
    /// produced this department.
    /// </summary>
    /// <param name="weight">The validated weight that matched the rule.</param>
    /// <returns>A human-readable routing reason containing the exact boundaries.</returns>
    public string Explain(Weight weight)
    {
        string department = $"{Department} Department";
        string lower = FormatKilograms(LowerBoundExclusive);

        if (!UpperBoundInclusive.HasValue)
        {
            return $"Weight {weight} is above {lower}, so the intended department is {department}.";
        }

        string upper = FormatKilograms(UpperBoundInclusive.Value);
        return $"Weight {weight} is above {lower} and up to and including {upper}, "
            + $"so the intended department is {department}.";
    }

    /// <summary>
    /// Formats one decimal kilogram boundary invariantly so explanations remain
    /// deterministic across operating-system cultures.
    /// </summary>
    /// <param name="kilograms">The exact decimal boundary to format.</param>
    /// <returns>The invariant number followed by the kilogram unit.</returns>
    private static string FormatKilograms(decimal kilograms)
    {
        return $"{kilograms.ToString("0.############################", CultureInfo.InvariantCulture)} kg";
    }
}
