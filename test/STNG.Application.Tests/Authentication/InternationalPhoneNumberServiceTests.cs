using Shouldly;
using Volo.Abp;
using Xunit;

namespace STNG.Authentication;

public class InternationalPhoneNumberServiceTests
{
    private readonly InternationalPhoneNumberService _service = new();

    [Theory]
    [InlineData("EG", "01012345678", "+201012345678")]
    [InlineData("SA", "0501234567", "+966501234567")]
    [InlineData("AE", "0501234567", "+971501234567")]
    [InlineData("US", "4155552671", "+14155552671")]
    [InlineData("CA", "4165551234", "+14165551234")]
    [InlineData("EG", "  +201012345678  ", "+201012345678")]
    public void Normalizes_valid_mobile_numbers(string country, string input, string expected)
    {
        var result = _service.ValidateAndNormalizeMobile(input, country.ToLowerInvariant());

        result.E164.ShouldBe(expected);
        result.CountryIso2.ShouldBe(country);
    }

    [Theory]
    [InlineData("ZZ", "+201012345678", STNGDomainErrorCodes.ParentInvalidPhoneCountry)]
    [InlineData("EG", "+966501234567", STNGDomainErrorCodes.ParentPhoneCountryMismatch)]
    [InlineData("US", "+14165551234", STNGDomainErrorCodes.ParentPhoneCountryMismatch)]
    [InlineData("EG", "123", STNGDomainErrorCodes.ParentInvalidMobileNumber)]
    [InlineData("EG", "+201012345678901234567890", STNGDomainErrorCodes.ParentInvalidMobileNumber)]
    [InlineData("EG", "+20223456789", STNGDomainErrorCodes.ParentUnsupportedPhoneType)]
    [InlineData("GB", "+445612345678", STNGDomainErrorCodes.ParentUnsupportedPhoneType)]
    [InlineData("US", "+18005551234", STNGDomainErrorCodes.ParentUnsupportedPhoneType)]
    [InlineData("EG", "+201012345678 ext. 123", STNGDomainErrorCodes.ParentPhoneExtensionNotSupported)]
    public void Rejects_invalid_or_unsupported_numbers(string country, string input, string errorCode)
    {
        var exception = Should.Throw<BusinessException>(() =>
            _service.ValidateAndNormalizeMobile(input, country));

        exception.Code.ShouldBe(errorCode);
        exception.Message.ShouldNotContain(input);
    }

    [Theory]
    [InlineData("", "EG", STNGDomainErrorCodes.ParentPhoneNumberRequired)]
    [InlineData("01012345678", "", STNGDomainErrorCodes.ParentPhoneCountryRequired)]
    public void Rejects_missing_input(string phone, string country, string errorCode)
    {
        Should.Throw<BusinessException>(() => _service.ValidateAndNormalizeMobile(phone, country))
            .Code.ShouldBe(errorCode);
    }
}
