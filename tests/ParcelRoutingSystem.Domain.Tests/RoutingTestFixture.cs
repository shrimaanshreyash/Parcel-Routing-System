using ParcelRoutingSystem.Domain.Parcels;
using ParcelRoutingSystem.Domain.Routing;

namespace ParcelRoutingSystem.Domain.Tests;

/// <summary>
/// Creates privacy-safe parcels and deterministic decision metadata shared by
/// domain tests, keeping business assertions focused on the rule under test.
/// </summary>
internal static class RoutingTestFixture
{
    /// <summary>
    /// Provides a fixed UTC decision time so repeated evaluations can be
    /// compared without introducing a system-clock dependency.
    /// </summary>
    internal static DateTimeOffset DecisionTime { get; } =
        new(2026, 7, 28, 9, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// Builds a valid parcel from primitive test values. The helper uses no
    /// recipient data from the supplied manifest and defaults the explicitly
    /// missing manifest country to the operator-supplied Netherlands code.
    /// </summary>
    /// <param name="weightKilograms">The positive parcel weight in kilograms.</param>
    /// <param name="declaredValueEuros">The non-negative declared value in euros.</param>
    /// <param name="countryCode">The explicit ISO 3166-1 alpha-2 destination.</param>
    /// <param name="attributes">Optional non-routing attributes to preserve.</param>
    /// <returns>A validated parcel ready for deterministic routing.</returns>
    internal static Parcel CreateParcel(
        decimal weightKilograms,
        decimal declaredValueEuros,
        string countryCode = "NL",
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        return Parcel.Create(
            Weight.FromKilograms(weightKilograms),
            DeclaredValue.FromEuros(declaredValueEuros),
            CountryCode.FromAlpha2(countryCode),
            attributes);
    }

    /// <summary>
    /// Routes one privacy-safe parcel through the immutable default rule set.
    /// It accepts an optional correlation identifier so determinism and
    /// traceability can be tested independently.
    /// </summary>
    /// <param name="weightKilograms">The positive parcel weight in kilograms.</param>
    /// <param name="declaredValueEuros">The non-negative declared value in euros.</param>
    /// <param name="countryCode">The explicit ISO destination country.</param>
    /// <param name="correlationId">The caller-owned trace identifier.</param>
    /// <returns>The explainable routing decision produced by the domain.</returns>
    internal static RoutingDecision Route(
        decimal weightKilograms,
        decimal declaredValueEuros,
        string countryCode = "NL",
        string correlationId = "phase-1-domain-test")
    {
        Parcel parcel = CreateParcel(weightKilograms, declaredValueEuros, countryCode);
        RoutingDecisionContext context =
            RoutingDecisionContext.Create(DecisionTime, correlationId);

        return RoutingRuleSet.CreateDefault().Route(parcel, context);
    }
}
