using System;
using FluentValidation;

namespace STNG.Families;

public class CreateFamilyJoinCredentialValidator : AbstractValidator<CreateFamilyJoinCredentialDto>
{
    public CreateFamilyJoinCredentialValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.Kind).IsInEnum();
    }
}

public class SubmitFamilyJoinRequestValidator : AbstractValidator<SubmitFamilyJoinRequestDto>
{
    public SubmitFamilyJoinRequestValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Secret).NotEmpty().MaximumLength(FamilyJoinConsts.MaxSecretInputLength);
    }
}

public class RejectFamilyJoinRequestValidator : AbstractValidator<RejectFamilyJoinRequestDto>
{
    public RejectFamilyJoinRequestValidator()
    {
        Include(new FamilyMutationValidator());
        RuleFor(x => x.Reason).MaximumLength(FamilyJoinConsts.MaxRejectionReasonLength);
    }
}

public class FamilyJoinRequestPageValidator : AbstractValidator<FamilyJoinRequestPageDto>
{
    public FamilyJoinRequestPageValidator()
    {
        Include(new FamilyPageValidator());
        RuleFor(x => x.Status).Must(x => !x.HasValue || Enum.IsDefined(x.Value));
    }
}
