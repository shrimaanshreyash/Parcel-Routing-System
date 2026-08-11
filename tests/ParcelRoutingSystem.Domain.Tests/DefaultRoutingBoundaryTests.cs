using System.Globalization;
using ParcelRoutingSystem.Domain.Routing;

namespace ParcelRoutingSystem.Domain.Tests;

/// <summary>
/// Protects the exact default weight boundaries so a
/// future threshold change cannot silently reroute parcels.
/// </summary>
public sealed class DefaultRoutingBoundaryTests
{
    /// <summary>
    /// Verifies that the inclusive 1 kg boundary remains in Mail Department.
    /// </summary>
    [Fact]
    public void Route_WhenWeightIsExactlyOneKilogram_SelectsMailDepartment()
    {
        RoutingDecision decision = RoutingTestFixture.Route(1m, 0m);

        Assert.Equal(RoutingDepartment.Mail, decision.IntendedDepartment);
        Assert.Contains(DefaultRoutingRuleIds.MailWeight, decision.MatchedRuleIds);
    }

    /// <summary>
    /// Verifies that the inclusive 10 kg boundary remains in Regular Department.
    /// </summary>
    [Fact]
    public void Route_WhenWeightIsExactlyTenKilograms_SelectsRegularDepartment()
    {
        RoutingDecision decision = RoutingTestFixture.Route(10m, 0m);

        Assert.Equal(RoutingDepartment.Regular, decision.IntendedDepartment);
        Assert.Contains(DefaultRoutingRuleIds.RegularWeight, decision.MatchedRuleIds);
    }

    /// <summary>
    /// Verifies that the first representable sample above 10 kg selects Heavy
    /// Department rather than remaining in the inclusive Regular band.
    /// </summary>
    [Fact]
    public void Route_WhenWeightIsImmediatelyAboveTenKilograms_SelectsHeavyDepartment()
    {
        RoutingDecision decision = RoutingTestFixture.Route(10.0001m, 0m);

        Assert.Equal(RoutingDepartment.Heavy, decision.IntendedDepartment);
        Assert.Contains(DefaultRoutingRuleIds.HeavyWeight, decision.MatchedRuleIds);
    }

    /// <summary>
    /// Exercises values adjacent to both thresholds and verifies the configured
    /// intervals are inclusive above and exclusive below exactly as specified.
    /// </summary>
    /// <param name="weightText">Invariant decimal text used by the test case.</param>
    /// <param name="expectedDepartment">The department required at that weight.</param>
    [Theory]
    [InlineData("0.9999", RoutingDepartment.Mail)]
    [InlineData("1.0001", RoutingDepartment.Regular)]
    [InlineData("9.9999", RoutingDepartment.Regular)]
    [InlineData("10.0001", RoutingDepartment.Heavy)]
    public void Route_WhenWeightIsAdjacentToBoundary_SelectsExpectedDepartment(
        string weightText,
        RoutingDepartment expectedDepartment)
    {
        decimal weight = decimal.Parse(weightText, CultureInfo.InvariantCulture);

        RoutingDecision decision = RoutingTestFixture.Route(weight, 0m);

        Assert.Equal(expectedDepartment, decision.IntendedDepartment);
    }
}
