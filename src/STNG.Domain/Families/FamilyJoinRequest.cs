using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace STNG.Families;

// Retained mutable aggregate. Review and membership activation share a transaction.
public class FamilyJoinRequest : AuditedAggregateRoot<Guid>
{
    public Guid FamilyId { get; private set; }
    public Guid ParentId { get; private set; }
    public Guid CredentialId { get; private set; }
    public FamilyJoinRequestStatus Status { get; private set; }
    public DateTime RequestedAt { get; private set; }
    public DateTime? ReviewedAt { get; private set; }
    public Guid? ReviewedByParentId { get; private set; }
    public string? RejectionReason { get; private set; }

    private FamilyJoinRequest() { }
    internal FamilyJoinRequest(Guid id, Guid familyId, Guid parentId, Guid credentialId, DateTime now) : base(id)
    {
        if (id == Guid.Empty || familyId == Guid.Empty || parentId == Guid.Empty || credentialId == Guid.Empty)
            throw new BusinessException(FamilyJoinErrorCodes.InvalidInput);
        FamilyId = familyId; ParentId = parentId; CredentialId = credentialId;
        RequestedAt = now; Status = FamilyJoinRequestStatus.Pending;
        AddLocalEvent(new FamilyJoinRequestChangedEvent(FamilyId, Id, ParentId, Status, now));
    }

    public void EnsureOtherReviewer(Guid reviewerParentId)
    {
        if (reviewerParentId == ParentId) throw new BusinessException(FamilyJoinErrorCodes.CannotReviewOwnRequest);
        if (reviewerParentId == Guid.Empty) throw new BusinessException(FamilyJoinErrorCodes.InvalidInput);
    }

    internal void Approve(Guid reviewerParentId, DateTime now)
    {
        EnsureOtherReviewer(reviewerParentId);
        if (Status == FamilyJoinRequestStatus.Approved) return;
        EnsurePending();
        Decide(FamilyJoinRequestStatus.Approved, reviewerParentId, now);
    }

    internal void Reject(Guid reviewerParentId, string? reason, DateTime now)
    {
        EnsureOtherReviewer(reviewerParentId);
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (reason?.Length > FamilyJoinConsts.MaxRejectionReasonLength) throw new BusinessException(FamilyJoinErrorCodes.InvalidInput);
        if (Status == FamilyJoinRequestStatus.Rejected && RejectionReason == reason) return;
        EnsurePending();
        RejectionReason = reason;
        Decide(FamilyJoinRequestStatus.Rejected, reviewerParentId, now);
    }

    internal void Cancel(Guid requesterParentId, DateTime now)
    {
        if (requesterParentId != ParentId) throw new BusinessException(FamilyErrorCodes.NotFound);
        if (Status == FamilyJoinRequestStatus.Cancelled) return;
        EnsurePending();
        Decide(FamilyJoinRequestStatus.Cancelled, null, now);
    }

    internal void CancelForRevocation(Guid reviewerParentId, DateTime now)
    {
        EnsurePending();
        Decide(FamilyJoinRequestStatus.Cancelled, reviewerParentId, now);
    }

    internal void EnsurePending()
    {
        if (Status != FamilyJoinRequestStatus.Pending) throw new BusinessException(FamilyJoinErrorCodes.InvalidTransition);
    }

    private void Decide(FamilyJoinRequestStatus status, Guid? reviewer, DateTime now)
    {
        Status = status; ReviewedByParentId = reviewer; ReviewedAt = now;
        AddLocalEvent(new FamilyJoinRequestChangedEvent(FamilyId, Id, ParentId, Status, now));
    }
}

public sealed record FamilyJoinRequestChangedEvent(Guid FamilyId, Guid RequestId, Guid ParentId, FamilyJoinRequestStatus Status, DateTime OccurredAt);
