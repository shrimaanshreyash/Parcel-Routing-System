using ParcelRoutingSystem.Domain.Parcels;
using ParcelRoutingSystem.Domain.Routing;

namespace ParcelRoutingSystem.Application.Rules;

/// <summary>
/// Defines one persistence-safe typed weight band without exposing arbitrary
/// expressions or executable rule content.
/// </summary>
public sealed record WeightBandDefinition(
    string RuleId,
    int Priority,
    decimal LowerBoundExclusive,
    decimal? UpperBoundInclusive,
    RoutingDepartment Department);

/// <summary>
/// Defines the single constrained insurance threshold stored with a rule-set
/// version.
/// </summary>
public sealed record InsuranceRuleDefinition(
    string RuleId,
    int Priority,
    decimal ThresholdExclusiveEuros);

/// <summary>
/// Represents a complete versioned rule configuration that can be validated by
/// reconstructing the pure domain rule set before storage or activation.
/// </summary>
public sealed class RuleSetDefinition
{
    /// <summary>
    /// Creates one immutable application representation of a rule-set version.
    /// </summary>
    /// <param name="version">The positive immutable version number.</param>
    /// <param name="weightBands">The complete constrained department bands.</param>
    /// <param name="insuranceRule">The independent insurance threshold rule.</param>
    public RuleSetDefinition(
        int version,
        IReadOnlyList<WeightBandDefinition> weightBands,
        InsuranceRuleDefinition insuranceRule)
    {
        Version = version;
        WeightBands = weightBands;
        InsuranceRule = insuranceRule;
    }

    /// <summary>Gets the immutable rule-set version number.</summary>
    public int Version { get; }

    /// <summary>Gets the constrained department bands.</summary>
    public IReadOnlyList<WeightBandDefinition> WeightBands { get; }

    /// <summary>Gets the independent insurance rule.</summary>
    public InsuranceRuleDefinition InsuranceRule { get; }

    /// <summary>
    /// Reconstructs and semantically validates the pure domain rule set so
    /// unsafe persisted or proposed configurations fail closed.
    /// </summary>
    /// <returns>A pure deterministic rule set ready for evaluation.</returns>
    public RoutingRuleSet ToDomain()
    {
        WeightBandRule[] bands = WeightBands
            .Select(
                band => WeightBandRule.Create(
                    RuleId.From(band.RuleId),
                    band.Priority,
                    band.LowerBoundExclusive,
                    band.UpperBoundInclusive,
                    band.Department))
            .ToArray();
        InsuranceApprovalRule insuranceRule = InsuranceApprovalRule.Create(
            RuleId.From(InsuranceRule.RuleId),
            InsuranceRule.Priority,
            DeclaredValue.FromEuros(InsuranceRule.ThresholdExclusiveEuros));

        return RoutingRuleSet.Create(
            RuleSetVersion.From(Version),
            bands,
            insuranceRule);
    }

    /// <summary>
    /// Captures an already validated domain rule set in the constrained
    /// persistence representation without losing identifiers or boundaries.
    /// </summary>
    /// <param name="ruleSet">The pure domain rule set to capture.</param>
    /// <returns>A complete immutable application definition.</returns>
    public static RuleSetDefinition FromDomain(RoutingRuleSet ruleSet)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);

        WeightBandDefinition[] bands = ruleSet.DepartmentRules
            .Select(
                rule => new WeightBandDefinition(
                    rule.Id.Value,
                    rule.Priority,
                    rule.LowerBoundExclusive,
                    rule.UpperBoundInclusive,
                    rule.Department))
            .ToArray();

        return new RuleSetDefinition(
            ruleSet.Version.Value,
            bands,
            new InsuranceRuleDefinition(
                ruleSet.InsuranceRule.Id.Value,
                ruleSet.InsuranceRule.Priority,
                ruleSet.InsuranceRule.ThresholdExclusive.Euros));
    }
}
