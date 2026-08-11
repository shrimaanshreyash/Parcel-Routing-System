using ParcelRoutingSystem.Application.Operations;

namespace ParcelRoutingSystem.Application.Tests;

/// <summary>
/// Verifies operational history choices remain bounded and are translated into
/// deterministic UTC persistence queries before HTTP or database concerns.
/// </summary>
public sealed class OperationsQueryUseCaseTests
{
    /// <summary>
    /// Verifies the default overview remains the newest ten decisions while
    /// still asking persistence for all-time and current-day counters.
    /// </summary>
    [Fact]
    public async Task GetOverview_WhenRecent_UsesNewestTenWithoutDateBoundary()
    {
        var repository = new CapturingOperationsRepository();
        var clock = new MutableClock(ApplicationTestFixture.FixedTime);
        var useCase = new OperationsQueryUseCase(repository, clock);

        await useCase.GetOverviewAsync();

        Assert.Equal(OperationsTimeRange.Recent, repository.OverviewRange);
        Assert.Null(repository.OverviewRangeStartUtc);
        Assert.True(repository.OverviewRecentOnly);
        Assert.Equal(1, repository.OverviewPage);
        Assert.Equal(10, repository.OverviewPageSize);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero),
            repository.UtcDayStart);
    }

    /// <summary>
    /// Verifies each selectable history preset becomes the expected inclusive
    /// UTC boundary and paged reads never exceed fifteen records.
    /// </summary>
    [Theory]
    [InlineData(OperationsTimeRange.Last24Hours, -1)]
    [InlineData(OperationsTimeRange.Last7Days, -7)]
    [InlineData(OperationsTimeRange.Last30Days, -30)]
    public async Task GetOverview_WhenRelativeRange_UsesBoundedFifteenItemPage(
        OperationsTimeRange range,
        int expectedDays)
    {
        var repository = new CapturingOperationsRepository();
        var clock = new MutableClock(ApplicationTestFixture.FixedTime);
        var useCase = new OperationsQueryUseCase(repository, clock);

        await useCase.GetOverviewAsync(range, page: 3);

        Assert.Equal(range, repository.OverviewRange);
        Assert.Equal(
            clock.UtcNow.AddDays(expectedDays),
            repository.OverviewRangeStartUtc);
        Assert.False(repository.OverviewRecentOnly);
        Assert.Equal(3, repository.OverviewPage);
        Assert.Equal(15, repository.OverviewPageSize);
    }

    /// <summary>
    /// Verifies the twelve-month preset follows calendar arithmetic rather than
    /// assuming every year has the same number of days.
    /// </summary>
    [Fact]
    public async Task GetActivity_WhenTwelveMonths_UsesCalendarYearBoundary()
    {
        var repository = new CapturingOperationsRepository();
        var clock = new MutableClock(ApplicationTestFixture.FixedTime);
        var useCase = new OperationsQueryUseCase(repository, clock);

        await useCase.GetActivityAsync(
            OperationsTimeRange.Last12Months,
            page: 2);

        Assert.Equal(clock.UtcNow.AddYears(-1), repository.ActivityRangeStartUtc);
        Assert.False(repository.ActivityRecentOnly);
        Assert.Equal(2, repository.ActivityPage);
        Assert.Equal(15, repository.ActivityPageSize);
    }

    /// <summary>
    /// Verifies activity categories remain typed and reach persistence before
    /// paging so the browser never filters one incomplete page locally.
    /// </summary>
    [Fact]
    public async Task GetActivity_WhenImportsSelected_ForwardsServerCategory()
    {
        var repository = new CapturingOperationsRepository();
        var clock = new MutableClock(ApplicationTestFixture.FixedTime);
        var useCase = new OperationsQueryUseCase(repository, clock);

        await useCase.GetActivityAsync(
            OperationsTimeRange.AllTime,
            page: 1,
            category: ActivityCategory.Imports);

        Assert.Equal(ActivityCategory.Imports, repository.ActivityCategory);
    }

    /// <summary>
    /// Verifies decision filters remain typed, paged, and separate from the
    /// current operational KPI counters.
    /// </summary>
    [Fact]
    public async Task GetOverview_WhenAwaitingSelected_ForwardsDecisionFilter()
    {
        var repository = new CapturingOperationsRepository();
        var clock = new MutableClock(ApplicationTestFixture.FixedTime);
        var useCase = new OperationsQueryUseCase(repository, clock);

        await useCase.GetOverviewAsync(
            OperationsTimeRange.AllTime,
            page: 2,
            filter: RoutingDecisionFilter.AwaitingApproval);

        Assert.Equal(
            RoutingDecisionFilter.AwaitingApproval,
            repository.OverviewFilter);
        Assert.Equal(2, repository.OverviewPage);
    }

    /// <summary>
    /// Verifies the issue drill-down uses the same UTC-day boundary as its KPI
    /// and normalizes invalid pages to the agreed fifteen-row read.
    /// </summary>
    [Fact]
    public async Task GetImportAttention_WhenIssuesRequested_UsesTodayAndBoundedPage()
    {
        var repository = new CapturingOperationsRepository();
        var clock = new MutableClock(ApplicationTestFixture.FixedTime);
        var useCase = new OperationsQueryUseCase(repository, clock);

        await useCase.GetImportAttentionAsync(
            ImportAttentionKind.Issues,
            page: 0);

        Assert.Equal(ImportAttentionKind.Issues, repository.AttentionKind);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero),
            repository.AttentionUtcDayStart);
        Assert.Equal(1, repository.AttentionPage);
        Assert.Equal(15, repository.AttentionPageSize);
    }

    /// <summary>
    /// Verifies unresolved approvals always use the agreed fifteen-item page and
    /// invalid page numbers are safely normalized.
    /// </summary>
    [Fact]
    public async Task GetAwaitingInsurance_WhenPageIsInvalid_UsesFirstPage()
    {
        var repository = new CapturingOperationsRepository();
        var clock = new MutableClock(ApplicationTestFixture.FixedTime);
        var useCase = new OperationsQueryUseCase(repository, clock);

        await useCase.GetAwaitingInsuranceAsync(page: 0);

        Assert.Equal(1, repository.InsurancePage);
        Assert.Equal(15, repository.InsurancePageSize);
    }
}

