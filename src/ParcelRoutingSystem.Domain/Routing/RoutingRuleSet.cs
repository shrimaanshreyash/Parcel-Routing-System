using System.Collections.ObjectModel;
using ParcelRoutingSystem.Domain.Parcels;

namespace ParcelRoutingSystem.Domain.Routing;

/// <summary>
/// Owns one immutable, semantically validated routing policy and evaluates
/// parcels without clocks, I/O, databases, XML, or framework dependencies.
/// </summary>
public sealed class RoutingRuleSet
{
    private RoutingRuleSet(
        RuleSetVersion version,
        WeightBandRule[] departmentRules,
        InsuranceApprovalRule insuranceRule)
    {
        Version = version;
        DepartmentRules = new ReadOnlyCollection<WeightBandRule>(departmentRules);
        InsuranceRule = insuranceRule;
    }

    /// <summary>
    /// Gets the immutable version recorded on every decision.
    /// </summary>
    public RuleSetVersion Version { get; }

    /// <summary>
    /// Gets the validated weight rules in deterministic boundary order.
    /// </summary>
    public IReadOnlyList<WeightBandRule> DepartmentRules { get; }

    /// <summary>
    /// Gets the independent insurance approval rule.
    /// </summary>
    public InsuranceApprovalRule InsuranceRule { get; }

    /// <summary>
    /// Creates version one of the default policy: Mail through one
    /// kilogram, Regular through ten kilograms, Heavy above ten kilograms, and
    /// an insurance hold above EUR 1,000.
    /// </summary>
    /// <returns>The validated immutable default rule set.</returns>
    public static RoutingRuleSet CreateDefault()
    {
        WeightBandRule[] departmentRules =
        [
            WeightBandRule.Create(
                DefaultRoutingRuleIds.MailWeight,
                priority: 100,
                lowerBoundExclusive: 0m,
                upperBoundInclusive: 1m,
                RoutingDepartment.Mail),
            WeightBandRule.Create(
                DefaultRoutingRuleIds.RegularWeight,
                priority: 200,
                lowerBoundExclusive: 1m,
                upperBoundInclusive: 10m,
                RoutingDepartment.Regular),
            WeightBandRule.Create(
                DefaultRoutingRuleIds.HeavyWeight,
                priority: 300,
                lowerBoundExclusive: 10m,
                upperBoundInclusive: null,
                RoutingDepartment.Heavy),
        ];
        InsuranceApprovalRule insuranceRule = InsuranceApprovalRule.Create(
            DefaultRoutingRuleIds.InsuranceValue,
            priority: 1_000,
            DeclaredValue.FromEuros(1_000m));

        return Create(RuleSetVersion.From(1), departmentRules, insuranceRule);
    }

    /// <summary>
    /// Creates an immutable rule set only after rejecting duplicate identifiers,
    /// weight gaps, overlaps, unreachable bands, and a missing catch-all.
    /// </summary>
    /// <param name="version">The positive immutable policy version.</param>
    /// <param name="departmentRules">The constrained weight-band rules.</param>
    /// <param name="insuranceRule">The independent declared-value approval rule.</param>
    /// <returns>A safe rule set ready for deterministic evaluation.</returns>
    /// <exception cref="RuleSetValidationException">
    /// Thrown with all discovered semantic problems when evaluation would be unsafe.
    /// </exception>
    public static RoutingRuleSet Create(
        RuleSetVersion version,
        IEnumerable<WeightBandRule> departmentRules,
        InsuranceApprovalRule insuranceRule)
    {
        ArgumentNullException.ThrowIfNull(departmentRules);
        ArgumentNullException.ThrowIfNull(insuranceRule);

        if (version.Value <= 0)
        {
            throw new DomainValidationException(
                DomainErrorCodes.RuleSetVersionInvalid,
                "Rule-set version must be greater than zero.",
                nameof(version));
        }

        WeightBandRule[] orderedRules = departmentRules
            .OrderBy(rule => rule.LowerBoundExclusive)
            .ThenBy(rule => rule.Priority)
            .ToArray();
        string[] errors = FindValidationErrors(orderedRules, insuranceRule).ToArray();

        if (errors.Length > 0)
        {
            throw new RuleSetValidationException(errors);
        }

        return new RoutingRuleSet(version, orderedRules, insuranceRule);
    }

