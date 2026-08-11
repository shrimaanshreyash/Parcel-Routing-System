using System.Text;
using ParcelRoutingSystem.Application.Batches;
using ParcelRoutingSystem.Application.Common;
using ParcelRoutingSystem.Application.Imports;
using ParcelRoutingSystem.Infrastructure.Xml;

namespace ParcelRoutingSystem.Infrastructure.IntegrationTests;

/// <summary>
/// Verifies the untrusted legacy XML boundary retains only routing facts and
/// rejects unsupported or resource-risking documents.
/// </summary>
public sealed class LegacyXmlParcelManifestParserTests
{
    /// <summary>
    /// Proves the privacy-safe reference corpus yields seventeen ordered rows
    /// while recipient fields never enter the application import contract.
    /// </summary>
    [Fact]
    public async Task Parse_WhenReferenceCorpusIsUsed_ReturnsPrivacyMinimizedRows()
    {
        var parser = CreateParser();
        string path = FixturePath("09-reference-corpus.xml");
        await using FileStream stream = File.OpenRead(path);

        ParsedParcelManifest result = await parser.ParseAsync(
            stream,
            CancellationToken.None);

        Assert.Equal(17, result.Rows.Count);
        Assert.All(
            result.Rows,
            row => Assert.Null(row.DestinationCountry));
        Assert.Equal(
            [
                "WeightKilograms",
                "DeclaredValueEuros",
                "DestinationCountry",
                "ValidationErrorCode",
                "ValidationErrorMessage",
            ],
            typeof(BatchParcelRowInput)
                .GetProperties()
                .Select(property => property.Name)
                .ToArray());
    }

    /// <summary>
    /// Proves the privacy-safe boundary fixture retains the exact values needed
    /// to test rules immediately below, at, and above their thresholds.
    /// </summary>
    [Fact]
    public async Task Parse_WhenBoundaryFixtureIsUsed_RetainsExactRoutingFacts()
    {
        var parser = CreateParser();
        await using FileStream stream = File.OpenRead(FixturePath(
            "01-valid-boundaries.xml"));

        ParsedParcelManifest result = await parser.ParseAsync(
            stream,
            CancellationToken.None);

        Assert.Equal(5, result.Rows.Count);
        Assert.Equal(
            [0.999m, 1m, 1.001m, 10m, 10.001m],
            result.Rows.Select(row => row.WeightKilograms).ToArray());
        Assert.Equal(
            [999.99m, 1_000m, 1_000.01m, 100m, 1_500m],
            result.Rows.Select(row => row.DeclaredValueEuros).ToArray());
        Assert.All(result.Rows, row => Assert.Null(row.ValidationErrorCode));
    }

    /// <summary>
    /// Proves harmless element ordering, either supported country field, and
    /// discarded recipient content do not change the minimized import contract.
    /// </summary>
    [Fact]
    public async Task Parse_WhenValidVariationsAreUsed_AcceptsSupportedLegacyShapes()
    {
        var parser = CreateParser();
        await using FileStream stream = File.OpenRead(FixturePath(
            "02-valid-variations.xml"));

        ParsedParcelManifest result = await parser.ParseAsync(
            stream,
            CancellationToken.None);

        Assert.Equal(3, result.Rows.Count);
        Assert.Null(result.Rows[0].DestinationCountry);
        Assert.Equal("SE", result.Rows[1].DestinationCountry);
        Assert.Equal("IE", result.Rows[2].DestinationCountry);
        Assert.All(result.Rows, row => Assert.Null(row.ValidationErrorCode));
    }

