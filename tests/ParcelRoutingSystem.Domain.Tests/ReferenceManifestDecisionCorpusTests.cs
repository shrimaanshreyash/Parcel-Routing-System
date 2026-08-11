using ParcelRoutingSystem.Domain.Routing;

namespace ParcelRoutingSystem.Domain.Tests;

/// <summary>
/// Protects a privacy-safe golden corpus of routing weights and values;
/// recipient names and addresses are deliberately excluded.
/// </summary>
public sealed class ReferenceManifestDecisionCorpusTests
{
    /// <summary>
    /// Verifies all 17 reference rows produce the expected department and
    /// insurance state when the operator explicitly supplies the missing
    /// manifest-level country.
    /// </summary>
    /// <param name="sampleId">A synthetic row label containing no personal data.</param>
    /// <param name="weightKilograms">The reference row weight.</param>
    /// <param name="declaredValueEuros">The reference row value.</param>
    /// <param name="expectedDepartment">The expected weight-based department.</param>
    /// <param name="expectedApproval">The expected insurance state.</param>
    [Theory]
    [MemberData(nameof(ReferenceManifestSamples))]
    public void Route_WhenUsingReferenceManifestFacts_MatchesGoldenDecision(
        string sampleId,
        decimal weightKilograms,
        decimal declaredValueEuros,
        RoutingDepartment expectedDepartment,
        ApprovalState expectedApproval)
    {
        RoutingDecision decision = RoutingTestFixture.Route(
            weightKilograms,
            declaredValueEuros,
            "NL",
            sampleId);

        Assert.Equal(expectedDepartment, decision.IntendedDepartment);
        Assert.Equal(expectedApproval, decision.ApprovalState);
    }

    /// <summary>
    /// Returns 17 representative weight/value pairs with synthetic row
    /// labels. Duplicate rows remain duplicated because no trusted parcel
    /// identifier authorizes deduplication.
    /// </summary>
    /// <returns>Privacy-safe golden decision cases for xUnit.</returns>
    public static TheoryData<string, decimal, decimal, RoutingDepartment, ApprovalState>
        ReferenceManifestSamples()
    {
        return new TheoryData<string, decimal, decimal, RoutingDepartment, ApprovalState>
        {
            { "manifest-row-01", 0.02m, 0m, RoutingDepartment.Mail, ApprovalState.NotRequired },
            { "manifest-row-02", 2m, 0m, RoutingDepartment.Regular, ApprovalState.NotRequired },
            { "manifest-row-03", 100m, 2_000m, RoutingDepartment.Heavy, ApprovalState.PendingInsuranceApproval },
            { "manifest-row-04", 11m, 500m, RoutingDepartment.Heavy, ApprovalState.NotRequired },
            { "manifest-row-05", 3m, 0m, RoutingDepartment.Regular, ApprovalState.NotRequired },
            { "manifest-row-06", 10m, 1_500m, RoutingDepartment.Regular, ApprovalState.PendingInsuranceApproval },
            { "manifest-row-07", 10m, 1_500m, RoutingDepartment.Regular, ApprovalState.PendingInsuranceApproval },
            { "manifest-row-08", 0.7m, 0m, RoutingDepartment.Mail, ApprovalState.NotRequired },
            { "manifest-row-09", 0.9m, 1_100m, RoutingDepartment.Mail, ApprovalState.PendingInsuranceApproval },
            { "manifest-row-10", 4.5m, 0m, RoutingDepartment.Regular, ApprovalState.NotRequired },
            { "manifest-row-11", 120m, 1_500m, RoutingDepartment.Heavy, ApprovalState.PendingInsuranceApproval },
            { "manifest-row-12", 130m, 2_000m, RoutingDepartment.Heavy, ApprovalState.PendingInsuranceApproval },
            { "manifest-row-13", 0.3m, 0m, RoutingDepartment.Mail, ApprovalState.NotRequired },
            { "manifest-row-14", 1m, 0m, RoutingDepartment.Mail, ApprovalState.NotRequired },
            { "manifest-row-15", 15m, 100m, RoutingDepartment.Heavy, ApprovalState.NotRequired },
            { "manifest-row-16", 15m, 100m, RoutingDepartment.Heavy, ApprovalState.NotRequired },
            { "manifest-row-17", 0.4m, 0m, RoutingDepartment.Mail, ApprovalState.NotRequired },
        };
    }
}
