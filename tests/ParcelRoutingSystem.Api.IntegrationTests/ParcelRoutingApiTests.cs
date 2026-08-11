using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ParcelRoutingSystem.Api.IntegrationTests;

/// <summary>
/// Verifies real HTTP behavior against PostgreSQL, including security
/// policies and asynchronous XML processing.
/// </summary>
[Collection(ApiIntegrationCollection.Name)]
public sealed class ParcelRoutingApiTests
{
    private readonly ApiIntegrationFixture _fixture;

    /// <summary>
    /// Creates the test class around the shared real API/database fixture.
    /// </summary>
    /// <param name="fixture">The migrated API fixture.</param>
    public ParcelRoutingApiTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Proves liveness and PostgreSQL readiness through the actual middleware.
    /// </summary>
    [Fact]
    public async Task Health_WhenHostAndDatabaseAreReady_ReturnsHealthy()
    {
        using HttpClient client = CreateClient(_fixture.Factory);

        using HttpResponseMessage live = await client.GetAsync("/health/live");
        using HttpResponseMessage ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    /// <summary>
    /// Proves a protected endpoint returns 401 when the local identity is
    /// deliberately disabled.
    /// </summary>
    [Fact]
    public async Task Rules_WhenNoIdentityExists_ReturnsUnauthorized()
    {
        using WebApplicationFactory<Program> anonymousFactory =
            _fixture.Factory.WithWebHostBuilder(
                builder => builder.UseSetting(
                    "ParcelAuthentication:DevelopmentAutoAuthenticate",
                    "false"));
        using HttpClient client = CreateClient(anonymousFactory);

        using HttpResponseMessage response = await client.GetAsync(
            "/api/rules/active");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Proves one parcel is routed and persisted by the server and receives the
    /// expected hardening headers.
    /// </summary>
    [Fact]
    public async Task Route_WhenParcelIsValid_ReturnsExplainableDecision()
    {
        using HttpClient client = CreateClient(_fixture.Factory);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/parcels/route")
        {
            Content = JsonContent.Create(
                new
                {
                    weightKilograms = 1.01m,
                    declaredValueEuros = 1_000.01m,
                    destinationCountry = "GB",
                    operatorReference = "API-TEST",
                }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using HttpResponseMessage response = await client.SendAsync(request);
        using JsonDocument body = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync());
        JsonElement decision = body.RootElement.GetProperty("decision");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "Regular",
            decision.GetProperty("intendedDepartment").GetString());
        Assert.Equal(
            "PendingInsuranceApproval",
            decision.GetProperty("approvalState").GetString());
        Assert.NotEmpty(decision.GetProperty("matchedRuleIds").EnumerateArray());
        Assert.Equal(
            "nosniff",
            response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(
            "DENY",
            response.Headers.GetValues("X-Frame-Options").Single());
    }

    /// <summary>
    /// Proves an Operator without the InsuranceApprover role receives 403 from
    /// the server policy rather than a UI-only restriction.
    /// </summary>
    [Fact]
    public async Task Approval_WhenIdentityLacksApproverRole_ReturnsForbidden()
    {
        Guid decisionId = await CreateHighValueDecisionAsync();
        using WebApplicationFactory<Program> operatorFactory =
            _fixture.Factory.WithWebHostBuilder(
                builder =>
                {
                    builder.UseSetting(
                        "ParcelAuthentication:DevelopmentRoles:0",
                        "Operator");
                    builder.UseSetting(
                        "ParcelAuthentication:DevelopmentRoles:1",
                        "Operator");
                    builder.UseSetting(
                        "ParcelAuthentication:DevelopmentRoles:2",
                        "Operator");
                });
        using HttpClient client = CreateClient(operatorFactory);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/approvals/{decisionId:D}/approve");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Proves the privacy-safe reference corpus is accepted with an explicit
    /// fallback country and all seventeen rows reach terminal durable states.
    /// </summary>
    [Fact]
    public async Task Import_WhenReferenceCorpusIsValid_CompletesAllRows()
    {
        using HttpClient client = CreateClient(_fixture.Factory);
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "XmlFixtures",
            "09-reference-corpus.xml");
        await using FileStream stream = File.OpenRead(path);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/batches/import-xml?fallbackCountry=GB")
        {
            Content = new StreamContent(stream),
        };
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/xml");
        request.Headers.Add("X-Manifest-Name", "09-reference-corpus.xml");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using HttpResponseMessage accepted = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using JsonDocument acceptedBody = JsonDocument.Parse(
            await accepted.Content.ReadAsStreamAsync());
        Guid batchId = acceptedBody.RootElement.GetProperty("id").GetGuid();

        JsonElement completed = await PollUntilTerminalAsync(client, batchId);

        Assert.Equal(17, completed.GetProperty("totalRows").GetInt32());
        Assert.Equal(17, completed.GetProperty("completedRows").GetInt32());
        Assert.Equal(0, completed.GetProperty("failedRows").GetInt32());
        Assert.All(
            completed.GetProperty("rows").EnumerateArray(),
            row => Assert.Equal(
                "ManifestFallback",
                row.GetProperty("countrySource").GetString()));
    }

    /// <summary>
    /// Proves the privacy-safe boundary and legacy-variation fixtures both
    /// become complete durable batches through the real HTTP and worker path.
    /// </summary>
    [Theory]
    [InlineData("01-valid-boundaries.xml", null, 5)]
    [InlineData("02-valid-variations.xml", "GB", 3)]
    public async Task Import_WhenPrivacySafeValidFixtureIsUsed_EvaluatesEveryRow(
        string fixtureName,
        string? fallbackCountry,
        int expectedRows)
    {
        using HttpClient client = CreateClient(_fixture.Factory);
        using HttpResponseMessage accepted = await SendFixtureAsync(
            client,
            fixtureName,
            fallbackCountry);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Guid batchId = (await accepted.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        JsonElement completed = await PollUntilTerminalAsync(client, batchId);

        Assert.Equal(expectedRows, completed.GetProperty("totalRows").GetInt32());
        Assert.Equal(expectedRows, completed.GetProperty("completedRows").GetInt32());
        Assert.Equal(0, completed.GetProperty("failedRows").GetInt32());
    }

    /// <summary>
    /// Proves a correctly spelled Recipient element follows the same real HTTP,
    /// privacy-discard, persistence, and worker path as the legacy form.
    /// </summary>
    [Fact]
    public async Task Import_WhenRecipientUsesCorrectSpelling_EvaluatesRow()
    {
        using HttpClient client = CreateClient(_fixture.Factory);
        const string xml =
            "<Container><parcels><Parcel>"
            + "<Recipient><Name>Synthetic Test Recipient</Name>"
            + "<Address>Synthetic Test Address</Address></Recipient>"
            + "<Weight>3.25</Weight><Value>42.5</Value><Country>GB</Country>"
            + "</Parcel></parcels></Container>";
        using HttpResponseMessage accepted = await SendXmlAsync(
            client,
            xml,
            Guid.NewGuid().ToString("N"),
            confirmDuplicate: false);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Guid batchId = (await accepted.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        JsonElement completed = await PollUntilTerminalAsync(client, batchId);

        Assert.Equal(1, completed.GetProperty("totalRows").GetInt32());
        Assert.Equal(1, completed.GetProperty("completedRows").GetInt32());
        Assert.Equal(0, completed.GetProperty("failedRows").GetInt32());
        JsonElement row = completed.GetProperty("rows").EnumerateArray().Single();
        Assert.Equal("GB", row.GetProperty("destinationCountry").GetString());
        Assert.False(row.TryGetProperty("recipient", out _));
        Assert.False(row.TryGetProperty("address", out _));
    }

    /// <summary>
    /// Proves parser-shape and domain-value failures remain isolated while valid
    /// sibling parcels in the same accepted document are still evaluated.
    /// </summary>
    [Fact]
    public async Task Import_WhenFixtureContainsMixedRowErrors_PersistsPartialSuccess()
    {
        using HttpClient client = CreateClient(_fixture.Factory);
        using HttpResponseMessage accepted = await SendFixtureAsync(
            client,
            "03-mixed-row-errors.xml",
            fallbackCountry: null);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Guid batchId = (await accepted.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        JsonElement completed = await PollUntilTerminalAsync(client, batchId);

        Assert.Equal("CompletedWithErrors", completed.GetProperty("status").GetString());
        Assert.Equal(6, completed.GetProperty("totalRows").GetInt32());
        Assert.Equal(2, completed.GetProperty("completedRows").GetInt32());
        Assert.Equal(4, completed.GetProperty("failedRows").GetInt32());
        Assert.Equal(
            4,
            completed.GetProperty("rows").EnumerateArray().Count(
                row => row.GetProperty("status").GetString() == "ValidationFailed"));
    }

    /// <summary>
    /// Proves an unsupported row ISO code is one durable failure rather than a
    /// reason to discard the two valid sibling parcels.
    /// </summary>
    [Fact]
    public async Task Import_WhenFixtureContainsInvalidCountry_IsolatesCountryRow()
    {
        using HttpClient client = CreateClient(_fixture.Factory);
        using HttpResponseMessage accepted = await SendFixtureAsync(
            client,
            "04-invalid-country.xml",
            fallbackCountry: null);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Guid batchId = (await accepted.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        JsonElement completed = await PollUntilTerminalAsync(client, batchId);

        Assert.Equal(3, completed.GetProperty("totalRows").GetInt32());
        Assert.Equal(2, completed.GetProperty("completedRows").GetInt32());
        Assert.Equal(1, completed.GetProperty("failedRows").GetInt32());
        JsonElement failedRow = completed.GetProperty("rows")
            .EnumerateArray()
            .Single(row => row.GetProperty("status").GetString() == "ValidationFailed");
        Assert.Equal(
            "parcel.country.invalid",
            failedRow.GetProperty("errorCode").GetString());
    }

    /// <summary>
    /// Proves malformed, unsupported-root, and DTD/XXE fixtures are rejected as
    /// document failures before any durable batch resource is returned.
    /// </summary>
    [Theory]
    [InlineData("05-malformed.xml")]
    [InlineData("06-unsupported-structure.xml")]
    [InlineData("07-xxe.xml")]
    public async Task Import_WhenFixtureHasDocumentFailure_ReturnsSafeBadRequest(
        string fixtureName)
    {
        using HttpClient client = CreateClient(_fixture.Factory);
        using HttpResponseMessage response = await SendFixtureAsync(
            client,
            fixtureName,
            fallbackCountry: "GB");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "routing.manifest.invalid",
            problem.GetProperty("code").GetString());
        Assert.False(problem.TryGetProperty("exception", out _));
        Assert.False(problem.TryGetProperty("stackTrace", out _));
    }

    /// <summary>
    /// Proves DTD-bearing XML is rejected by the hardened parser without batch
    /// persistence or entity expansion.
    /// </summary>
    [Fact]
    public async Task Import_WhenDocumentTypeIsPresent_ReturnsBadRequest()
    {
        using HttpClient client = CreateClient(_fixture.Factory);
        const string xml =
            "<!DOCTYPE Container [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]>"
            + "<Container><parcels><Parcel><Weight>1</Weight>"
            + "<Value>2</Value><Country>GB</Country></Parcel></parcels></Container>";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/batches/import-xml")
        {
            Content = new StringContent(xml, Encoding.UTF8, "application/xml"),
        };
        request.Headers.Add("X-Manifest-Name", "unsafe.xml");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Proves one operation key replays transparently, a new operation receives
    /// a duplicate warning, and explicit confirmation creates another batch.
    /// </summary>
    [Fact]
    public async Task Import_WhenManifestRepeats_RequiresExplicitConfirmation()
    {
        using HttpClient client = CreateClient(_fixture.Factory);
        string key = Guid.NewGuid().ToString("N");
        string xml = await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "XmlFixtures",
            "08-duplicate-retry.xml"));

        using HttpResponseMessage first = await SendXmlAsync(
            client,
            xml,
            key,
            confirmDuplicate: false);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Guid firstBatchId = (await first.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        using HttpResponseMessage replay = await SendXmlAsync(
            client,
            xml,
            key,
            confirmDuplicate: false);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(
            firstBatchId,
            (await replay.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("id")
                .GetGuid());

        using HttpResponseMessage warning = await SendXmlAsync(
            client,
            xml,
            Guid.NewGuid().ToString("N"),
            confirmDuplicate: false);
        Assert.Equal(HttpStatusCode.Conflict, warning.StatusCode);
        JsonElement problem = await warning.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "routing.batch.duplicate_manifest",
            problem.GetProperty("code").GetString());
        Assert.Equal(
            firstBatchId,
            problem.GetProperty("previousBatchId").GetGuid());

        using HttpResponseMessage confirmed = await SendXmlAsync(
            client,
            xml,
            Guid.NewGuid().ToString("N"),
            confirmDuplicate: true);
        Assert.Equal(HttpStatusCode.Accepted, confirmed.StatusCode);
        Assert.NotEqual(
            firstBatchId,
            (await confirmed.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("id")
                .GetGuid());
    }

    /// <summary>
    /// Proves pending approval reads, authorized append-only release, idempotent
    /// repeat behavior, and joined decision evidence through real HTTP.
    /// </summary>
    [Fact]
    public async Task Approval_WhenAuthorized_LeavesDurableEvidenceAndQueueRefreshes()
    {
        Guid decisionId = await CreateHighValueDecisionAsync();
        using HttpClient client = CreateClient(_fixture.Factory);

        JsonElement firstQueuePage = await client.GetFromJsonAsync<JsonElement>(
            "/api/operations/insurance/awaiting?page=1");
        int initialQueueSize = firstQueuePage.GetProperty("totalItems").GetInt32();
        int lastQueuePage = firstQueuePage.GetProperty("totalPages").GetInt32();
        JsonElement queue = lastQueuePage == 1
            ? firstQueuePage
            : await client.GetFromJsonAsync<JsonElement>(
                $"/api/operations/insurance/awaiting?page={lastQueuePage}");
        Assert.Contains(
            queue.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetGuid() == decisionId);

        string key = Guid.NewGuid().ToString("N");
        using HttpResponseMessage first = await SendApprovalAsync(
            client,
            decisionId,
            key);
        using HttpResponseMessage replay = await SendApprovalAsync(
            client,
            decisionId,
            key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);

        JsonElement detail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/operations/decisions/{decisionId:D}");
        Assert.True(detail.GetProperty("decision").GetProperty(
            "isInsuranceApproved").GetBoolean());
        Assert.Equal(
            "api-reviewer",
            detail.GetProperty("approval").GetProperty("approvedBy").GetString());
        JsonElement refreshed = await client.GetFromJsonAsync<JsonElement>(
            "/api/operations/insurance/awaiting?page=1");
        Assert.Equal(
            initialQueueSize - 1,
            refreshed.GetProperty("totalItems").GetInt32());
    }

    /// <summary>
    /// Proves history presets and page metadata are server-owned while the
    /// overview still exposes all-time totals separately from the selected page.
    /// </summary>
    [Fact]
    public async Task Overview_WhenAllTimeRange_ReturnsBoundedPageMetadata()
    {
        using HttpClient client = CreateClient(_fixture.Factory);

        JsonElement overview = await client.GetFromJsonAsync<JsonElement>(
            "/api/operations/overview?range=AllTime&page=1");
        JsonElement history = overview.GetProperty("decisionHistory");

        Assert.Equal("AllTime", overview.GetProperty("decisionRange").GetString());
        Assert.True(overview.GetProperty("totalDecisions").GetInt32() >= 0);
        Assert.Equal(15, history.GetProperty("pageSize").GetInt32());
        Assert.True(history.GetProperty("items").GetArrayLength() <= 15);
        Assert.True(history.GetProperty("totalItems").GetInt32()
            <= overview.GetProperty("totalDecisions").GetInt32());
    }

    /// <summary>
    /// Proves a work queue larger than one page remains complete and navigable
    /// without increasing the fixed fifteen-item server bound.
    /// </summary>
    [Fact]
    public async Task InsuranceQueue_WhenMoreThanFifteenAwait_PaginatesEveryHold()
    {
        for (int index = 0; index < 16; index++)
        {
            await CreateHighValueDecisionAsync();
        }

        using HttpClient client = CreateClient(_fixture.Factory);
        JsonElement firstPage = await client.GetFromJsonAsync<JsonElement>(
            "/api/operations/insurance/awaiting?page=1");
        JsonElement secondPage = await client.GetFromJsonAsync<JsonElement>(
            "/api/operations/insurance/awaiting?page=2");

        Assert.Equal(15, firstPage.GetProperty("items").GetArrayLength());
        Assert.True(firstPage.GetProperty("totalItems").GetInt32() >= 16);
        Assert.True(firstPage.GetProperty("totalPages").GetInt32() >= 2);
        Assert.True(secondPage.GetProperty("items").GetArrayLength() > 0);
        Assert.Equal(2, secondPage.GetProperty("page").GetInt32());
    }

    /// <summary>
    /// Proves audit history uses the same bounded page contract and supports
    /// explicit all-time investigation without returning an unlimited array.
    /// </summary>
    [Fact]
    public async Task Activity_WhenAllTimeRange_ReturnsBoundedPageMetadata()
    {
        using HttpClient client = CreateClient(_fixture.Factory);

        JsonElement activity = await client.GetFromJsonAsync<JsonElement>(
            "/api/operations/activity?range=AllTime&page=1");

        Assert.Equal(15, activity.GetProperty("pageSize").GetInt32());
        Assert.True(activity.GetProperty("items").GetArrayLength() <= 15);
        Assert.True(activity.GetProperty("totalItems").GetInt32()
            >= activity.GetProperty("items").GetArrayLength());
    }

    /// <summary>
    /// Proves the Overview department filter executes before paging and never
    /// mixes another department into the selected operator result.
    /// </summary>
    [Fact]
    public async Task Overview_WhenHeavyFilterSelected_ReturnsOnlyHeavyDecisions()
    {
        using HttpClient client = CreateClient(_fixture.Factory);
        using var route = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/parcels/route")
        {
            Content = JsonContent.Create(
                new
                {
                    weightKilograms = 20m,
                    declaredValueEuros = 100m,
                    destinationCountry = "GB",
                }),
        };
        route.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using HttpResponseMessage routed = await client.SendAsync(route);
        routed.EnsureSuccessStatusCode();

        JsonElement overview = await client.GetFromJsonAsync<JsonElement>(
            "/api/operations/overview?range=AllTime&page=1&filter=Heavy");
        JsonElement history = overview.GetProperty("decisionHistory");

        Assert.Equal("Heavy", overview.GetProperty("decisionFilter").GetString());
        Assert.NotEmpty(history.GetProperty("items").EnumerateArray());
        Assert.All(
            history.GetProperty("items").EnumerateArray(),
            item => Assert.Equal(
                "Heavy",
                item.GetProperty("intendedDepartment").GetString()));
    }

    /// <summary>
    /// Proves activity category filtering happens on the server before paging so
    /// an Imports page contains only batch lifecycle events.
    /// </summary>
    [Fact]
    public async Task Activity_WhenImportsCategorySelected_ReturnsOnlyBatchEvents()
    {
        const string xml = """
            <Container><parcels>
              <Parcel><Weight>0.333</Weight><Value>33</Value><Country>SE</Country></Parcel>
            </parcels></Container>
            """;
        using HttpClient client = CreateClient(_fixture.Factory);
        using HttpResponseMessage accepted = await SendXmlAsync(
            client,
            xml,
            Guid.NewGuid().ToString("N"),
            confirmDuplicate: false);
        accepted.EnsureSuccessStatusCode();

        JsonElement activity = await client.GetFromJsonAsync<JsonElement>(
            "/api/operations/activity?range=AllTime&page=1&category=Imports");

        Assert.NotEmpty(activity.GetProperty("items").EnumerateArray());
        Assert.All(
            activity.GetProperty("items").EnumerateArray(),
            item => Assert.StartsWith(
                "batch.",
                item.GetProperty("eventType").GetString(),
                StringComparison.Ordinal));
    }

    /// <summary>
    /// Proves the issue KPI has a concrete privacy-safe drill-down containing
    /// stable row identities and safe messages for the exact failed batch.
    /// </summary>
    [Fact]
    public async Task ImportAttention_WhenMixedRowsFail_ReturnsExactSafeIssueRows()
    {
        const string xml = """
            <Container><parcels>
              <Parcel><Weight>0.75</Weight><Value>75</Value><Country>NL</Country></Parcel>
              <Parcel><Value>76</Value><Country>GB</Country></Parcel>
              <Parcel><Weight>three</Weight><Value>77</Value><Country>DE</Country></Parcel>
            </parcels></Container>
            """;
        using HttpClient client = CreateClient(_fixture.Factory);
        using HttpResponseMessage accepted = await SendXmlAsync(
            client,
            xml,
            Guid.NewGuid().ToString("N"),
            confirmDuplicate: false);
        accepted.EnsureSuccessStatusCode();
        Guid batchId = (await accepted.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        JsonElement completed = await PollUntilTerminalAsync(client, batchId);
        Assert.Equal(2, completed.GetProperty("failedRows").GetInt32());

        JsonElement attention = await client.GetFromJsonAsync<JsonElement>(
            "/api/operations/import-attention?kind=Issues&page=1");
        JsonElement[] matching = attention
            .GetProperty("items")
            .EnumerateArray()
            .Where(item => item.GetProperty("batchId").GetGuid() == batchId)
            .ToArray();

        Assert.Equal(2, matching.Length);
        Assert.All(
            matching,
            item =>
            {
                Assert.True(item.GetProperty("rowNumber").GetInt32() > 0);
                Assert.False(string.IsNullOrWhiteSpace(
                    item.GetProperty("errorCode").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(
                    item.GetProperty("errorMessage").GetString()));
                Assert.DoesNotContain(
                    "recipient",
                    item.GetProperty("errorMessage").GetString()!,
                    StringComparison.OrdinalIgnoreCase);
            });
    }

    /// <summary>
    /// Proves rule writes are server-role-gated even when the interface would
    /// normally hide administration controls.
    /// </summary>
    [Fact]
    public async Task RuleDraft_WhenIdentityLacksAdministratorRole_ReturnsForbidden()
    {
        using WebApplicationFactory<Program> operatorFactory =
            _fixture.Factory.WithWebHostBuilder(
                builder =>
                {
                    builder.UseSetting(
                        "ParcelAuthentication:DevelopmentRoles:0",
                        "Operator");
                    builder.UseSetting(
                        "ParcelAuthentication:DevelopmentRoles:1",
                        "Operator");
                    builder.UseSetting(
                        "ParcelAuthentication:DevelopmentRoles:2",
                        "Operator");
                });
        using HttpClient client = CreateClient(operatorFactory);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/rules/drafts")
        {
            Content = JsonContent.Create(
                new
                {
                    version = 88,
                    mailUpperKilograms = 1m,
                    regularUpperKilograms = 10m,
                    insuranceThresholdEuros = 1_000m,
                }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Proves the complete constrained lifecycle through HTTP: validated draft,
    /// decision-difference simulation, atomic activation, and audited rollback.
    /// </summary>
    [Fact]
    public async Task Rules_WhenAdministratorCompletesLifecycle_ActivatesAndRollsBack()
    {
        using HttpClient client = CreateClient(_fixture.Factory);
        JsonElement original = await client.GetFromJsonAsync<JsonElement>(
            "/api/rules/active");
        int originalVersion = original.GetProperty("version").GetInt32();
        JsonElement history = await client.GetFromJsonAsync<JsonElement>(
            "/api/rules?limit=50");
        int candidateVersion = history
            .EnumerateArray()
            .Max(item => item.GetProperty("version").GetInt32()) + 1;
        using var draftRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/rules/drafts")
        {
            Content = JsonContent.Create(
                new
                {
                    version = candidateVersion,
                    mailUpperKilograms = 1.5m,
                    regularUpperKilograms = 11m,
                    insuranceThresholdEuros = 1_200m,
                }),
        };
        draftRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using HttpResponseMessage draft = await client.SendAsync(draftRequest);
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        JsonElement draftBody =
            await draft.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1.5m, draftBody.GetProperty("mailUpperKilograms").GetDecimal());
        Assert.Equal(11m, draftBody.GetProperty("regularUpperKilograms").GetDecimal());
        Assert.Equal(
            1_200m,
            draftBody.GetProperty("insuranceThresholdEuros").GetDecimal());

        JsonElement refreshedHistory = await client.GetFromJsonAsync<JsonElement>(
            "/api/rules?limit=50");
        JsonElement restoredDraft = refreshedHistory
            .EnumerateArray()
            .Single(item => item.GetProperty("version").GetInt32() == candidateVersion);
        Assert.Equal("Draft", restoredDraft.GetProperty("status").GetString());
        Assert.Equal(
            1.5m,
            restoredDraft.GetProperty("mailUpperKilograms").GetDecimal());

        using HttpResponseMessage simulation = await client.PostAsJsonAsync(
            $"/api/rules/{candidateVersion}/simulate",
            new
            {
                samples = new[]
                {
                    new
                    {
                        sampleId = "changed-mail-band",
                        weightKilograms = 1.2m,
                        declaredValueEuros = 100m,
                        destinationCountry = "GB",
                    },
                    new
                    {
                        sampleId = "unchanged-heavy",
                        weightKilograms = 15m,
                        declaredValueEuros = 100m,
                        destinationCountry = "GB",
                    },
                },
            });
        simulation.EnsureSuccessStatusCode();
        JsonElement simulationBody =
            await simulation.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, simulationBody.GetProperty("changedCount").GetInt32());

        try
        {
            using HttpResponseMessage activated = await SendRuleActionAsync(
                client,
                candidateVersion,
                "activate");
            activated.EnsureSuccessStatusCode();
            Assert.Equal(
                candidateVersion,
                (await activated.Content.ReadFromJsonAsync<JsonElement>())
                    .GetProperty("version")
                    .GetInt32());
        }
        finally
        {
            using HttpResponseMessage rolledBack = await SendRuleActionAsync(
                client,
                originalVersion,
                "rollback");
            rolledBack.EnsureSuccessStatusCode();
            Assert.Equal(
                originalVersion,
                (await rolledBack.Content.ReadFromJsonAsync<JsonElement>())
                    .GetProperty("version")
                    .GetInt32());
        }
    }

    /// <summary>
    /// Sends one safe in-memory XML manifest with explicit replay and duplicate
    /// confirmation metadata.
    /// </summary>
    private static Task<HttpResponseMessage> SendXmlAsync(
        HttpClient client,
        string xml,
        string idempotencyKey,
        bool confirmDuplicate)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/batches/import-xml?confirmDuplicate={confirmDuplicate.ToString().ToLowerInvariant()}")
        {
            Content = new StringContent(xml, Encoding.UTF8, "application/xml"),
        };
        request.Headers.Add("X-Manifest-Name", "api-test.xml");
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return client.SendAsync(request);
    }

    /// <summary>
    /// Sends one copied privacy-safe fixture through the real raw-stream import
    /// endpoint with a unique operation key.
    /// </summary>
    /// <param name="client">The authenticated API test client.</param>
    /// <param name="fixtureName">The controlled XML fixture filename.</param>
    /// <param name="fallbackCountry">The optional manifest fallback country.</param>
    /// <returns>The unconsumed import response.</returns>
    private static async Task<HttpResponseMessage> SendFixtureAsync(
        HttpClient client,
        string fixtureName,
        string? fallbackCountry)
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "XmlFixtures",
            fixtureName);
        byte[] content = await File.ReadAllBytesAsync(path);
        string query = fallbackCountry is null
            ? string.Empty
            : $"?fallbackCountry={Uri.EscapeDataString(fallbackCountry)}";
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/batches/import-xml{query}")
        {
            Content = new ByteArrayContent(content),
        };
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/xml");
        request.Headers.Add("X-Manifest-Name", fixtureName);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    /// <summary>
    /// Sends one approval request with a caller-controlled replay key.
    /// </summary>
    private static Task<HttpResponseMessage> SendApprovalAsync(
        HttpClient client,
        Guid decisionId,
        string idempotencyKey)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/approvals/{decisionId:D}/approve");
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return client.SendAsync(request);
    }

    /// <summary>
    /// Sends one idempotent rule activation or rollback request.
    /// </summary>
    private static Task<HttpResponseMessage> SendRuleActionAsync(
        HttpClient client,
        int version,
        string action)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/rules/{version}/{action}");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return client.SendAsync(request);
    }

    /// <summary>
    /// Routes one pending high-value parcel and returns its server identifier for
    /// subsequent authorization testing.
    /// </summary>
    private async Task<Guid> CreateHighValueDecisionAsync()
    {
        using HttpClient client = CreateClient(_fixture.Factory);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/parcels/route")
        {
            Content = JsonContent.Create(
                new
                {
                    weightKilograms = 2m,
                    declaredValueEuros = 1_500m,
                    destinationCountry = "GB",
                }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using JsonDocument body = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("decision").GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Polls a durable batch for at most fifteen seconds and returns its terminal
    /// JSON object without relying on an arbitrary one-shot sleep.
    /// </summary>
    private static async Task<JsonElement> PollUntilTerminalAsync(
        HttpClient client,
        Guid batchId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!timeout.IsCancellationRequested)
        {
            using HttpResponseMessage response = await client.GetAsync(
                $"/api/batches/{batchId:D}",
                timeout.Token);
            response.EnsureSuccessStatusCode();
            using JsonDocument body = JsonDocument.Parse(
                await response.Content.ReadAsStreamAsync(timeout.Token));
            JsonElement root = body.RootElement;
            string? status = root.GetProperty("status").GetString();
            if (status is "Completed" or "CompletedWithErrors")
            {
                return root.Clone();
            }

            await Task.Delay(100, timeout.Token);
        }

        throw new TimeoutException("The durable batch did not reach a terminal state.");
    }

    /// <summary>
    /// Creates a non-redirecting client so authorization status codes remain
    /// directly observable.
    /// </summary>
    private static HttpClient CreateClient(
        WebApplicationFactory<Program> factory)
    {
        return factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });
    }
}
