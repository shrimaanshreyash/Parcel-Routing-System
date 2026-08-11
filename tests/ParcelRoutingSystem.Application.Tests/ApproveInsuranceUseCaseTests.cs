using ParcelRoutingSystem.Application.Approvals;
using ParcelRoutingSystem.Application.Common;
using ParcelRoutingSystem.Application.Routing;

namespace ParcelRoutingSystem.Application.Tests;

/// <summary>
/// Verifies insurance approval remains an idempotent append-only workflow action
/// tied to an immutable high-value decision.
/// </summary>
public sealed class ApproveInsuranceUseCaseTests
{
    /// <summary>
    /// Verifies approval is persisted once and replay returns the same append-only
    /// approval without duplicating audit evidence.
    /// </summary>
    [Fact]
    public async Task Execute_WhenApprovalRepeats_ReturnsOriginalApproval()
    {
        InMemoryApplicationStore store = ApplicationTestFixture.CreateStore();
        var clock = new MutableClock(ApplicationTestFixture.FixedTime);
        var identifiers = new SequenceIdentifierGenerator();
        RoutingDecisionRecord decision = await CreateDecisionAsync(
            store,
            clock,
            identifiers,
            valueEuros: 1_500m);
        var useCase = new ApproveInsuranceUseCase(store, clock, identifiers);
        var command = new ApproveInsuranceCommand(
            decision.Id,
            "approval-001",
            ApplicationTestFixture.Metadata("approval-correlation"));

        InsuranceApprovalRecord first = await useCase.ExecuteAsync(command);
        InsuranceApprovalRecord second = await useCase.ExecuteAsync(command);

        Assert.Equal(first, second);
        Assert.Equal(decision.Id, first.DecisionId);
        Assert.Equal(2, store.AuditEvents.Count);
    }

    /// <summary>
    /// Verifies an approval cannot be attached to a decision whose value did not
    /// create an insurance hold.
    /// </summary>
    [Fact]
    public async Task Execute_WhenDecisionNeedsNoApproval_ReturnsStableConflict()
    {
        InMemoryApplicationStore store = ApplicationTestFixture.CreateStore();
        var clock = new MutableClock(ApplicationTestFixture.FixedTime);
        var identifiers = new SequenceIdentifierGenerator();
        RoutingDecisionRecord decision = await CreateDecisionAsync(
            store,
            clock,
            identifiers,
            valueEuros: 500m);
        var useCase = new ApproveInsuranceUseCase(store, clock, identifiers);

        ApplicationOperationException exception =
            await Assert.ThrowsAsync<ApplicationOperationException>(
                () => useCase.ExecuteAsync(
                    new ApproveInsuranceCommand(
                        decision.Id,
                        "approval-not-required",
                        ApplicationTestFixture.Metadata())));

        Assert.Equal(
            ApplicationErrorCodes.InsuranceApprovalNotRequired,
            exception.Code);
        Assert.Single(store.AuditEvents);
    }

    /// <summary>
    /// Routes one fixture parcel so approval tests exercise the same application
    /// path used by production orchestration.
    /// </summary>
    /// <param name="store">The shared in-memory transactional test store.</param>
    /// <param name="clock">The deterministic application clock.</param>
    /// <param name="identifiers">The deterministic identifier generator.</param>
    /// <param name="valueEuros">The declared value controlling approval state.</param>
    /// <returns>The newly persisted immutable routing decision.</returns>
    private static async Task<RoutingDecisionRecord> CreateDecisionAsync(
        InMemoryApplicationStore store,
        MutableClock clock,
        SequenceIdentifierGenerator identifiers,
        decimal valueEuros)
    {
        var route = new RouteParcelUseCase(store, store, clock, identifiers);
        RouteParcelResult result = await route.ExecuteAsync(
            new RouteParcelCommand(
                $"route-{valueEuros}",
                2m,
                valueEuros,
                "GB",
                AdditionalAttributes: null,
                ApplicationTestFixture.Metadata()));

        return result.Decision;
    }
}
