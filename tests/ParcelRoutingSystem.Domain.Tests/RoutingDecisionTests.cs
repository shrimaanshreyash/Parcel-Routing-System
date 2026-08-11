using ParcelRoutingSystem.Domain.Routing;

namespace ParcelRoutingSystem.Domain.Tests;

/// <summary>
/// Verifies decisions remain deterministic, traceable, explainable, and
/// independent of attributes not used by the default policy.
/// </summary>
public sealed class RoutingDecisionTests
{
    /// <summary>
    /// Verifies repeated evaluation with identical parcel and caller metadata
    /// produces identical business output and explanation order.
    /// </summary>
    [Fact]
    public void Route_WhenInputsRepeat_ProducesDeterministicDecision()
    {
        RoutingDecision first = RoutingTestFixture.Route(12.5m, 1_250m, "GB", "repeat-001");
        RoutingDecision second = RoutingTestFixture.Route(12.5m, 1_250m, "GB", "repeat-001");

        Assert.Equal(first.IntendedDepartment, second.IntendedDepartment);
        Assert.Equal(first.ApprovalState, second.ApprovalState);
        Assert.Equal(first.RuleSetVersion, second.RuleSetVersion);
        Assert.Equal(first.DecidedAtUtc, second.DecidedAtUtc);
        Assert.Equal(first.CorrelationId, second.CorrelationId);
        Assert.Equal(first.MatchedRuleIds, second.MatchedRuleIds);
        Assert.Equal(first.Reasons, second.Reasons);
    }

    /// <summary>
    /// Verifies a decision identifies the matched department and insurance rules
    /// and explains both the destination and approval hold in plain language.
    /// </summary>
    [Fact]
    public void Route_WhenParcelIsHeavyAndHighValue_ExplainsEveryEffect()
    {
        RoutingDecision decision = RoutingTestFixture.Route(12.5m, 1_250m, "GB");

        Assert.Equal(RoutingDepartment.Heavy, decision.IntendedDepartment);
        Assert.Equal(ApprovalState.PendingInsuranceApproval, decision.ApprovalState);
        Assert.Equal(RuleSetVersion.From(1), decision.RuleSetVersion);
        Assert.Equal(
            [DefaultRoutingRuleIds.HeavyWeight, DefaultRoutingRuleIds.InsuranceValue],
            decision.MatchedRuleIds);
        Assert.Contains(
            decision.Reasons,
            reason => reason.Contains("Heavy Department", StringComparison.Ordinal));
        Assert.Contains(
            decision.Reasons,
            reason => reason.Contains(
                "insurance approval is required before routing",
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies valid countries are mandatory facts but do not alter the current
    /// weight-only department policy.
    /// </summary>
    [Fact]
    public void Route_WhenOnlyCountryChanges_PreservesDefaultDecision()
    {
        RoutingDecision netherlands = RoutingTestFixture.Route(4m, 500m, "NL");
        RoutingDecision japan = RoutingTestFixture.Route(4m, 500m, "JP");

        Assert.Equal(netherlands.IntendedDepartment, japan.IntendedDepartment);
        Assert.Equal(netherlands.ApprovalState, japan.ApprovalState);
        Assert.Equal(netherlands.MatchedRuleIds, japan.MatchedRuleIds);
    }

    /// <summary>
    /// Verifies optional attributes remain available on the parcel but cannot
    /// affect decisions until an allow-listed typed rule explicitly uses them.
    /// </summary>
    [Fact]
    public void Route_WhenOnlyOptionalAttributesChange_PreservesDefaultDecision()
    {
        ParcelWithDecision first = RouteWithAttribute("priority", "standard");
        ParcelWithDecision second = RouteWithAttribute("priority", "express");

        Assert.NotEqual(
            first.Parcel.AdditionalAttributes["priority"],
            second.Parcel.AdditionalAttributes["priority"]);
        Assert.Equal(first.Decision.IntendedDepartment, second.Decision.IntendedDepartment);
        Assert.Equal(first.Decision.ApprovalState, second.Decision.ApprovalState);
    }

    /// <summary>
    /// Routes a parcel carrying one optional attribute so tests can compare the
    /// preserved input with the business decision without duplicating setup.
    /// </summary>
    /// <param name="name">The allow-listed test attribute name.</param>
    /// <param name="value">The attribute value to preserve.</param>
    /// <returns>The parcel and its decision as one comparison fixture.</returns>
    private static ParcelWithDecision RouteWithAttribute(string name, string value)
    {
        var attributes = new Dictionary<string, string>
        {
            [name] = value,
        };
        var parcel = RoutingTestFixture.CreateParcel(4m, 500m, attributes: attributes);
        RoutingDecisionContext context =
            RoutingDecisionContext.Create(RoutingTestFixture.DecisionTime, "attribute-test");
        RoutingDecision decision = RoutingRuleSet.CreateDefault().Route(parcel, context);

        return new ParcelWithDecision(parcel, decision);
    }

    /// <summary>
    /// Groups the parcel and decision used by the optional-attribute comparison
    /// without creating a production-domain concept solely for testing.
    /// </summary>
    /// <param name="Parcel">The validated parcel supplied to the rule set.</param>
    /// <param name="Decision">The resulting deterministic routing decision.</param>
    private sealed record ParcelWithDecision(
        ParcelRoutingSystem.Domain.Parcels.Parcel Parcel,
        RoutingDecision Decision);
}
