using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace STNG.Families;

// Retained child entity: leaving/removal changes status and never soft-deletes the membership.
public class FamilyMember : AuditedEntity<Guid>
{
    public Guid FamilyId { get; private set; }
    public Guid ParentId { get; private set; }
    public FamilyMemberRole Role { get; private set; }
    public FamilyMemberStatus Status { get; private set; }
    public DateTime JoinedAt { get; private set; }
    public DateTime? LeftAt { get; private set; }
    public Guid? RemovedByParentId { get; private set; }

    private FamilyMember() { }
    internal FamilyMember(Guid id, Guid familyId, Guid parentId, FamilyMemberRole role, DateTime now) : base(id)
    {
        if (id == Guid.Empty || parentId == Guid.Empty) throw new ArgumentException("Membership identities are required.");
        FamilyId = familyId;
        ParentId = parentId;
        Role = role;
        Status = FamilyMemberStatus.Active;
        JoinedAt = now;
    }

    internal void ChangeRole(FamilyMemberRole role) => Role = role;
    internal void End(FamilyMemberStatus status, DateTime now, Guid? removedBy = null)
    {
        Status = status;
        LeftAt = now;
        RemovedByParentId = removedBy;
    }

    internal void Reactivate(DateTime now)
    {
        Role = FamilyMemberRole.Member;
        Status = FamilyMemberStatus.Active;
        JoinedAt = now;
        LeftAt = null;
        RemovedByParentId = null;
    }
}