/// <summary>
/// Captures operational query arguments so application tests can verify range
/// and paging policy without depending on EF Core or PostgreSQL.
/// </summary>
internal sealed class CapturingOperationsRepository : IOperationsReadRepository
{
    internal DateTimeOffset UtcDayStart { get; private set; }
    internal OperationsTimeRange OverviewRange { get; private set; }
    internal RoutingDecisionFilter OverviewFilter { get; private set; }
    internal DateTimeOffset? OverviewRangeStartUtc { get; private set; }
    internal bool OverviewRecentOnly { get; private set; }
    internal int OverviewPage { get; private set; }
    internal int OverviewPageSize { get; private set; }
    internal DateTimeOffset? ActivityRangeStartUtc { get; private set; }
    internal ActivityCategory ActivityCategory { get; private set; }
    internal bool ActivityRecentOnly { get; private set; }
    internal int ActivityPage { get; private set; }
    internal int ActivityPageSize { get; private set; }
    internal int InsurancePage { get; private set; }
    internal int InsurancePageSize { get; private set; }
    internal ImportAttentionKind AttentionKind { get; private set; }
    internal DateTimeOffset AttentionUtcDayStart { get; private set; }
    internal int AttentionPage { get; private set; }
    internal int AttentionPageSize { get; private set; }

    /// <inheritdoc />
    public Task<OperationsOverview> GetOverviewAsync(
        DateTimeOffset utcDayStart,
        OperationsTimeRange range,
        RoutingDecisionFilter filter,
        DateTimeOffset? rangeStartUtc,
        bool recentOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        UtcDayStart = utcDayStart;
        OverviewRange = range;
        OverviewFilter = filter;
        OverviewRangeStartUtc = rangeStartUtc;
        OverviewRecentOnly = recentOnly;
        OverviewPage = page;
        OverviewPageSize = pageSize;
        return Task.FromResult(
            new OperationsOverview(
                0,
                0,
                0,
                0,
                0,
                range,
                filter,
                new PagedResults<RoutingDecisionSummary>(
                    [],
                    page,
                    pageSize,
                    0)));
    }

    /// <inheritdoc />
    public Task<PagedResults<ActivityRecord>> GetActivityAsync(
        DateTimeOffset? rangeStartUtc,
        ActivityCategory category,
        bool recentOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ActivityRangeStartUtc = rangeStartUtc;
        ActivityCategory = category;
        ActivityRecentOnly = recentOnly;
        ActivityPage = page;
        ActivityPageSize = pageSize;
        return Task.FromResult(
            new PagedResults<ActivityRecord>([], page, pageSize, 0));
    }

    /// <inheritdoc />
    public Task<PagedResults<ImportAttentionItem>> GetImportAttentionAsync(
        ImportAttentionKind kind,
        DateTimeOffset utcDayStart,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        AttentionKind = kind;
        AttentionUtcDayStart = utcDayStart;
        AttentionPage = page;
        AttentionPageSize = pageSize;
        return Task.FromResult(
            new PagedResults<ImportAttentionItem>([], page, pageSize, 0));
    }

    /// <inheritdoc />
    public Task<BatchDetails?> GetBatchDetailsAsync(
        Guid batchId,
        CancellationToken cancellationToken) =>
        Task.FromResult<BatchDetails?>(null);

    /// <inheritdoc />
    public Task<IReadOnlyList<BatchSummary>> GetRecentBatchesAsync(
        int limit,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BatchSummary>>([]);

    /// <inheritdoc />
    public Task<RoutingDecisionDetails?> GetDecisionAsync(
        Guid decisionId,
        CancellationToken cancellationToken) =>
        Task.FromResult<RoutingDecisionDetails?>(null);

    /// <inheritdoc />
    public Task<PagedResults<RoutingDecisionSummary>> GetAwaitingInsuranceAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        InsurancePage = page;
        InsurancePageSize = pageSize;
        return Task.FromResult(
            new PagedResults<RoutingDecisionSummary>([], page, pageSize, 0));
    }
}
