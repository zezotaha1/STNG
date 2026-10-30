using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace STNG.Families;

// Append-only record, independent of the membership's current state and audit fields.
public class FamilyMembershipHistory : CreationAuditedEntity<Guid>
{
    public Guid FamilyId { get; private set; }
    public Guid MemberId { get; private set; }
    public Guid ParentId { get; private set; }
    public Guid ActorParentId { get; private set; }
    public FamilyMembershipChange Change { get; private set; }
    public FamilyMemberRole Role { get; private set; }
    public DateTime OccurredAt { get; private set; }

    private FamilyMembershipHistory() { }
    internal FamilyMembershipHistory(Guid id, FamilyMember member, Guid actorParentId,
        FamilyMembershipChange change, DateTime now) : base(id)
    {
        FamilyId = member.FamilyId;
        MemberId = member.Id;
        ParentId = member.ParentId;
        ActorParentId = actorParentId;
        Change = change;
        Role = member.Role;
        OccurredAt = now;
    }
}

public sealed record FamilyMembershipChangedEvent(Guid FamilyId, Guid ParentId, Guid ActorParentId,
    FamilyMembershipChange Change, FamilyMemberRole Role, DateTime OccurredAt);

public sealed record FamilyOwnershipTransferredEvent(Guid FamilyId, Guid TransferId,
    Guid FromParentId, Guid ToParentId, DateTime OccurredAt);
