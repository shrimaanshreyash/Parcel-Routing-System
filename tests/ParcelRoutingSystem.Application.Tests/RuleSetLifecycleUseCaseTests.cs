using ParcelRoutingSystem.Application.Rules;
using ParcelRoutingSystem.Domain.Routing;

namespace ParcelRoutingSystem.Application.Tests;

/// <summary>
/// Verifies rule versions remain validated and immutable through draft,
/// simulation, activation, and rollback orchestration.
/// </summary>
public sealed class RuleSetLifecycleUseCaseTests
{
    /// <summary>
    /// Verifies a valid draft can be simulated, activated, and rolled back while
    /// each state change retains an explicit audit event.
    /// </summary>
    [Fact]
    public async Task Lifecycle_WhenDraftIsValid_SimulatesActivatesAndRollsBack()
    {
        InMemoryApplicationStore store = ApplicationTestFixture.CreateStore();
        var useCase = new RuleSetLifecycleUseCase(
            store,
            new MutableClock(ApplicationTestFixture.FixedTime),
            new SequenceIdentifierGenerator());
        RuleSetDefinition definition = CreateVersionTwo();

        RuleSetWriteResult draft = await useCase.CreateDraftAsync(
            definition,
            "rules-draft-002",
            ApplicationTestFixture.Metadata());
        IReadOnlyList<RuleDecisionDifference> differences =
            await useCase.SimulateAsync(
                2,
                [new RuleSimulationParcel("sample-1", 1.5m, 0m, "GB")],
                "rules-simulation");
        RuleSetActivationResult activated = await useCase.ActivateAsync(
            2,
            "rules-activate-002",
            ApplicationTestFixture.Metadata());
        RuleSetActivationResult rolledBack = await useCase.RollbackAsync(
            1,
            "rules-rollback-001",
            ApplicationTestFixture.Metadata());

        Assert.True(draft.WasCreated);
        Assert.Single(differences);
        Assert.Equal(
            RoutingDepartment.Regular,
            differences[0].CurrentDepartment);
        Assert.Equal(RoutingDepartment.Mail, differences[0].ProposedDepartment);
        Assert.Equal(2, activated.ActiveRuleSet.Definition.Version);
        Assert.Equal(1, rolledBack.ActiveRuleSet.Definition.Version);
        Assert.Equal(3, store.AuditEvents.Count);
    }

    /// <summary>
    /// Verifies a draft containing a weight gap is rejected by the pure domain
    /// before persistence or audit state is written.
    /// </summary>
    [Fact]
    public async Task CreateDraft_WhenRulesContainGap_RejectsBeforePersistence()
    {
        InMemoryApplicationStore store = ApplicationTestFixture.CreateStore();
        var useCase = new RuleSetLifecycleUseCase(
            store,
            new MutableClock(ApplicationTestFixture.FixedTime),
            new SequenceIdentifierGenerator());
        var invalid = new RuleSetDefinition(
            2,
            [
                new WeightBandDefinition(
                    "MAIL",
                    100,
                    0m,
                    1m,
                    RoutingDepartment.Mail),
                new WeightBandDefinition(
                    "HEAVY",
                    200,
                    2m,
                    null,
                    RoutingDepartment.Heavy),
            ],
            new InsuranceRuleDefinition("INSURANCE", 1_000, 1_000m));

        await Assert.ThrowsAsync<RuleSetValidationException>(
            () => useCase.CreateDraftAsync(
                invalid,
                "rules-invalid",
                ApplicationTestFixture.Metadata()));

        Assert.Empty(store.AuditEvents);
        Assert.Null(await store.GetVersionAsync(2, CancellationToken.None));
    }

    /// <summary>
    /// Creates a valid version whose Mail boundary changes to two kilograms so
    /// simulation has one deliberate, explainable difference.
    /// </summary>
    /// <returns>A constrained semantically valid version-two definition.</returns>
    private static RuleSetDefinition CreateVersionTwo()
    {
        return new RuleSetDefinition(
            2,
            [
                new WeightBandDefinition(
                    "MAIL-UP-TO-2-KG",
                    100,
                    0m,
                    2m,
                    RoutingDepartment.Mail),
                new WeightBandDefinition(
                    "REGULAR-UP-TO-10-KG",
                    200,
                    2m,
                    10m,
                    RoutingDepartment.Regular),
                new WeightBandDefinition(
                    "HEAVY-OVER-10-KG",
                    300,
                    10m,
                    null,
                    RoutingDepartment.Heavy),
            ],
            new InsuranceRuleDefinition(
                "INSURANCE-OVER-1000-EUR",
                1_000,
                1_000m));
    }
}
