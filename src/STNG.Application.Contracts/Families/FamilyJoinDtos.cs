using System;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Auditing;

namespace STNG.Families;

public class CreateFamilyJoinCredentialDto
{
    public Guid RequestId { get; set; }
    public FamilyJoinCredentialKind Kind { get; set; }
}

[DisableAuditing]
public class SubmitFamilyJoinRequestDto
{
    public Guid RequestId { get; set; }
    public FamilyJoinCredentialKind Kind { get; set; }
    public string Secret { get; set; } = string.Empty;
}

public class RejectFamilyJoinRequestDto : FamilyMutationDto
{
    public string? Reason { get; set; }
}

public class FamilyJoinRequestPageDto : FamilyPageDto
{
    public FamilyJoinRequestStatus? Status { get; set; }
}

public class FamilyJoinCredentialDto : EntityDto<Guid>
{
    public FamilyJoinCredentialKind Kind { get; set; }
    public FamilyJoinCredentialStatus Status { get; set; }
    public Guid CreatedByParentId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

[DisableAuditing]
public class IssuedFamilyJoinCredentialDto
{
    public FamilyJoinCredentialDto Credential { get; set; } = null!;
    /// <summary>Returned once on initial issuance. Null on an idempotent replay.</summary>
    public string? Secret { get; set; }
    /// <summary>Opaque QR payload; returned once for QR credentials. No family/parent information.</summary>
    public string? JoinLink { get; set; }
}

public class FamilyJoinRequestDto : EntityDto<Guid>
{
    public Guid FamilyId { get; set; }
    public Guid ParentId { get; set; }
    public Guid CredentialId { get; set; }
    public FamilyJoinRequestStatus Status { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public Guid? ReviewedByParentId { get; set; }
    public string? RejectionReason { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
}
