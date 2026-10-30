using FluentValidation;

namespace STNG.Families;

public class CreateFamilyValidator : AbstractValidator<CreateFamilyDto>
{
    public CreateFamilyValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().Must(x => x == null || x.Trim().Length <= FamilyConsts.MaxNameLength);
    }
}

public class FamilyMutationValidator : AbstractValidator<FamilyMutationDto>
{
    public FamilyMutationValidator() => RuleFor(x => x.ConcurrencyStamp).NotEmpty().MaximumLength(40);
}

public class UpdateFamilyValidator : AbstractValidator<UpdateFamilyDto>
{
    public UpdateFamilyValidator()
    {
        Include(new FamilyMutationValidator());
        RuleFor(x => x.Name).NotEmpty().Must(x => x == null || x.Trim().Length <= FamilyConsts.MaxNameLength);
    }
}

public class InitiateOwnershipTransferValidator : AbstractValidator<InitiateOwnershipTransferDto>
{
    public InitiateOwnershipTransferValidator()
    {
        Include(new FamilyMutationValidator());
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.ToParentId).NotEmpty();
    }
}

public class FamilyPageValidator : AbstractValidator<FamilyPageDto>
{
    public FamilyPageValidator()
    {
        RuleFor(x => x.SkipCount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxResultCount).InclusiveBetween(1, FamilyConsts.MaxPageSize);
    }
}
