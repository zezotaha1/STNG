using System;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Content;

namespace STNG.FileManagement;

public class FileAssetDto : AuditedEntityDto<Guid>
{
    public string OriginalFileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public string Extension { get; set; } = null!;
    public long SizeInBytes { get; set; }
    public string? ContentHash { get; set; }
    public FileVisibility Visibility { get; set; }
    public bool IsDiscoverable { get; set; }
    public FileAssetStatus Status { get; set; }
}

public class FileReferenceDto : EntityDto<Guid>
{
    public string ResourceType { get; set; } = null!;
    public string ResourceId { get; set; } = null!;
    public string Purpose { get; set; } = null!;
}

public class UploadFileInput
{
    public IRemoteStreamContent Content { get; set; } = null!;
    public Guid? OwnerUserId { get; set; }
}

public class ReplaceFileContentInput
{
    public IRemoteStreamContent Content { get; set; } = null!;
}

public class FileReferenceInput
{
    public string ResourceType { get; set; } = "";
    public string ResourceId { get; set; } = "";
    public string Purpose { get; set; } = "";
}

public class ChangeFileVisibilityInput
{
    public FileVisibility Visibility { get; set; }
    public bool IsDiscoverable { get; set; }
}

public class GetFileListInput : PagedResultRequestDto
{
    public string? Filter { get; set; }
}

public class FileCleanupResultDto
{
    public int InspectedCount { get; set; }
    public int CleanedCount { get; set; }
}
