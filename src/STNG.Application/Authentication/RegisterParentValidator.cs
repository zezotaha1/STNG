using FluentValidation;
using Microsoft.Extensions.Localization;
using STNG.Localization;
using STNG.Parents;
using Volo.Abp.Identity;

namespace STNG.Authentication;

public class RegisterParentValidator : AbstractValidator<RegisterParentDto>
{
    public RegisterParentValidator(IStringLocalizer<STNGResource> localizer)
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(IdentityUserConsts.MaxEmailLength);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(IdentityUserConsts.MaxNameLength);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Job).MaximumLength(Parent.MaxJobLength);
        RuleFor(x => x.DateOfBirth)
            .NotNull()
            .WithErrorCode(STNGDomainErrorCodes.ParentDateOfBirthRequired)
            .WithMessage(localizer[STNGDomainErrorCodes.ParentDateOfBirthRequired]);
        RuleFor(x => x.PhoneCountryIso2)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithErrorCode(STNGDomainErrorCodes.ParentPhoneCountryRequired)
            .WithMessage(localizer[STNGDomainErrorCodes.ParentPhoneCountryRequired])
            .Matches("^[A-Za-z]{2}$")
            .WithErrorCode(STNGDomainErrorCodes.ParentInvalidPhoneCountry)
            .WithMessage(localizer[STNGDomainErrorCodes.ParentInvalidPhoneCountry]);
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .WithErrorCode(STNGDomainErrorCodes.ParentPhoneNumberRequired)
            .WithMessage(localizer[STNGDomainErrorCodes.ParentPhoneNumberRequired]);
    }
}
