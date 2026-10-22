using FluentValidation;

namespace STNG.FileManagement;

public class UploadFileInputValidator : AbstractValidator<UploadFileInput>
{
    public UploadFileInputValidator()
    {
        RuleFor(x => x.Content).NotNull();
        When(x => x.Content != null, () =>
        {
            RuleFor(x => x.Content.FileName).NotEmpty().MaximumLength(FileManagementConsts.MaxFileNameLength);
            RuleFor(x => x.Content.ContentType).NotEmpty().MaximumLength(FileManagementConsts.MaxContentTypeLength);
        });
    }
}

public class ReplaceFileContentInputValidator : AbstractValidator<ReplaceFileContentInput>
{
    public ReplaceFileContentInputValidator()
    {
        RuleFor(x => x.Content).NotNull();
        When(x => x.Content != null, () =>
        {
            RuleFor(x => x.Content.FileName).NotEmpty().MaximumLength(FileManagementConsts.MaxFileNameLength);
            RuleFor(x => x.Content.ContentType).NotEmpty().MaximumLength(FileManagementConsts.MaxContentTypeLength);
        });
    }
}

public class FileReferenceInputValidator : AbstractValidator<FileReferenceInput>
{
    public FileReferenceInputValidator()
    {
        RuleFor(x => x.ResourceType).NotEmpty().MaximumLength(FileManagementConsts.MaxResourceTypeLength);
        RuleFor(x => x.ResourceId).NotEmpty().MaximumLength(FileManagementConsts.MaxResourceIdLength);
        RuleFor(x => x.Purpose).NotEmpty().MaximumLength(FileManagementConsts.MaxPurposeLength);
    }
}

public class GetFileListInputValidator : AbstractValidator<GetFileListInput>
{
    public GetFileListInputValidator()
    {
        RuleFor(x => x.SkipCount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxResultCount).InclusiveBetween(1, FileManagementConsts.MaxDiscoveryPageSize);
        RuleFor(x => x.Filter).MaximumLength(FileManagementConsts.MaxFileNameLength);
    }
}
