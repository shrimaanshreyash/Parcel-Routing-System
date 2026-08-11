using System.Collections.ObjectModel;

namespace ParcelRoutingSystem.Domain.Parcels;

/// <summary>
/// Represents the validated business facts needed to route one parcel. It
/// deliberately excludes recipient names and addresses because the default
/// routing policy does not require personal data.
/// </summary>
public sealed class Parcel
{
    private Parcel(
        Weight weight,
        DeclaredValue declaredValue,
        CountryCode destinationCountry,
        IReadOnlyDictionary<string, string> additionalAttributes)
    {
        Weight = weight;
        DeclaredValue = declaredValue;
        DestinationCountry = destinationCountry;
        AdditionalAttributes = additionalAttributes;
    }

    /// <summary>Gets the validated positive parcel weight.</summary>
    public Weight Weight { get; }

    /// <summary>Gets the validated non-negative declared value.</summary>
    public DeclaredValue DeclaredValue { get; }

    /// <summary>Gets the explicitly supplied destination country.</summary>
    public CountryCode DestinationCountry { get; }

    /// <summary>
    /// Gets a defensive read-only copy of optional business attributes. The
    /// default rule set preserves but does not evaluate these values.
    /// </summary>
    public IReadOnlyDictionary<string, string> AdditionalAttributes { get; }

    /// <summary>
    /// Creates an immutable parcel from already validated core value objects.
    /// </summary>
    /// <param name="weight">The positive parcel weight.</param>
    /// <param name="declaredValue">The non-negative EUR value.</param>
    /// <param name="destinationCountry">The explicit ISO destination country.</param>
    /// <param name="additionalAttributes">
    /// Optional named facts preserved for future allow-listed rules.
    /// </param>
    /// <returns>A parcel safe to evaluate repeatedly.</returns>
    /// <exception cref="DomainValidationException">
    /// Thrown when an optional attribute has no usable name or value.
    /// </exception>
    public static Parcel Create(
        Weight weight,
        DeclaredValue declaredValue,
        CountryCode destinationCountry,
        IReadOnlyDictionary<string, string>? additionalAttributes = null)
    {
        if (weight.Kilograms <= 0m)
        {
            throw new DomainValidationException(
                DomainErrorCodes.WeightMustBePositive,
                "Parcel weight must be greater than zero kilograms.",
                nameof(weight));
        }

        if (string.IsNullOrWhiteSpace(destinationCountry.Value)
            || !IsoCountryCodes.Contains(destinationCountry.Value))
        {
            throw new DomainValidationException(
                DomainErrorCodes.CountryInvalid,
                "Destination country must be an assigned ISO 3166-1 alpha-2 code.",
                nameof(destinationCountry));
        }

        IReadOnlyDictionary<string, string> copiedAttributes =
            CopyAdditionalAttributes(additionalAttributes);

        return new Parcel(
            weight,
            declaredValue,
            destinationCountry,
            copiedAttributes);
    }

    /// <summary>
    /// Validates and defensively copies optional attributes so caller mutation
    /// cannot change a parcel after construction.
    /// </summary>
    /// <param name="source">The optional caller-owned attribute map.</param>
    /// <returns>An ordinal-ignore-case read-only attribute map.</returns>
    /// <exception cref="DomainValidationException">
    /// Thrown for blank names or null values.
    /// </exception>
    private static IReadOnlyDictionary<string, string> CopyAdditionalAttributes(
        IReadOnlyDictionary<string, string>? source)
    {
        if (source is null || source.Count == 0)
        {
            return new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        var copy = new Dictionary<string, string>(
            source.Count,
            StringComparer.OrdinalIgnoreCase);

        foreach ((string name, string value) in source)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new DomainValidationException(
                    DomainErrorCodes.AdditionalAttributeNameInvalid,
                    "Additional attribute names cannot be blank.",
                    nameof(source));
            }

            if (value is null)
            {
                throw new DomainValidationException(
                    DomainErrorCodes.AdditionalAttributeValueInvalid,
                    "Additional attribute values cannot be null.",
                    nameof(source));
            }

            if (!copy.TryAdd(name.Trim(), value))
            {
                throw new DomainValidationException(
                    DomainErrorCodes.AdditionalAttributeNameInvalid,
                    "Additional attribute names must be unique after normalization.",
                    nameof(source));
            }
        }

        return new ReadOnlyDictionary<string, string>(copy);
    }
}
