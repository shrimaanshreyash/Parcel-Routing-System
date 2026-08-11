using ParcelRoutingSystem.Domain;
using ParcelRoutingSystem.Domain.Parcels;

namespace ParcelRoutingSystem.Domain.Tests;

/// <summary>
/// Verifies invalid parcel facts are rejected at the domain boundary before a
/// routing decision can be attempted.
/// </summary>
public sealed class ParcelValidationTests
{
    /// <summary>
    /// Verifies zero weight fails because every routed parcel must have positive
    /// physical weight.
    /// </summary>
    [Fact]
    public void FromKilograms_WhenWeightIsZero_RejectsValue()
    {
        DomainValidationException exception =
            Assert.Throws<DomainValidationException>(() => Weight.FromKilograms(0m));

        Assert.Equal(DomainErrorCodes.WeightMustBePositive, exception.Code);
    }

    /// <summary>
    /// Verifies negative weight fails instead of accidentally matching the Mail
    /// catchment.
    /// </summary>
    [Fact]
    public void FromKilograms_WhenWeightIsNegative_RejectsValue()
    {
        DomainValidationException exception =
            Assert.Throws<DomainValidationException>(() => Weight.FromKilograms(-0.01m));

        Assert.Equal(DomainErrorCodes.WeightMustBePositive, exception.Code);
    }

    /// <summary>
    /// Verifies negative declared value fails while zero remains a legitimate
    /// value for the supplied legacy data.
    /// </summary>
    [Fact]
    public void FromEuros_WhenValueIsNegative_RejectsValue()
    {
        DomainValidationException exception =
            Assert.Throws<DomainValidationException>(() => DeclaredValue.FromEuros(-0.01m));

        Assert.Equal(DomainErrorCodes.DeclaredValueMustBeNonNegative, exception.Code);
    }

    /// <summary>
    /// Verifies a missing country is rejected rather than silently inferred from
    /// an address or source file.
    /// </summary>
    [Fact]
    public void FromAlpha2_WhenCountryIsMissing_RejectsValue()
    {
        DomainValidationException exception =
            Assert.Throws<DomainValidationException>(() => CountryCode.FromAlpha2(" "));

        Assert.Equal(DomainErrorCodes.CountryRequired, exception.Code);
    }

    /// <summary>
    /// Verifies a syntactically plausible but unassigned country code is rejected.
    /// </summary>
    [Fact]
    public void FromAlpha2_WhenCountryIsUnknown_RejectsValue()
    {
        DomainValidationException exception =
            Assert.Throws<DomainValidationException>(() => CountryCode.FromAlpha2("ZZ"));

        Assert.Equal(DomainErrorCodes.CountryInvalid, exception.Code);
    }

    /// <summary>
    /// Verifies country input is normalized to uppercase so equivalent operator
    /// input produces one stable domain value.
    /// </summary>
    [Fact]
    public void FromAlpha2_WhenCountryUsesLowercase_NormalizesValue()
    {
        CountryCode country = CountryCode.FromAlpha2("gb");

        Assert.Equal("GB", country.Value);
    }

    /// <summary>
    /// Verifies a default value-type weight cannot bypass the public weight
    /// factory when a parcel aggregate is created.
    /// </summary>
    [Fact]
    public void Create_WhenWeightValueObjectIsDefault_RejectsParcel()
    {
        DomainValidationException exception = Assert.Throws<DomainValidationException>(
            () => Parcel.Create(
                default,
                DeclaredValue.FromEuros(0m),
                CountryCode.FromAlpha2("GB")));

        Assert.Equal(DomainErrorCodes.WeightMustBePositive, exception.Code);
    }

    /// <summary>
    /// Verifies a default value-type country cannot bypass assigned-country
    /// validation when a parcel aggregate is created.
    /// </summary>
    [Fact]
    public void Create_WhenCountryValueObjectIsDefault_RejectsParcel()
    {
        DomainValidationException exception = Assert.Throws<DomainValidationException>(
            () => Parcel.Create(
                Weight.FromKilograms(1m),
                DeclaredValue.FromEuros(0m),
                default));

        Assert.Equal(DomainErrorCodes.CountryInvalid, exception.Code);
    }

    /// <summary>
    /// Verifies optional attributes are copied so caller mutation cannot change a
    /// parcel after it has entered the domain.
    /// </summary>
    [Fact]
    public void Create_WhenAttributesAreProvided_DefensivelyCopiesValues()
    {
        var source = new Dictionary<string, string>
        {
            ["operator-reference"] = "BAY-04-017",
        };

        Parcel parcel = RoutingTestFixture.CreateParcel(2m, 10m, attributes: source);
        source["operator-reference"] = "CHANGED";

        Assert.Equal("BAY-04-017", parcel.AdditionalAttributes["operator-reference"]);
    }

    /// <summary>
    /// Verifies blank attribute names fail because unnamed data cannot be
    /// explained or safely mapped by future constrained rules.
    /// </summary>
    [Fact]
    public void Create_WhenAttributeNameIsBlank_RejectsParcel()
    {
        var attributes = new Dictionary<string, string>
        {
            [" "] = "value",
        };

        DomainValidationException exception = Assert.Throws<DomainValidationException>(
            () => RoutingTestFixture.CreateParcel(2m, 10m, attributes: attributes));

        Assert.Equal(DomainErrorCodes.AdditionalAttributeNameInvalid, exception.Code);
    }

    /// <summary>
    /// Verifies attribute names that become equal after case-insensitive
    /// normalization fail with a stable domain error instead of a dictionary
    /// implementation exception.
    /// </summary>
    [Fact]
    public void Create_WhenNormalizedAttributeNamesAreDuplicated_RejectsParcel()
    {
        var attributes = new Dictionary<string, string>
        {
            ["priority"] = "standard",
            [" Priority "] = "express",
        };

        DomainValidationException exception = Assert.Throws<DomainValidationException>(
            () => RoutingTestFixture.CreateParcel(2m, 10m, attributes: attributes));

        Assert.Equal(DomainErrorCodes.AdditionalAttributeNameInvalid, exception.Code);
    }
}
