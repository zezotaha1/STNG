namespace STNG.Authentication;

/// <summary>
/// Validates numbering-plan metadata and normalizes mobile numbers. A successful result does not
/// prove that the number is active, SMS-capable, or owned by the registrant.
/// </summary>
public interface IInternationalPhoneNumberService
{
    InternationalPhoneNumber ValidateAndNormalizeMobile(string phoneNumber, string countryIso2);
}

public sealed record InternationalPhoneNumber(string E164, string CountryIso2);
