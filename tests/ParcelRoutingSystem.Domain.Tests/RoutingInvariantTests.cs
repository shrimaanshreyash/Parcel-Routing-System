using ParcelRoutingSystem.Domain.Routing;

namespace ParcelRoutingSystem.Domain.Tests;

/// <summary>
/// Samples broad numeric ranges to protect routing invariants that are more
/// general than individual boundary examples.
/// </summary>
public sealed class RoutingInvariantTests
{
    /// <summary>
    /// Verifies a dense deterministic sample of positive weights always selects
    /// exactly the department implied by the default thresholds.
    /// </summary>
    [Fact]
    public void Route_ForDensePositiveWeightSample_SelectsExactlyOneExpectedDepartment()
    {
        RoutingRuleSet ruleSet = RoutingRuleSet.CreateDefault();
        RoutingDecisionContext context =
            RoutingDecisionContext.Create(RoutingTestFixture.DecisionTime, "weight-invariant");

        for (int hundredths = 1; hundredths <= 25_000; hundredths++)
        {
            decimal weight = hundredths / 100m;
            RoutingDepartment expected = weight <= 1m
                ? RoutingDepartment.Mail
                : weight <= 10m
                    ? RoutingDepartment.Regular
                    : RoutingDepartment.Heavy;
            var parcel = RoutingTestFixture.CreateParcel(weight, 0m);

            RoutingDecision decision = ruleSet.Route(parcel, context);

            Assert.Equal(expected, decision.IntendedDepartment);
            Assert.Single(decision.MatchedRuleIds);
        }
    }

    /// <summary>
    /// Verifies approval remains false at and below EUR 1,000 and true for every
    /// sampled value above it without affecting the intended department.
    /// </summary>
    [Fact]
    public void Route_ForDenseValueSample_AppliesStrictInsuranceThreshold()
    {
        RoutingRuleSet ruleSet = RoutingRuleSet.CreateDefault();
        RoutingDecisionContext context =
            RoutingDecisionContext.Create(RoutingTestFixture.DecisionTime, "value-invariant");

        for (int wholeEuros = 0; wholeEuros <= 2_000; wholeEuros++)
        {
            var parcel = RoutingTestFixture.CreateParcel(5m, wholeEuros);
            ApprovalState expected = wholeEuros > 1_000
                ? ApprovalState.PendingInsuranceApproval
                : ApprovalState.NotRequired;

            RoutingDecision decision = ruleSet.Route(parcel, context);

            Assert.Equal(RoutingDepartment.Regular, decision.IntendedDepartment);
            Assert.Equal(expected, decision.ApprovalState);
        }
    }
}
