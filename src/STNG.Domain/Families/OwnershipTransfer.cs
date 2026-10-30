using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace STNG.Families;

// Retained mutable child; terminal decisions are part of the family's ownership history.
public class OwnershipTransfer : AuditedEntity<Guid>
{
    public Guid FamilyId { get; private set; }
    public Guid FromParentId { get; private set; }
    public Guid ToParentId { get; private set; }
    public OwnershipTransferStatus Status { get; private set; }
    public DateTime RequestedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? AcceptedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }

    private OwnershipTransfer() { }
    internal OwnershipTransfer(Guid id, Guid familyId, Guid fromParentId, Guid toParentId, DateTime now, DateTime expiresAt) : base(id)
    {
        FamilyId = familyId;
        FromParentId = fromParentId;
        ToParentId = toParentId;
        RequestedAt = now;
        ExpiresAt = expiresAt;
        Status = OwnershipTransferStatus.Pending;
    }

    internal void Decide(OwnershipTransferStatus status, DateTime now)
    {
        Status = status;
        if (status == OwnershipTransferStatus.Accepted) AcceptedAt = now;
        if (status == OwnershipTransferStatus.Cancelled) CancelledAt = now;
    }
}