    /// <summary>
    /// Proves both the legacy spelling and the correct recipient
    /// spelling are accepted only as privacy-discarded XML aliases.
    /// </summary>
    /// <param name="elementName">The allow-listed legacy or corrected element name.</param>
    [Theory]
    [InlineData("Receipient")]
    [InlineData("Recipient")]
    public async Task Parse_WhenRecipientUsesEitherSupportedSpelling_DiscardsIt(
        string elementName)
    {
        var parser = CreateParser();
        await using Stream stream = ToStream(
            $"<Container><parcels><Parcel><{elementName}>"
            + "<Name>Synthetic Test Recipient</Name>"
            + "<Address>Synthetic Test Address</Address>"
            + $"</{elementName}><Weight>3.25</Weight><Value>42.5</Value>"
            + "<Country>GB</Country></Parcel></parcels></Container>");

        ParsedParcelManifest result = await parser.ParseAsync(
            stream,
            CancellationToken.None);

        BatchParcelRowInput row = Assert.Single(result.Rows);
        Assert.Equal(3.25m, row.WeightKilograms);
        Assert.Equal(42.5m, row.DeclaredValueEuros);
        Assert.Equal("GB", row.DestinationCountry);
        Assert.Null(row.ValidationErrorCode);
        Assert.DoesNotContain(
            typeof(BatchParcelRowInput).GetProperties(),
            property => property.Name.Contains(
                "Recipient",
                StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains(
                    "Address",
                    StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Proves malformed parcel subtrees become isolated safe row results while
    /// valid siblings remain available for durable processing.
    /// </summary>
    [Fact]
    public async Task Parse_WhenRowsContainStructuralErrors_IsolatesOnlyThoseRows()
    {
        var parser = CreateParser();
        await using FileStream stream = File.OpenRead(FixturePath(
            "03-mixed-row-errors.xml"));

        ParsedParcelManifest result = await parser.ParseAsync(
            stream,
            CancellationToken.None);

        Assert.Equal(6, result.Rows.Count);
        Assert.Equal(
            3,
            result.Rows.Count(
                row => row.ValidationErrorCode
                    == ApplicationErrorCodes.ManifestRowInvalid));
        Assert.Equal(0.5m, result.Rows[0].WeightKilograms);
        Assert.Equal(5m, result.Rows[5].WeightKilograms);
    }

    /// <summary>
    /// Proves a row-level country is retained for provenance and later fallback
    /// resolution.
    /// </summary>
    [Fact]
    public async Task Parse_WhenRowCountryExists_RetainsCountry()
    {
        var parser = CreateParser();
        await using Stream stream = ToStream(
            "<Container><parcels><Parcel><Weight>2</Weight>"
            + "<Value>10</Value><Country>NL</Country></Parcel>"
            + "</parcels></Container>");

        ParsedParcelManifest result = await parser.ParseAsync(
            stream,
            CancellationToken.None);

        Assert.Equal("NL", Assert.Single(result.Rows).DestinationCountry);
    }

    /// <summary>
    /// Proves document type declarations are rejected before any external entity
    /// can be expanded.
    /// </summary>
    [Fact]
    public async Task Parse_WhenDocumentTypeExists_RejectsManifest()
    {
        var parser = CreateParser();
        await using Stream stream = ToStream(
            "<!DOCTYPE Container [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]>"
            + "<Container><parcels><Parcel><Weight>1</Weight>"
            + "<Value>2</Value></Parcel></parcels></Container>");

        ManifestImportException exception =
            await Assert.ThrowsAsync<ManifestImportException>(
                () => parser.ParseAsync(stream, CancellationToken.None));

        Assert.Equal(ApplicationErrorCodes.ManifestInvalid, exception.Code);
    }

    /// <summary>
    /// Proves malformed XML becomes a stable safe import error rather than a raw
    /// parser exception or partial batch.
    /// </summary>
    [Fact]
    public async Task Parse_WhenXmlIsMalformed_ReturnsStableError()
    {
        var parser = CreateParser();
        await using Stream stream = ToStream(
            "<Container><parcels><Parcel><Weight>1</Weight>");

        ManifestImportException exception =
            await Assert.ThrowsAsync<ManifestImportException>(
                () => parser.ParseAsync(stream, CancellationToken.None));

        Assert.Equal(ApplicationErrorCodes.ManifestInvalid, exception.Code);
    }

    /// <summary>
    /// Proves an unsupported document root is rejected as a document-level
    /// failure rather than being reinterpreted as an empty or partial batch.
    /// </summary>
    [Fact]
    public async Task Parse_WhenDocumentStructureIsUnsupported_RejectsManifest()
    {
        var parser = CreateParser();
        await using FileStream stream = File.OpenRead(FixturePath(
            "06-unsupported-structure.xml"));

        ManifestImportException exception =
            await Assert.ThrowsAsync<ManifestImportException>(
                () => parser.ParseAsync(stream, CancellationToken.None));

        Assert.Equal(ApplicationErrorCodes.ManifestInvalid, exception.Code);
    }

    /// <summary>
    /// Proves parser row limits stop oversized manifests before durable work is
    /// created.
    /// </summary>
    [Fact]
    public async Task Parse_WhenRowLimitIsExceeded_RejectsManifest()
    {
        var parser = new LegacyXmlParcelManifestParser(
            new LegacyXmlManifestLimits(
                maximumRows: 1,
                maximumCharacters: 10_000,
                timeout: TimeSpan.FromSeconds(5)));
        await using Stream stream = ToStream(
            "<Container><parcels>"
            + "<Parcel><Weight>1</Weight><Value>2</Value></Parcel>"
            + "<Parcel><Weight>2</Weight><Value>3</Value></Parcel>"
            + "</parcels></Container>");

        ManifestImportException exception =
            await Assert.ThrowsAsync<ManifestImportException>(
                () => parser.ParseAsync(stream, CancellationToken.None));

        Assert.Equal(ApplicationErrorCodes.ManifestLimitExceeded, exception.Code);
    }

    /// <summary>
    /// Proves the configured production row ceiling is inclusive so exactly ten
    /// thousand valid parcels remain supported.
    /// </summary>
    [Fact]
    public async Task Parse_WhenRowCountEqualsProductionLimit_AcceptsManifest()
    {
        var parser = new LegacyXmlParcelManifestParser(
            new LegacyXmlManifestLimits(
                maximumRows: 10_000,
                maximumCharacters: 2_000_000,
                timeout: TimeSpan.FromSeconds(10)));
        const string row = "<Parcel><Weight>1</Weight><Value>2</Value></Parcel>";
        await using Stream stream = ToStream(
            "<Container><parcels>"
            + string.Concat(Enumerable.Repeat(row, 10_000))
            + "</parcels></Container>");

        ParsedParcelManifest result = await parser.ParseAsync(
            stream,
            CancellationToken.None);

        Assert.Equal(10_000, result.Rows.Count);
    }

    /// <summary>
    /// Proves the independent character ceiling rejects expansion-prone or
    /// oversized content even when the HTTP boundary is not involved.
    /// </summary>
    [Fact]
    public async Task Parse_WhenCharacterLimitIsExceeded_RejectsManifest()
    {
        var parser = new LegacyXmlParcelManifestParser(
            new LegacyXmlManifestLimits(
                maximumRows: 100,
                maximumCharacters: 100,
                timeout: TimeSpan.FromSeconds(5)));
        await using Stream stream = ToStream(
            "<Container><parcels><Parcel><Weight>1</Weight><Value>2</Value>"
            + "<Country>GB</Country></Parcel></parcels></Container>");

        ManifestImportException exception =
            await Assert.ThrowsAsync<ManifestImportException>(
                () => parser.ParseAsync(stream, CancellationToken.None));

        Assert.Equal(ApplicationErrorCodes.ManifestLimitExceeded, exception.Code);
    }

    /// <summary>
    /// Creates the production parser with small but sufficient test limits.
    /// </summary>
    private static LegacyXmlParcelManifestParser CreateParser()
    {
        return new LegacyXmlParcelManifestParser(
            new LegacyXmlManifestLimits(
                maximumRows: 100,
                maximumCharacters: 100_000,
                timeout: TimeSpan.FromSeconds(5)));
    }

    /// <summary>
    /// Resolves one copied privacy-safe XML fixture from the test output.
    /// </summary>
    /// <param name="fileName">The controlled fixture filename.</param>
    /// <returns>The absolute test-data path.</returns>
    private static string FixturePath(string fileName)
    {
        return Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "XmlFixtures",
            fileName);
    }

    /// <summary>
    /// Converts controlled test XML into a readable in-memory stream.
    /// </summary>
    private static MemoryStream ToStream(string xml)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(xml));
    }
}
