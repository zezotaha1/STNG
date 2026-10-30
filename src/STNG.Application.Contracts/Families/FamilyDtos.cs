using System;
using Volo.Abp.Application.Dtos;

namespace STNG.Families;

public class CreateFamilyDto
{
    /// <summary>Client-generated idempotency key. Reuse it when retrying the same creation request.</summary>
    public Guid RequestId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class FamilyMutationDto
{
    /// <summary>The concurrency stamp returned by the latest family response.</summary>
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class UpdateFamilyDto : FamilyMutationDto
{
    public string Name { get; set; } = string.Empty;
}

public class InitiateOwnershipTransferDto : FamilyMutationDto
{
    /// <summary>Client-generated transfer identity, reused when retrying initiation.</summary>
    public Guid RequestId { get; set; }
    /// <summary>The target membership's parent profile identity, not an identity user ID.</summary>
    public Guid ToParentId { get; set; }
}

public class FamilyPageDto : PagedResultRequestDto
{
    public FamilyPageDto() => MaxResultCount = 20;
}

public class FamilyDto : EntityDto<Guid>
{
    public string Name { get; set; } = string.Empty;
    public Guid? ImageFileAssetId { get; set; }
    public Guid CreatedByParentId { get; set; }
    public FamilyStatus Status { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }
    public FamilyMemberRole MyRole { get; set; }
}

public class FamilyMemberDto : EntityDto<Guid>
{
    public Guid ParentId { get; set; }
    public FamilyMemberRole Role { get; set; }
    public FamilyMemberStatus Status { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime? LeftAt { get; set; }
    public Guid? RemovedByParentId { get; set; }
}

public class OwnershipTransferDto : EntityDto<Guid>
{
    public Guid FromParentId { get; set; }
    public Guid ToParentId { get; set; }
    public OwnershipTransferStatus Status { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string FamilyConcurrencyStamp { get; set; } = string.Empty;
}
