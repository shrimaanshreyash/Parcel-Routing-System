using ParcelRoutingSystem.Application.Common;
using ParcelRoutingSystem.Application.Routing;
using ParcelRoutingSystem.Domain.Routing;

namespace ParcelRoutingSystem.Application.Tests;

/// <summary>
/// Verifies route-one-parcel orchestration uses the active policy, persists one
/// immutable audited decision, and honors idempotent replay.
/// </summary>
public sealed class RouteParcelUseCaseTests
{
    /// <summary>
    /// Verifies a high-value heavy parcel stores both its intended department
    /// and independent approval hold with versioned explanations.
    /// </summary>
    [Fact]
    public async Task Execute_WhenParcelIsHeavyAndHighValue_PersistsExplainableDecision()
    {
        InMemoryApplicationStore store = ApplicationTestFixture.CreateStore();
        var useCase = new RouteParcelUseCase(
            store,
            store,
            new MutableClock(ApplicationTestFixture.FixedTime),
            new SequenceIdentifierGenerator());
        var command = new RouteParcelCommand(
            "route-001",
            12m,
            1_500m,
            "GB",
            AdditionalAttributes: null,
            ApplicationTestFixture.Metadata());

        RouteParcelResult result = await useCase.ExecuteAsync(command);

        Assert.False(result.WasReplay);
        Assert.Equal(RoutingDepartment.Heavy, result.Decision.IntendedDepartment);
        Assert.Equal(
            ApprovalState.PendingInsuranceApproval,
            result.Decision.ApprovalState);
        Assert.Equal(1, result.Decision.RuleSetVersion);
        Assert.Equal(2, result.Decision.MatchedRuleIds.Count);
        Assert.Single(store.Decisions);
        Assert.Single(store.AuditEvents);
    }

    /// <summary>
    /// Verifies repeated requests return the original immutable decision and do
    /// not duplicate either the decision or its audit event.
    /// </summary>
    [Fact]
    public async Task Execute_WhenIdempotencyKeyRepeats_ReturnsOriginalDecision()
    {
        InMemoryApplicationStore store = ApplicationTestFixture.CreateStore();
        var useCase = new RouteParcelUseCase(
            store,
            store,
            new MutableClock(ApplicationTestFixture.FixedTime),
            new SequenceIdentifierGenerator());
        var command = new RouteParcelCommand(
            "route-repeat",
            2m,
            10m,
            "NL",
            AdditionalAttributes: null,
            ApplicationTestFixture.Metadata());

        RouteParcelResult first = await useCase.ExecuteAsync(command);
        RouteParcelResult second = await useCase.ExecuteAsync(command);

        Assert.False(first.WasReplay);
        Assert.True(second.WasReplay);
        Assert.Equal(first.Decision, second.Decision);
        Assert.Single(store.Decisions);
        Assert.Single(store.AuditEvents);
    }

    /// <summary>
    /// Verifies the same idempotency key cannot silently return an old decision
    /// for different normalized parcel facts.
    /// </summary>
    [Fact]
    public async Task Execute_WhenKeyIsReusedForDifferentParcel_RejectsConflict()
    {
        InMemoryApplicationStore store = ApplicationTestFixture.CreateStore();
        var useCase = new RouteParcelUseCase(
            store,
            store,
            new MutableClock(ApplicationTestFixture.FixedTime),
            new SequenceIdentifierGenerator());
        await useCase.ExecuteAsync(
            new RouteParcelCommand(
                "route-conflict",
                2m,
                10m,
                "GB",
                AdditionalAttributes: null,
                ApplicationTestFixture.Metadata()));

        ApplicationOperationException exception =
            await Assert.ThrowsAsync<ApplicationOperationException>(
                () => useCase.ExecuteAsync(
                    new RouteParcelCommand(
                        "route-conflict",
                        12m,
                        10m,
                        "GB",
                        AdditionalAttributes: null,
                        ApplicationTestFixture.Metadata())));

        Assert.Equal(ApplicationErrorCodes.IdempotencyConflict, exception.Code);
        Assert.Single(store.Decisions);
        Assert.Single(store.AuditEvents);
    }

    /// <summary>
    /// Verifies routing fails closed when persistence has no active rule set.
    /// </summary>
    [Fact]
    public async Task Execute_WhenNoRuleSetIsActive_FailsClosed()
    {
        var store = new InMemoryApplicationStore();
        var useCase = new RouteParcelUseCase(
            store,
            store,
            new MutableClock(ApplicationTestFixture.FixedTime),
            new SequenceIdentifierGenerator());
        var command = new RouteParcelCommand(
            "route-no-policy",
            2m,
            10m,
            "NL",
            AdditionalAttributes: null,
            ApplicationTestFixture.Metadata());

        ApplicationOperationException exception =
            await Assert.ThrowsAsync<ApplicationOperationException>(
                () => useCase.ExecuteAsync(command));

        Assert.Equal(ApplicationErrorCodes.ActiveRuleSetUnavailable, exception.Code);
        Assert.Empty(store.Decisions);
        Assert.Empty(store.AuditEvents);
    }
}
