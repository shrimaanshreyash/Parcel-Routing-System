using ParcelRoutingSystem.Domain;
using ParcelRoutingSystem.Domain.Parcels;
using ParcelRoutingSystem.Domain.Routing;

namespace ParcelRoutingSystem.Domain.Tests;

/// <summary>
/// Verifies unsafe configuration cannot become an evaluatable rule set and a
/// valid versioned change remains possible without rewriting the evaluator.
/// </summary>
public sealed class RoutingRuleSetSafetyTests
{
    /// <summary>
    /// Verifies a gap between adjacent weight bands is rejected so parcels
    /// cannot fall through without a safe department.
    /// </summary>
    [Fact]
    public void Create_WhenWeightBandsContainGap_RejectsRuleSet()
    {
        WeightBandRule[] rules =
        [
            Band("MAIL", 100, 0m, 1m, RoutingDepartment.Mail),
            Band("HEAVY", 200, 2m, null, RoutingDepartment.Heavy),
        ];

        RuleSetValidationException exception = Assert.Throws<RuleSetValidationException>(
            () => CreateRuleSet(rules));

        Assert.Contains(exception.Errors, error => error.Contains("gap", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies overlapping bands are rejected so evaluation cannot select more
    /// than one intended department.
    /// </summary>
    [Fact]
    public void Create_WhenWeightBandsOverlap_RejectsRuleSet()
    {
        WeightBandRule[] rules =
        [
            Band("MAIL", 100, 0m, 2m, RoutingDepartment.Mail),
            Band("REGULAR", 200, 1m, 10m, RoutingDepartment.Regular),
            Band("HEAVY", 300, 10m, null, RoutingDepartment.Heavy),
        ];

        RuleSetValidationException exception = Assert.Throws<RuleSetValidationException>(
            () => CreateRuleSet(rules));

        Assert.Contains(
            exception.Errors,
            error => error.Contains("overlap", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies the final unbounded band is mandatory so every positive weight
    /// has exactly one safe destination.
    /// </summary>
    [Fact]
    public void Create_WhenCatchAllBandIsMissing_RejectsRuleSet()
    {
        WeightBandRule[] rules =
        [
            Band("MAIL", 100, 0m, 1m, RoutingDepartment.Mail),
            Band("REGULAR", 200, 1m, 10m, RoutingDepartment.Regular),
        ];

        RuleSetValidationException exception = Assert.Throws<RuleSetValidationException>(
            () => CreateRuleSet(rules));

        Assert.Contains(
            exception.Errors,
            error => error.Contains("catch-all", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies stable rule identifiers are unique across routing and approval
    /// effects so a decision explanation cannot be ambiguous.
    /// </summary>
    [Fact]
    public void Create_WhenRuleIdentifierIsDuplicated_RejectsRuleSet()
    {
        RuleId duplicatedId = RuleId.From("DUPLICATE");
        WeightBandRule[] rules =
        [
            WeightBandRule.Create(duplicatedId, 100, 0m, 1m, RoutingDepartment.Mail),
            WeightBandRule.Create(duplicatedId, 200, 1m, null, RoutingDepartment.Heavy),
        ];

        RuleSetValidationException exception = Assert.Throws<RuleSetValidationException>(
            () => CreateRuleSet(rules));

        Assert.Contains(
            exception.Errors,
            error => error.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies duplicate explicit priorities are rejected so future ordered
    /// rule presentation and evaluation cannot become ambiguous.
    /// </summary>
    [Fact]
    public void Create_WhenDepartmentPriorityIsDuplicated_RejectsRuleSet()
    {
        WeightBandRule[] rules =
        [
            Band("MAIL", 100, 0m, 1m, RoutingDepartment.Mail),
            Band("REGULAR", 100, 1m, 10m, RoutingDepartment.Regular),
            Band("HEAVY", 300, 10m, null, RoutingDepartment.Heavy),
        ];

        RuleSetValidationException exception = Assert.Throws<RuleSetValidationException>(
            () => CreateRuleSet(rules));

        Assert.Contains(
            exception.Errors,
            error => error.Contains("priority", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies a default value-type rule-set version cannot bypass the positive
    /// version factory.
    /// </summary>
    [Fact]
    public void Create_WhenRuleSetVersionIsDefault_RejectsRuleSet()
    {
        WeightBandRule[] rules =
        [
            Band("MAIL", 100, 0m, 1m, RoutingDepartment.Mail),
            Band("REGULAR", 200, 1m, 10m, RoutingDepartment.Regular),
            Band("HEAVY", 300, 10m, null, RoutingDepartment.Heavy),
        ];
        InsuranceApprovalRule insuranceRule = InsuranceApprovalRule.Create(
            RuleId.From("INSURANCE"),
            1_000,
            DeclaredValue.FromEuros(1_000m));

        DomainValidationException exception = Assert.Throws<DomainValidationException>(
            () => RoutingRuleSet.Create(default, rules, insuranceRule));

        Assert.Equal(DomainErrorCodes.RuleSetVersionInvalid, exception.Code);
    }

    /// <summary>
    /// Verifies undefined department values fail at rule creation before they can
    /// enter a versioned rule set.
    /// </summary>
    [Fact]
    public void Create_WhenDepartmentIsUnknown_RejectsRule()
    {
        DomainValidationException exception = Assert.Throws<DomainValidationException>(
            () => WeightBandRule.Create(
                RuleId.From("UNKNOWN-DEPARTMENT"),
                100,
                0m,
                null,
                (RoutingDepartment)999));

        Assert.Equal(DomainErrorCodes.RoutingDepartmentInvalid, exception.Code);
    }

    /// <summary>
    /// Verifies a valid threshold change can be expressed as a new immutable
    /// version and evaluated by the unchanged routing engine.
    /// </summary>
    [Fact]
    public void Route_WhenVersionedWeightThresholdChanges_UsesNewRuleSet()
    {
        WeightBandRule[] rules =
        [
            Band("MAIL-UP-TO-2-KG", 100, 0m, 2m, RoutingDepartment.Mail),
            Band("REGULAR-UP-TO-10-KG", 200, 2m, 10m, RoutingDepartment.Regular),
            Band("HEAVY-OVER-10-KG", 300, 10m, null, RoutingDepartment.Heavy),
        ];
        RoutingRuleSet ruleSet = CreateRuleSet(rules, version: 2);
        var parcel = RoutingTestFixture.CreateParcel(1.5m, 0m);
        RoutingDecisionContext context =
            RoutingDecisionContext.Create(RoutingTestFixture.DecisionTime, "safe-change");

        RoutingDecision decision = ruleSet.Route(parcel, context);

        Assert.Equal(RoutingDepartment.Mail, decision.IntendedDepartment);
        Assert.Equal(RuleSetVersion.From(2), decision.RuleSetVersion);
        Assert.Contains(RuleId.From("MAIL-UP-TO-2-KG"), decision.MatchedRuleIds);
    }

    /// <summary>
    /// Creates one typed weight-band rule for semantic validation test setup.
    /// </summary>
    /// <param name="id">The stable test rule identifier.</param>
    /// <param name="priority">The explicit evaluation priority.</param>
    /// <param name="lowerExclusive">The exclusive lower weight boundary.</param>
    /// <param name="upperInclusive">The optional inclusive upper boundary.</param>
    /// <param name="department">The department assigned by the band.</param>
    /// <returns>An immutable constrained weight-band rule.</returns>
    private static WeightBandRule Band(
        string id,
        int priority,
        decimal lowerExclusive,
        decimal? upperInclusive,
        RoutingDepartment department)
    {
        return WeightBandRule.Create(
            RuleId.From(id),
            priority,
            lowerExclusive,
            upperInclusive,
            department);
    }

    /// <summary>
    /// Creates a versioned rule set with the common insurance rule used by
    /// semantic validation tests.
    /// </summary>
    /// <param name="rules">The department rules to validate.</param>
    /// <param name="version">The positive immutable rule-set version.</param>
    /// <returns>A safe evaluatable rule set when all invariants hold.</returns>
    /// <exception cref="RuleSetValidationException">
    /// Thrown when the supplied rules contain a gap, overlap, duplicate, or
    /// missing fallback.
    /// </exception>
    private static RoutingRuleSet CreateRuleSet(
        IEnumerable<WeightBandRule> rules,
        int version = 1)
    {
        InsuranceApprovalRule insuranceRule = InsuranceApprovalRule.Create(
            RuleId.From("INSURANCE-OVER-1000-EUR"),
            priority: 1_000,
            DeclaredValue.FromEuros(1_000m));

        return RoutingRuleSet.Create(RuleSetVersion.From(version), rules, insuranceRule);
    }
}
