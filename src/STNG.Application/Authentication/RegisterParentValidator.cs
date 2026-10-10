using FluentValidation;
using STNG.Parents;
using Volo.Abp.Identity;

namespace STNG.Authentication;

public class RegisterParentValidator : AbstractValidator<RegisterParentDto>
{
    public RegisterParentValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(IdentityUserConsts.MaxEmailLength);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(IdentityUserConsts.MaxNameLength);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Job).MaximumLength(Parent.MaxJobLength);
    }
}
