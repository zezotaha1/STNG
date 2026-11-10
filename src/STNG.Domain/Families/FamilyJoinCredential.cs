using System;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities.Auditing;

namespace STNG.Families;

// Retained security aggregate: revoke/expire instead of deleting. No soft-delete lifecycle.
[DisableAuditing]
public class FamilyJoinCredential : AuditedAggregateRoot<Guid>
{
    public Guid FamilyId { get; private set; }
    public FamilyJoinCredentialKind Kind { get; private set; }
    public string SecretHash { get; private set; } = null!;
    public FamilyJoinCredentialStatus Status { get; private set; }
    public Guid CreatedByParentId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public Guid? RevokedByParentId { get; private set; }

    private FamilyJoinCredential() { }

    internal FamilyJoinCredential(Guid id, Guid familyId, Guid creatorParentId, FamilyJoinCredentialKind kind,
        string hash, DateTime now, DateTime expiresAt) : base(id)
    {
        if (id == Guid.Empty || familyId == Guid.Empty || creatorParentId == Guid.Empty || !Enum.IsDefined(kind) ||
            hash == null || hash.Length != FamilyJoinConsts.HashLength || !System.Linq.Enumerable.All(hash, Uri.IsHexDigit) || expiresAt <= now)
            throw new BusinessException(FamilyJoinErrorCodes.InvalidInput);
        FamilyId = familyId; CreatedByParentId = creatorParentId; Kind = kind;
        SecretHash = hash; CreatedAt = now; ExpiresAt = expiresAt;
        Status = FamilyJoinCredentialStatus.Active;
        AddLocalEvent(new FamilyJoinCredentialChangedEvent(FamilyId, Id, Status, now));
    }

    public bool CanSubmit(DateTime now) => Status == FamilyJoinCredentialStatus.Active && now < ExpiresAt;

    internal void Expire(DateTime now)
    {
        if (Status != FamilyJoinCredentialStatus.Active || now < ExpiresAt)
            throw new BusinessException(FamilyJoinErrorCodes.InvalidTransition);
        Status = FamilyJoinCredentialStatus.Expired;
        AddLocalEvent(new FamilyJoinCredentialChangedEvent(FamilyId, Id, Status, now));
    }

    internal void Revoke(Guid actorParentId, DateTime now)
    {
        if (actorParentId == Guid.Empty) throw new BusinessException(FamilyJoinErrorCodes.InvalidInput);
        if (Status == FamilyJoinCredentialStatus.Revoked) return;
        Status = FamilyJoinCredentialStatus.Revoked;
        RevokedAt = now; RevokedByParentId = actorParentId;
        AddLocalEvent(new FamilyJoinCredentialChangedEvent(FamilyId, Id, Status, now));
    }
}

public sealed record FamilyJoinCredentialChangedEvent(Guid FamilyId, Guid CredentialId, FamilyJoinCredentialStatus Status, DateTime OccurredAt);
