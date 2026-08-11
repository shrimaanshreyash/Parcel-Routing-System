using ParcelRoutingSystem.Domain.Routing;

namespace ParcelRoutingSystem.Domain.Tests;

/// <summary>
/// Protects insurance as an additive approval hold that never replaces the
/// parcel's intended weight-based department.
/// </summary>
public sealed class InsuranceApprovalTests
{
    /// <summary>
    /// Verifies that exactly EUR 1,000 does not satisfy the strictly-greater-than
    /// insurance condition.
    /// </summary>
    [Fact]
    public void Route_WhenValueIsExactlyOneThousandEuros_DoesNotRequireApproval()
    {
        RoutingDecision decision = RoutingTestFixture.Route(2m, 1_000m);

        Assert.Equal(ApprovalState.NotRequired, decision.ApprovalState);
        Assert.DoesNotContain(DefaultRoutingRuleIds.InsuranceValue, decision.MatchedRuleIds);
    }

    /// <summary>
    /// Verifies that the first cent below EUR 1,000 remains below the strict
    /// insurance threshold.
    /// </summary>
    [Fact]
    public void Route_WhenValueIsImmediatelyBelowOneThousandEuros_DoesNotRequireApproval()
    {
        RoutingDecision decision = RoutingTestFixture.Route(2m, 999.99m);

        Assert.Equal(ApprovalState.NotRequired, decision.ApprovalState);
        Assert.DoesNotContain(DefaultRoutingRuleIds.InsuranceValue, decision.MatchedRuleIds);
    }

    /// <summary>
    /// Verifies that the first cent above EUR 1,000 requires insurance approval.
    /// </summary>
    [Fact]
    public void Route_WhenValueIsImmediatelyAboveOneThousandEuros_RequiresApproval()
    {
        RoutingDecision decision = RoutingTestFixture.Route(2m, 1_000.01m);

        Assert.Equal(ApprovalState.PendingInsuranceApproval, decision.ApprovalState);
        Assert.Contains(DefaultRoutingRuleIds.InsuranceValue, decision.MatchedRuleIds);
    }

    /// <summary>
    /// Verifies that high value adds the same approval hold in every department
    /// without changing the intended weight-based destination.
    /// </summary>
    /// <param name="weightKilograms">A representative weight for one department.</param>
    /// <param name="expectedDepartment">The department selected by that weight.</param>
    [Theory]
    [InlineData(0.5, RoutingDepartment.Mail)]
    [InlineData(5, RoutingDepartment.Regular)]
    [InlineData(15, RoutingDepartment.Heavy)]
    public void Route_WhenParcelIsHighValue_PreservesDepartmentAndRequiresApproval(
        decimal weightKilograms,
        RoutingDepartment expectedDepartment)
    {
        RoutingDecision decision = RoutingTestFixture.Route(weightKilograms, 1_500m);

        Assert.Equal(expectedDepartment, decision.IntendedDepartment);
        Assert.Equal(ApprovalState.PendingInsuranceApproval, decision.ApprovalState);
        Assert.Equal(2, decision.MatchedRuleIds.Count);
    }
}
