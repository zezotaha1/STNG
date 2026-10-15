using PhoneNumbers;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace STNG.Authentication;

public class InternationalPhoneNumberService : IInternationalPhoneNumberService, ITransientDependency
{
    private readonly PhoneNumberUtil _phoneNumbers = PhoneNumberUtil.GetInstance();

    public InternationalPhoneNumber ValidateAndNormalizeMobile(string phoneNumber, string countryIso2)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw Error(STNGDomainErrorCodes.ParentPhoneNumberRequired);
        }

        if (string.IsNullOrWhiteSpace(countryIso2))
        {
            throw Error(STNGDomainErrorCodes.ParentPhoneCountryRequired);
        }

        var region = countryIso2.Trim().ToUpperInvariant();
        if (region.Length != 2 || region[0] is < 'A' or > 'Z' || region[1] is < 'A' or > 'Z' ||
            !_phoneNumbers.GetSupportedRegions().Contains(region))
        {
            throw Error(STNGDomainErrorCodes.ParentInvalidPhoneCountry);
        }

        PhoneNumber parsed;
        try
        {
            parsed = _phoneNumbers.Parse(phoneNumber.Trim(), region);
        }
        catch (NumberParseException)
        {
            throw Error(STNGDomainErrorCodes.ParentInvalidMobileNumber);
        }

        if (parsed.HasExtension)
        {
            throw Error(STNGDomainErrorCodes.ParentPhoneExtensionNotSupported);
        }

        if (!_phoneNumbers.IsPossibleNumber(parsed) || !_phoneNumbers.IsValidNumber(parsed))
        {
            throw Error(STNGDomainErrorCodes.ParentInvalidMobileNumber);
        }

        if (!_phoneNumbers.IsValidNumberForRegion(parsed, region))
        {
            throw Error(STNGDomainErrorCodes.ParentPhoneCountryMismatch);
        }

        var numberType = _phoneNumbers.GetNumberType(parsed);
        if (numberType is not PhoneNumberType.MOBILE and not PhoneNumberType.FIXED_LINE_OR_MOBILE)
        {
            throw Error(STNGDomainErrorCodes.ParentUnsupportedPhoneType);
        }

        return new InternationalPhoneNumber(
            _phoneNumbers.Format(parsed, PhoneNumberFormat.E164),
            region);
    }

    private static BusinessException Error(string code) => new(code);
}
