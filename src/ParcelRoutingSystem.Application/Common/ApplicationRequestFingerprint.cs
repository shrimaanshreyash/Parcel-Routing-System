using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ParcelRoutingSystem.Application.Batches;
using ParcelRoutingSystem.Application.Rules;
using ParcelRoutingSystem.Domain.Parcels;

namespace ParcelRoutingSystem.Application.Common;

/// <summary>
/// Creates deterministic SHA-256 fingerprints of normalized business input so
/// an idempotency key cannot silently be reused for a different request.
/// </summary>
internal static class ApplicationRequestFingerprint
{
    /// <summary>
    /// Fingerprints every normalized parcel fact, including optional attributes
    /// in stable case-insensitive name order.
    /// </summary>
    /// <param name="parcel">The validated immutable parcel.</param>
    /// <returns>An uppercase 64-character SHA-256 fingerprint.</returns>
    internal static string ForParcel(Parcel parcel)
    {
        ArgumentNullException.ThrowIfNull(parcel);

        var canonical = new StringBuilder();
        Append(canonical, parcel.Weight.Kilograms.ToString("G29", CultureInfo.InvariantCulture));
        Append(canonical, parcel.DeclaredValue.Euros.ToString("G29", CultureInfo.InvariantCulture));
        Append(canonical, parcel.DestinationCountry.Value);
        foreach ((string name, string value) in parcel.AdditionalAttributes
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            Append(canonical, name.ToUpperInvariant());
            Append(canonical, value);
        }

        return Hash(canonical);
    }

    /// <summary>
    /// Fingerprints the optional fallback country and every ordered row country
    /// so the same key cannot represent a different manifest or provenance.
    /// </summary>
    /// <param name="fallbackCountry">The validated optional manifest fallback.</param>
    /// <param name="rows">The ordered privacy-minimized row inputs.</param>
    /// <returns>An uppercase 64-character SHA-256 fingerprint.</returns>
    internal static string ForBatch(
        CountryCode? fallbackCountry,
        IReadOnlyList<BatchParcelRowInput> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var canonical = new StringBuilder();
        Append(canonical, fallbackCountry?.Value ?? "NO-FALLBACK");
        foreach (BatchParcelRowInput row in rows)
        {
            Append(
                canonical,
                row.WeightKilograms.ToString("G29", CultureInfo.InvariantCulture));
            Append(
                canonical,
                row.DeclaredValueEuros.ToString("G29", CultureInfo.InvariantCulture));
            Append(
                canonical,
                string.IsNullOrWhiteSpace(row.DestinationCountry)
                    ? "NO-ROW-COUNTRY"
                    : row.DestinationCountry.Trim().ToUpperInvariant());
            Append(
                canonical,
                row.ValidationErrorCode ?? "ROW-VALID");
            Append(
                canonical,
                row.ValidationErrorMessage ?? "NO-ROW-ERROR");
        }

        return Hash(canonical);
    }

    /// <summary>
    /// Fingerprints a complete constrained rule definition so replaying draft
    /// creation with changed boundaries is rejected explicitly.
    /// </summary>
    /// <param name="definition">The validated immutable rule definition.</param>
    /// <returns>An uppercase 64-character SHA-256 fingerprint.</returns>
    internal static string ForRuleSet(RuleSetDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var canonical = new StringBuilder();
        Append(
            canonical,
            definition.Version.ToString(CultureInfo.InvariantCulture));
        foreach (WeightBandDefinition band in definition.WeightBands
            .OrderBy(item => item.LowerBoundExclusive)
            .ThenBy(item => item.Priority))
        {
            Append(canonical, band.RuleId);
            Append(canonical, band.Priority.ToString(CultureInfo.InvariantCulture));
            Append(
                canonical,
                band.LowerBoundExclusive.ToString("G29", CultureInfo.InvariantCulture));
            Append(
                canonical,
                band.UpperBoundInclusive?.ToString(
                    "G29",
                    CultureInfo.InvariantCulture) ?? "UNBOUNDED");
            Append(canonical, band.Department.ToString());
        }

        Append(canonical, definition.InsuranceRule.RuleId);
        Append(
            canonical,
            definition.InsuranceRule.Priority.ToString(CultureInfo.InvariantCulture));
        Append(
            canonical,
            definition.InsuranceRule.ThresholdExclusiveEuros.ToString(
                "G29",
                CultureInfo.InvariantCulture));

        return Hash(canonical);
    }

    /// <summary>
    /// Appends one length-prefixed value so different field boundaries cannot
    /// produce the same canonical text.
    /// </summary>
    /// <param name="builder">The canonical request buffer.</param>
    /// <param name="value">The normalized field value.</param>
    private static void Append(StringBuilder builder, string value)
    {
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
        builder.Append('|');
    }

    /// <summary>
    /// Hashes canonical UTF-8 text without retaining the original request in the
    /// fingerprint or audit record.
    /// </summary>
    /// <param name="canonical">The normalized length-prefixed input.</param>
    /// <returns>An uppercase SHA-256 hexadecimal value.</returns>
    private static string Hash(StringBuilder canonical)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(canonical.ToString());
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