    /// <summary>
    /// Evaluates a parcel against this immutable policy, failing closed unless
    /// exactly one department rule matches.
    /// </summary>
    /// <param name="parcel">The validated parcel facts to evaluate.</param>
    /// <param name="context">Caller-supplied UTC time and correlation metadata.</param>
    /// <returns>An explainable versioned routing decision.</returns>
    /// <exception cref="UnsafeRoutingDecisionException">
    /// Thrown rather than guessing when zero or multiple department rules match.
    /// </exception>
    public RoutingDecision Route(Parcel parcel, RoutingDecisionContext context)
    {
        ArgumentNullException.ThrowIfNull(parcel);
        ArgumentNullException.ThrowIfNull(context);

        WeightBandRule[] matchedDepartmentRules = DepartmentRules
            .Where(rule => rule.Matches(parcel.Weight))
            .ToArray();

        if (matchedDepartmentRules.Length != 1)
        {
            throw new UnsafeRoutingDecisionException(
                $"Expected exactly one department rule for weight {parcel.Weight}, "
                + $"but matched {matchedDepartmentRules.Length}.");
        }

        WeightBandRule departmentRule = matchedDepartmentRules[0];
        bool insuranceRequired = InsuranceRule.Matches(parcel.DeclaredValue);
        var matchedRuleIds = new List<RuleId>
        {
            departmentRule.Id,
        };

        if (insuranceRequired)
        {
            matchedRuleIds.Add(InsuranceRule.Id);
        }

        string[] reasons =
        [
            departmentRule.Explain(parcel.Weight),
            InsuranceRule.Explain(parcel.DeclaredValue, insuranceRequired),
        ];
        ApprovalState approvalState = insuranceRequired
            ? ApprovalState.PendingInsuranceApproval
            : ApprovalState.NotRequired;

        return RoutingDecision.Create(
            departmentRule.Department,
            approvalState,
            Version,
            matchedRuleIds,
            reasons,
            context);
    }

    /// <summary>
    /// Finds every semantic configuration error in deterministic order so a
    /// proposed rule set is rejected before any parcel can be evaluated.
    /// </summary>
    /// <param name="rules">The materialized weight rules in boundary order.</param>
    /// <param name="insuranceRule">The insurance rule checked for identifier collisions.</param>
    /// <returns>The complete ordered sequence of validation findings.</returns>
    private static IEnumerable<string> FindValidationErrors(
        IReadOnlyList<WeightBandRule> rules,
        InsuranceApprovalRule insuranceRule)
    {
        if (rules.Count == 0)
        {
            yield return "At least one department rule is required.";
            yield break;
        }

        IEnumerable<IGrouping<RuleId, RuleId>> duplicateIdentifiers = rules
            .Select(rule => rule.Id)
            .Append(insuranceRule.Id)
            .GroupBy(id => id)
            .Where(group => group.Count() > 1);

        foreach (IGrouping<RuleId, RuleId> duplicate in duplicateIdentifiers)
        {
            yield return $"Duplicate rule identifier '{duplicate.Key}' is not allowed.";
        }

        IEnumerable<IGrouping<int, WeightBandRule>> duplicatePriorities = rules
            .GroupBy(rule => rule.Priority)
            .Where(group => group.Count() > 1);

        foreach (IGrouping<int, WeightBandRule> duplicate in duplicatePriorities)
        {
            yield return $"Duplicate department-rule priority '{duplicate.Key}' is ambiguous.";
        }

        if (rules[0].LowerBoundExclusive != 0m)
        {
            yield return "The department rules contain a gap above zero kilograms.";
        }

        for (int index = 1; index < rules.Count; index++)
        {
            WeightBandRule previous = rules[index - 1];
            WeightBandRule current = rules[index];

            if (!previous.UpperBoundInclusive.HasValue)
            {
                yield return $"Rule '{current.Id}' is unreachable after catch-all rule '{previous.Id}'.";
                continue;
            }

            decimal previousUpper = previous.UpperBoundInclusive.Value;
            if (current.LowerBoundExclusive > previousUpper)
            {
                yield return $"Rules '{previous.Id}' and '{current.Id}' contain a weight gap.";
            }
            else if (current.LowerBoundExclusive < previousUpper)
            {
                yield return $"Rules '{previous.Id}' and '{current.Id}' overlap.";
            }
        }

        if (rules[^1].UpperBoundInclusive.HasValue)
        {
            yield return "The final department rule must be an unbounded catch-all.";
        }
    }
}
