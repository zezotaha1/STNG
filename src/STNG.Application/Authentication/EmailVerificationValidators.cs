using FluentValidation;
using Volo.Abp.Identity;

namespace STNG.Authentication;

public class ResendVerificationCodeValidator : AbstractValidator<ResendVerificationCodeDto>
{
    public ResendVerificationCodeValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(IdentityUserConsts.MaxEmailLength);
}

public class VerifyParentEmailValidator : AbstractValidator<VerifyParentEmailDto>
{
    public VerifyParentEmailValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(IdentityUserConsts.MaxEmailLength);
        RuleFor(x => x.Otp).NotEmpty().Matches("^[0-9]{6}$");
    }
}
