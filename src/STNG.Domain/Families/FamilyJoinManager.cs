using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace STNG.Families;

public sealed record IssuedFamilyJoinCredential(FamilyJoinCredential Credential, string? Secret);

public class FamilyJoinManager : DomainService
{
    private readonly IRepository<Family, Guid> _families;
    private readonly IRepository<FamilyJoinCredential, Guid> _credentials;
    private readonly IRepository<FamilyJoinRequest, Guid> _requests;
    private readonly FamilyManager _memberships;
    private readonly FamilyJoinSecrets _secrets;
    private readonly IOptions<FamilyJoinOptions> _options;

    public FamilyJoinManager(IRepository<Family, Guid> families, IRepository<FamilyJoinCredential, Guid> credentials,
        IRepository<FamilyJoinRequest, Guid> requests, FamilyManager memberships, FamilyJoinSecrets secrets,
        IOptions<FamilyJoinOptions> options)
    {
        _families = families; _credentials = credentials; _requests = requests;
        _memberships = memberships; _secrets = secrets; _options = options;
    }

    public async Task<IssuedFamilyJoinCredential> CreateCredentialAsync(Family family, Guid actorParentId,
        Guid requestId, FamilyJoinCredentialKind kind, CancellationToken cancellationToken = default)
    {
        await _memberships.EnsureEligibleAsync(actorParentId, cancellationToken);
        family.RequireAdministrator(actorParentId);
        if (requestId == Guid.Empty || !Enum.IsDefined(kind)) throw new BusinessException(FamilyJoinErrorCodes.InvalidInput);
        var existing = await _credentials.FindAsync(requestId, cancellationToken: cancellationToken);
        if (existing != null)
        {
            if (existing.FamilyId != family.Id || existing.Kind != kind || existing.CreatedByParentId != actorParentId)
                throw new BusinessException(FamilyJoinErrorCodes.RequestConflict);
            // The original secret cannot be recovered from its hash. A retry returns
            // metadata only; a new request identity deliberately rotates the code.
            return new(existing, null);
        }

        var previous = await _credentials.FindAsync(x => x.FamilyId == family.Id && x.Kind == kind &&
            x.Status == FamilyJoinCredentialStatus.Active, cancellationToken: cancellationToken);
        if (previous != null)
        {
            if (previous.ExpiresAt <= Clock.Now) previous.Expire(Clock.Now);
            else await RevokePendingAsync(previous, actorParentId, cancellationToken);
        }
        var secret = _secrets.Generate(kind);
        if (!_secrets.TryHash(kind, secret, out var hash)) throw new InvalidOperationException("Generated join credential was invalid.");
        var lifetime = kind == FamilyJoinCredentialKind.ManualCode ? _options.Value.ManualCodeLifetime : _options.Value.QRTokenLifetime;
        var credential = new FamilyJoinCredential(requestId, family.Id, actorParentId, kind, hash, Clock.Now, Clock.Now.Add(lifetime));
        family.RecordJoinCredentialChange(actorParentId);
        await _credentials.InsertAsync(credential, cancellationToken: cancellationToken);
        return new(credential, secret);
    }

    public async Task RevokeAsync(Family family, Guid actorParentId, FamilyJoinCredential credential,
        CancellationToken cancellationToken = default)
    {
        await _memberships.EnsureEligibleAsync(actorParentId, cancellationToken);
        family.RequireAdministrator(actorParentId);
        EnsureFamily(family, credential.FamilyId);
        if (credential.Status == FamilyJoinCredentialStatus.Revoked) return;
        await RevokePendingAsync(credential, actorParentId, cancellationToken);
        family.RecordJoinCredentialChange(actorParentId);
    }

    private async Task RevokePendingAsync(FamilyJoinCredential credential, Guid actorParentId, CancellationToken cancellationToken)
    {
        credential.Revoke(actorParentId, Clock.Now);
        var pending = await _requests.GetListAsync(x => x.FamilyId == credential.FamilyId && x.CredentialId == credential.Id &&
            x.Status == FamilyJoinRequestStatus.Pending, cancellationToken: cancellationToken);
        foreach (var request in pending) request.CancelForRevocation(actorParentId, Clock.Now);
    }

