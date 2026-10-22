namespace STNG;

public static class STNGDomainErrorCodes
{
    public const string RegistrationUnavailable = "STNG:RegistrationUnavailable";
    public const string ParentDateOfBirthRequired = "STNG:Parent:DateOfBirthRequired";
    public const string ParentInvalidDateOfBirth = "STNG:Parent:InvalidDateOfBirth";
    public const string ParentMinimumAgeNotMet = "STNG:Parent:MinimumAgeNotMet";
    public const string ParentPhoneNumberRequired = "STNG:Parent:PhoneNumberRequired";
    public const string ParentPhoneCountryRequired = "STNG:Parent:PhoneCountryRequired";
    public const string ParentInvalidPhoneCountry = "STNG:Parent:InvalidPhoneCountry";
    public const string ParentInvalidMobileNumber = "STNG:Parent:InvalidMobileNumber";
    public const string ParentPhoneCountryMismatch = "STNG:Parent:PhoneCountryMismatch";
    public const string ParentUnsupportedPhoneType = "STNG:Parent:UnsupportedPhoneType";
    public const string ParentPhoneExtensionNotSupported = "STNG:Parent:PhoneExtensionNotSupported";
}
