using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using ParcelRoutingSystem.Application.Batches;
using ParcelRoutingSystem.Application.Common;
using ParcelRoutingSystem.Application.Imports;

namespace ParcelRoutingSystem.Infrastructure.Xml;

/// <summary>
/// Streams the supported legacy Container XML shape with DTDs and external
/// resolution disabled and discards recipient personal data at the boundary.
/// </summary>
public sealed class LegacyXmlParcelManifestParser : IParcelManifestParser
{
    private static readonly HashSet<string> AllowedParcelElements =
        new(StringComparer.Ordinal)
        {
            "Receipient",
            "Recipient",
            "Weight",
            "Value",
            "Country",
            "DestinationCountry",
        };

    private readonly LegacyXmlManifestLimits _limits;

    /// <summary>
    /// Creates the legacy parser with validated resource and duration limits.
    /// </summary>
    /// <param name="limits">The hard limits applied before durable batch creation.</param>
    public LegacyXmlParcelManifestParser(LegacyXmlManifestLimits limits)
    {
        _limits = limits;
    }

    /// <summary>
    /// Reads one parcel element at a time, retains only routing facts, and
    /// rejects malformed, oversized, timed-out, DTD-bearing, or unsupported XML.
    /// </summary>
    /// <param name="stream">The readable untrusted XML stream.</param>
    /// <param name="cancellationToken">Cancels parsing when the request ends.</param>
    /// <returns>The ordered privacy-minimized manifest rows.</returns>
    public async Task<ParsedParcelManifest> ParseAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw InvalidManifest("The manifest stream cannot be read.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(_limits.Timeout);
        XmlReaderSettings settings = CreateSecureSettings();

        try
        {
            using XmlReader reader = XmlReader.Create(stream, settings);
            XmlNodeType content = await reader.MoveToContentAsync();
            if (content != XmlNodeType.Element
                || !string.Equals(reader.LocalName, "Container", StringComparison.Ordinal)
                || !string.IsNullOrEmpty(reader.NamespaceURI))
            {
                throw InvalidManifest(
                    "The manifest root must be an unqualified Container element.");
            }

            var rows = new List<BatchParcelRowInput>();
            bool parcelsElementFound = false;

            while (await reader.ReadAsync())
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (reader.Depth == 1
                    && string.Equals(reader.LocalName, "parcels", StringComparison.Ordinal))
                {
                    parcelsElementFound = true;
                    continue;
                }

                if (reader.Depth == 2
                    && string.Equals(reader.LocalName, "Parcel", StringComparison.Ordinal))
                {
                    if (!parcelsElementFound)
                    {
                        throw InvalidManifest(
                            "Parcel elements must be contained inside Container/parcels.");
                    }

                    rows.Add(await ParseParcelAsync(reader, timeout.Token));
                    if (rows.Count > _limits.MaximumRows)
                    {
                        throw LimitExceeded(
                            $"The manifest exceeds the {_limits.MaximumRows} row limit.");
                    }
                }
            }

            if (!parcelsElementFound)
            {
                throw InvalidManifest(
                    "The manifest must contain Container/parcels.");
            }

            if (rows.Count == 0)
            {
                throw InvalidManifest("The manifest contains no Parcel rows.");
            }

            return new ParsedParcelManifest(rows.AsReadOnly());
        }
        catch (ManifestImportException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw LimitExceeded("The manifest exceeded the parsing time limit.");
        }
        catch (XmlException exception) when (IsCharacterLimitException(exception))
        {
            throw LimitExceeded(
                $"The manifest exceeds the {_limits.MaximumCharacters} character limit.");
        }
        catch (XmlException)
        {
            throw InvalidManifest("The manifest is not well-formed supported XML.");
        }
    }

    /// <summary>
    /// Recognizes the framework's character-quota exception without returning
    /// its implementation detail or any uploaded content to the caller.
    /// </summary>
    /// <param name="exception">The safe in-process XML reader failure.</param>
    /// <returns>True only for the configured document-character quota.</returns>
    private static bool IsCharacterLimitException(XmlException exception)
    {
        return exception.Message.Contains(
            "MaxCharactersInDocument",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates hardened reader settings that prohibit DTDs, never resolve
    /// external resources, and cap total document characters.
    /// </summary>
    /// <returns>Settings suitable for an untrusted asynchronous XML stream.</returns>
    private XmlReaderSettings CreateSecureSettings()
    {
        return new XmlReaderSettings
        {
            Async = true,
            CloseInput = false,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = _limits.MaximumCharacters,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };
    }

    /// <summary>
    /// Loads only the current bounded Parcel subtree and converts its supported
    /// direct child values into invariant routing facts.
    /// </summary>
    /// <param name="reader">The outer reader positioned on a Parcel start element.</param>
    /// <param name="cancellationToken">Cancels subtree parsing.</param>
    /// <returns>One privacy-minimized ordered batch input.</returns>
    private static async Task<BatchParcelRowInput> ParseParcelAsync(
        XmlReader reader,
        CancellationToken cancellationToken)
    {
        using XmlReader subtree = reader.ReadSubtree();
        XElement parcel = await XElement.LoadAsync(
            subtree,
            LoadOptions.None,
            cancellationToken);
        if (!string.IsNullOrEmpty(parcel.Name.NamespaceName))
        {
            return InvalidRow(
                "XML namespaces are not supported inside a Parcel row.");
        }

        try
        {
            XElement[] children = parcel.Elements().ToArray();
            bool hasUnsupportedElement = children
                .Select(element => element.Name.LocalName)
                .Any(name => !AllowedParcelElements.Contains(name));
            if (hasUnsupportedElement)
            {
                throw InvalidManifest("Parcel contains an unsupported element.");
            }

            decimal weight = ParseRequiredDecimal(parcel, "Weight");
            decimal value = ParseRequiredDecimal(parcel, "Value");
            string? country = ReadOptionalCountry(parcel);

            return new BatchParcelRowInput(weight, value, country);
        }
        catch (ManifestImportException exception)
        {
            return InvalidRow(exception.Message);
        }
    }

    /// <summary>
    /// Converts one structurally isolated Parcel failure into a privacy-safe row
    /// result so valid sibling rows can still become durable work.
    /// </summary>
    /// <param name="message">The fixed operator-facing row explanation.</param>
    /// <returns>A failed parsed row without recipient or raw XML content.</returns>
    private static BatchParcelRowInput InvalidRow(string message)
    {
        return new BatchParcelRowInput(
            WeightKilograms: 0m,
            DeclaredValueEuros: 0m,
            DestinationCountry: null,
            ApplicationErrorCodes.ManifestRowInvalid,
            message);
    }

    /// <summary>
    /// Reads exactly one required decimal child using invariant syntax and
    /// rejects duplicates or non-numeric values without echoing uploaded text.
    /// </summary>
    /// <param name="parcel">The current bounded parcel element.</param>
    /// <param name="elementName">The allow-listed direct child name.</param>
    /// <returns>The parsed decimal routing fact.</returns>
    private static decimal ParseRequiredDecimal(
        XElement parcel,
        string elementName)
    {
        XElement[] elements = parcel.Elements(elementName).ToArray();
        if (elements.Length != 1
            || !decimal.TryParse(
                elements[0].Value.Trim(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal value))
        {
            throw InvalidManifest(
                $"Each Parcel must contain one valid {elementName} value.");
        }

        return value;
    }

    /// <summary>
    /// Reads one optional direct row country from either supported legacy name
    /// and rejects ambiguous duplicates before fallback metadata is considered.
    /// </summary>
    /// <param name="parcel">The current bounded parcel element.</param>
    /// <returns>The trimmed row country or null when omitted.</returns>
    private static string? ReadOptionalCountry(XElement parcel)
    {
        XElement[] countries = parcel.Elements()
            .Where(
                element => element.Name.LocalName is "Country" or "DestinationCountry")
            .ToArray();
        if (countries.Length > 1)
        {
            throw InvalidManifest(
                "A Parcel can contain at most one country element.");
        }

        if (countries.Length == 0)
        {
            return null;
        }

        string value = countries[0].Value.Trim();
        return value.Length == 0
            ? throw InvalidManifest("A row country cannot be empty.")
            : value;
    }

    /// <summary>
    /// Creates the stable safe failure for malformed or unsupported structure.
    /// </summary>
    /// <param name="message">The non-personal operator explanation.</param>
    /// <returns>A coded invalid-manifest exception.</returns>
    private static ManifestImportException InvalidManifest(string message)
    {
        return new ManifestImportException(
            ApplicationErrorCodes.ManifestInvalid,
            message);
    }

    /// <summary>
    /// Creates the stable safe failure for row, character, or duration limits.
    /// </summary>
    /// <param name="message">The non-personal limit explanation.</param>
    /// <returns>A coded limit exception.</returns>
    private static ManifestImportException LimitExceeded(string message)
    {
        return new ManifestImportException(
            ApplicationErrorCodes.ManifestLimitExceeded,
            message);
    }
}