    public async Task<FamilyJoinRequest> SubmitAsync(Guid actorParentId, Guid requestId, FamilyJoinCredentialKind kind,
        string secret, CancellationToken cancellationToken = default)
    {
        await _memberships.EnsureEligibleAsync(actorParentId, cancellationToken);
        if (requestId == Guid.Empty) throw new BusinessException(FamilyJoinErrorCodes.InvalidInput);
        var replay = await _requests.FindAsync(requestId, cancellationToken: cancellationToken);
        if (replay != null)
        {
            if (replay.ParentId != actorParentId) throw new BusinessException(FamilyJoinErrorCodes.Unavailable);
            var originalCredential = await ReadCredentialAsync(replay.CredentialId, cancellationToken);
            if (!_secrets.Matches(originalCredential, kind, secret)) throw new BusinessException(FamilyJoinErrorCodes.RequestConflict);
            return replay;
        }
        if (!_secrets.TryHash(kind, secret, out var hash)) throw new BusinessException(FamilyJoinErrorCodes.Unavailable);
        FamilyJoinCredential? credential;
        // A request references the credential without changing it. Keeping this
        // lookup untracked also avoids ABP navigation-change stamp updates when
        // the new request is attached through that foreign key.
        using (_credentials.DisableTracking())
            credential = await _credentials.FindAsync(x => x.Kind == kind && x.SecretHash == hash, cancellationToken: cancellationToken);
        if (credential == null || !credential.CanSubmit(Clock.Now) || !_secrets.Matches(credential, kind, secret))
            throw new BusinessException(FamilyJoinErrorCodes.Unavailable);
        var family = await _families.GetAsync(credential.FamilyId, includeDetails: true, cancellationToken: cancellationToken);
        if (family.Status != FamilyStatus.Active) throw new BusinessException(FamilyJoinErrorCodes.Unavailable);
        // The root snapshot must precede the decisive credential read. Otherwise a
        // revocation could commit between the first lookup and loading the new root
        // version, letting an older tracked credential pass the concurrency gate.
        credential = await ReadCredentialAsync(credential.Id, cancellationToken);
        if (!credential.CanSubmit(Clock.Now) || !_secrets.Matches(credential, kind, secret))
            throw new BusinessException(FamilyJoinErrorCodes.Unavailable);
        if (await _requests.AnyAsync(x => x.FamilyId == family.Id && x.ParentId == actorParentId &&
                x.Status == FamilyJoinRequestStatus.Pending, cancellationToken))
            throw new BusinessException(FamilyJoinErrorCodes.PendingRequestExists);
        var cooldownStart = Clock.Now.Subtract(_options.Value.ReapplyCooldown);
        if (_options.Value.ReapplyCooldown > TimeSpan.Zero && await _requests.AnyAsync(x => x.FamilyId == family.Id &&
                x.ParentId == actorParentId && x.Status == FamilyJoinRequestStatus.Rejected && x.ReviewedAt.HasValue &&
                x.ReviewedAt.Value > cooldownStart, cancellationToken))
            throw new BusinessException(FamilyJoinErrorCodes.AttemptsLimited);
        family.RecordJoinRequestSubmission(actorParentId);
        var request = new FamilyJoinRequest(requestId, family.Id, actorParentId, credential.Id, Clock.Now);
        await _requests.InsertAsync(request, cancellationToken: cancellationToken);
        return request;
    }

    public async Task ApproveAsync(Family family, Guid actorParentId, FamilyJoinRequest request,
        CancellationToken cancellationToken = default)
    {
        await _memberships.EnsureEligibleAsync(actorParentId, cancellationToken);
        family.RequireAdministrator(actorParentId);
        EnsureFamily(family, request.FamilyId);
        request.EnsureOtherReviewer(actorParentId);
        if (request.Status == FamilyJoinRequestStatus.Approved) return;
        request.EnsurePending();
        var credential = await ReadCredentialAsync(request.CredentialId, cancellationToken);
        EnsureFamily(family, credential.FamilyId);
        // Expiration does not invalidate a request that was valid when submitted.
        if (credential.Status == FamilyJoinCredentialStatus.Revoked)
            throw new BusinessException(FamilyJoinErrorCodes.Unavailable);
        await _memberships.AddApprovedMemberAsync(family, actorParentId, request.ParentId, cancellationToken);
        request.Approve(actorParentId, Clock.Now);
    }

    public async Task RejectAsync(Family family, Guid actorParentId, FamilyJoinRequest request, string? reason,
        CancellationToken cancellationToken = default)
    {
        await _memberships.EnsureEligibleAsync(actorParentId, cancellationToken);
        family.RequireAdministrator(actorParentId);
        EnsureFamily(family, request.FamilyId);
        var before = request.Status;
        request.Reject(actorParentId, reason, Clock.Now);
        if (before != request.Status) family.RecordJoinRequestReview(actorParentId, request.ParentId);
    }

    public async Task CancelAsync(Family family, Guid actorParentId, FamilyJoinRequest request,
        CancellationToken cancellationToken = default)
    {
        await _memberships.EnsureEligibleAsync(actorParentId, cancellationToken);
        EnsureFamily(family, request.FamilyId);
        var before = request.Status;
        request.Cancel(actorParentId, Clock.Now);
        if (before != request.Status) family.RecordJoinRequestCancellation();
    }

    private static void EnsureFamily(Family family, Guid familyId)
    {
        if (family.Id != familyId) throw new BusinessException(FamilyErrorCodes.NotFound);
    }

    private async Task<FamilyJoinCredential> ReadCredentialAsync(Guid id, CancellationToken cancellationToken)
    {
        using (_credentials.DisableTracking())
            return await _credentials.GetAsync(id, cancellationToken: cancellationToken);
    }
}
