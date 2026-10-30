using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace STNG.Families;

// Archive is a business lifecycle, not deletion. Family is host-scoped, never an ABP tenant.
public class Family : AuditedAggregateRoot<Guid>
{
    private readonly List<FamilyMember> _members = new();
    private readonly List<OwnershipTransfer> _ownershipTransfers = new();
    private readonly List<FamilyMembershipHistory> _membershipHistory = new();

    public FamilyName Name { get; private set; } = null!;
    public Guid? ImageFileAssetId { get; private set; }
    public Guid CreatedByParentId { get; private set; }
    public Guid CreationRequestId { get; private set; }
    public FamilyStatus Status { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<FamilyMember> Members => _members.AsReadOnly();
    public IReadOnlyCollection<OwnershipTransfer> OwnershipTransfers => _ownershipTransfers.AsReadOnly();
    public IReadOnlyCollection<FamilyMembershipHistory> MembershipHistory => _membershipHistory.AsReadOnly();

    private Family() { }
    internal Family(Guid id, FamilyName name, Guid creatorParentId, Guid ownerMemberId,
        Guid requestId, DateTime now) : base(id)
    {
        if (id == Guid.Empty || creatorParentId == Guid.Empty || requestId == Guid.Empty)
            throw new ArgumentException("Family, parent and request identities are required.");
        Name = Check.NotNull(name, nameof(name));
        CreatedByParentId = creatorParentId;
        CreationRequestId = requestId;
        Status = FamilyStatus.Active;
        var owner = new FamilyMember(ownerMemberId, Id, creatorParentId, FamilyMemberRole.Owner, now);
        _members.Add(owner);
        Record(owner, creatorParentId, FamilyMembershipChange.Joined, now);
    }

    public FamilyMember RequireMember(Guid parentId)
    {
        var member = _members.SingleOrDefault(x => x.ParentId == parentId);
        if (member == null || member.Status != FamilyMemberStatus.Active)
            throw new BusinessException(FamilyErrorCodes.MembershipInactive);
        return member;
    }

    public void EnsureActive()
    {
        if (Status != FamilyStatus.Active)
            throw new BusinessException(Status == FamilyStatus.Archived ? FamilyErrorCodes.Archived : FamilyErrorCodes.NotActive);
    }

    public void RequireAdministrator(Guid parentId)
    {
        EnsureActive();
        if (RequireMember(parentId).Role == FamilyMemberRole.Member)
            throw new BusinessException(FamilyErrorCodes.InsufficientRole);
    }

    public void RequireOwner(Guid parentId)
    {
        EnsureActive();
        if (RequireMember(parentId).Role != FamilyMemberRole.Owner)
            throw new BusinessException(FamilyErrorCodes.InsufficientRole);
    }

    public void Rename(Guid actorParentId, FamilyName name)
    {
        RequireAdministrator(actorParentId);
        Name = Check.NotNull(name, nameof(name));
        Touch();
    }

    public void ChangeImage(Guid actorParentId, Guid? assetId)
    {
        RequireAdministrator(actorParentId);
        if (assetId == Guid.Empty) throw new ArgumentException("An image asset identity cannot be empty.", nameof(assetId));
        ImageFileAssetId = assetId;
        Touch();
    }

    public void AddMember(Guid actorParentId, Guid memberId, Guid parentId, DateTime now)
    {
        RequireAdministrator(actorParentId);
        if (_members.Any(x => x.ParentId == parentId)) throw new BusinessException(FamilyErrorCodes.AlreadyMember);
        var member = new FamilyMember(memberId, Id, parentId, FamilyMemberRole.Member, now);
        _members.Add(member);
        Record(member, actorParentId, FamilyMembershipChange.Joined, now);
        Touch();
    }

    public void ReactivateMember(Guid actorParentId, Guid parentId, DateTime now)
    {
        RequireAdministrator(actorParentId);
        var member = FindMember(parentId);
        if (member.Status == FamilyMemberStatus.Active) throw new BusinessException(FamilyErrorCodes.AlreadyMember);
        member.Reactivate(now);
        Record(member, actorParentId, FamilyMembershipChange.Reactivated, now);
        Touch();
    }

    public void Leave(Guid actorParentId, DateTime now)
    {
        EnsureActive();
        var member = RequireMember(actorParentId);
        if (member.Role == FamilyMemberRole.Owner) throw new BusinessException(FamilyErrorCodes.OwnerCannotLeave);
        member.End(FamilyMemberStatus.Left, now);
        CancelTransfersTo(actorParentId, now);
        Record(member, actorParentId, FamilyMembershipChange.Left, now);
        Touch();
    }

    public void RemoveMember(Guid actorParentId, Guid parentId, DateTime now)
    {
        RequireAdministrator(actorParentId);
        var member = RequireMember(parentId);
        if (member.Role == FamilyMemberRole.Owner) throw new BusinessException(FamilyErrorCodes.CannotRemoveOwner);
        if (actorParentId == parentId ||
            (member.Role == FamilyMemberRole.Admin && RequireMember(actorParentId).Role != FamilyMemberRole.Owner))
            throw new BusinessException(FamilyErrorCodes.InsufficientRole);
        member.End(FamilyMemberStatus.Removed, now, actorParentId);
        CancelTransfersTo(parentId, now);
        Record(member, actorParentId, FamilyMembershipChange.Removed, now);
        Touch();
    }

    public void PromoteToAdmin(Guid actorParentId, Guid parentId, DateTime now) =>
        ChangeRole(actorParentId, parentId, FamilyMemberRole.Member, FamilyMemberRole.Admin, FamilyMembershipChange.Promoted, now);

    public void DemoteAdmin(Guid actorParentId, Guid parentId, DateTime now) =>
        ChangeRole(actorParentId, parentId, FamilyMemberRole.Admin, FamilyMemberRole.Member, FamilyMembershipChange.Demoted, now);

    private void ChangeRole(Guid actorParentId, Guid parentId, FamilyMemberRole from, FamilyMemberRole to,
        FamilyMembershipChange change, DateTime now)
    {
        RequireOwner(actorParentId);
        var member = RequireMember(parentId);
        if (member.Role != from) throw new BusinessException(FamilyErrorCodes.InvalidRoleTransition);
        member.ChangeRole(to);
        Record(member, actorParentId, change, now);
        Touch();
    }

    public OwnershipTransfer InitiateOwnershipTransfer(Guid actorParentId, Guid transferId, Guid toParentId,
        DateTime now, DateTime expiresAt)
    {
        RequireOwner(actorParentId);
        if (toParentId == actorParentId || !_members.Any(x => x.ParentId == toParentId && x.Status == FamilyMemberStatus.Active))
            throw new BusinessException(FamilyErrorCodes.InvalidOwnershipRecipient);
        if (transferId == Guid.Empty || expiresAt <= now) throw new ArgumentException("A valid transfer and expiration are required.");
        var existing = _ownershipTransfers.SingleOrDefault(x => x.Id == transferId);
        if (existing != null)
        {
            if (existing.FromParentId != actorParentId || existing.ToParentId != toParentId)
                throw new BusinessException(FamilyErrorCodes.TransferConflict);
            return existing;
        }
        if (_ownershipTransfers.Any(x => x.Status == OwnershipTransferStatus.Pending && x.ExpiresAt > now))
            throw new BusinessException(FamilyErrorCodes.TransferAlreadyPending);
        foreach (var expired in _ownershipTransfers.Where(x => x.Status == OwnershipTransferStatus.Pending))
            expired.Decide(OwnershipTransferStatus.Expired, now);
        var transfer = new OwnershipTransfer(transferId, Id, actorParentId, toParentId, now, expiresAt);
        _ownershipTransfers.Add(transfer);
        Touch();
        return transfer;
    }

    public void CompleteOwnershipTransfer(Guid actorParentId, Guid transferId, DateTime now)
    {
        EnsureActive();
        var transfer = FindTransfer(transferId);
        if (transfer.ToParentId != actorParentId) throw new BusinessException(FamilyErrorCodes.InsufficientRole);
        RequireMember(actorParentId);
        if (transfer.Status == OwnershipTransferStatus.Accepted) return;
        EnsurePending(transfer, now);
        var previousOwner = RequireMember(transfer.FromParentId);
        if (previousOwner.Role != FamilyMemberRole.Owner)
            throw new BusinessException(FamilyErrorCodes.TransferConflict);
        var recipient = RequireMember(actorParentId);
        if (recipient.Role == FamilyMemberRole.Owner) throw new BusinessException(FamilyErrorCodes.InvalidOwnershipRecipient);
        previousOwner.ChangeRole(FamilyMemberRole.Admin);
        recipient.ChangeRole(FamilyMemberRole.Owner);
        transfer.Decide(OwnershipTransferStatus.Accepted, now);
        Record(previousOwner, actorParentId, FamilyMembershipChange.Demoted, now);
        Record(recipient, actorParentId, FamilyMembershipChange.Promoted, now);
        AddLocalEvent(new FamilyOwnershipTransferredEvent(Id, transfer.Id, transfer.FromParentId, transfer.ToParentId, now));
        Touch();
    }

    public void DeclineOwnershipTransfer(Guid actorParentId, Guid transferId, DateTime now)
    {
        EnsureActive();
        RequireMember(actorParentId);
        var transfer = FindTransfer(transferId);
        if (transfer.ToParentId != actorParentId) throw new BusinessException(FamilyErrorCodes.InsufficientRole);
        EnsurePending(transfer, now);
        transfer.Decide(OwnershipTransferStatus.Declined, now);
        Touch();
    }

    public void CancelOwnershipTransfer(Guid actorParentId, Guid transferId, DateTime now)
    {
        RequireOwner(actorParentId);
        var transfer = FindTransfer(transferId);
        if (transfer.FromParentId != actorParentId) throw new BusinessException(FamilyErrorCodes.InsufficientRole);
        EnsurePending(transfer, now);
        transfer.Decide(OwnershipTransferStatus.Cancelled, now);
        Touch();
    }

    // Completion belongs to the future device-safety coordinator; no archive API is exposed in Phase 1.
    public void BeginArchiving(Guid actorParentId, DateTime now)
    {
        if (Status == FamilyStatus.Archiving && RequireMember(actorParentId).Role == FamilyMemberRole.Owner) return;
        RequireOwner(actorParentId);
        Status = FamilyStatus.Archiving;
        foreach (var transfer in _ownershipTransfers.Where(x => x.Status == OwnershipTransferStatus.Pending))
            transfer.Decide(OwnershipTransferStatus.Cancelled, now);
        Touch();
    }

    private FamilyMember FindMember(Guid parentId) => _members.SingleOrDefault(x => x.ParentId == parentId)
        ?? throw new BusinessException(FamilyErrorCodes.MembershipNotFound);
    private OwnershipTransfer FindTransfer(Guid id) => _ownershipTransfers.SingleOrDefault(x => x.Id == id)
        ?? throw new BusinessException(FamilyErrorCodes.TransferNotPending);

    private static void EnsurePending(OwnershipTransfer transfer, DateTime now)
    {
        if (transfer.Status == OwnershipTransferStatus.Expired ||
            (transfer.Status == OwnershipTransferStatus.Pending && transfer.ExpiresAt <= now))
            throw new BusinessException(FamilyErrorCodes.TransferExpired);
        if (transfer.Status != OwnershipTransferStatus.Pending) throw new BusinessException(FamilyErrorCodes.TransferNotPending);
    }

    private void CancelTransfersTo(Guid parentId, DateTime now)
    {
        foreach (var transfer in _ownershipTransfers.Where(x => x.ToParentId == parentId && x.Status == OwnershipTransferStatus.Pending))
            transfer.Decide(OwnershipTransferStatus.Cancelled, now);
    }

    private void Record(FamilyMember member, Guid actorParentId, FamilyMembershipChange change, DateTime now)
    {
        _membershipHistory.Add(new FamilyMembershipHistory(Guid.NewGuid(), member, actorParentId, change, now));
        AddLocalEvent(new FamilyMembershipChangedEvent(Id, member.ParentId, actorParentId, change, member.Role, now));
    }

    // Child-only changes must participate in the root's optimistic concurrency protocol.
    private void Touch() => Version++;
}
