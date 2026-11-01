using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using STNG.Parents;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;
using Volo.Abp.Users;

namespace STNG.Families;

[Authorize, RemoteService(false), UnitOfWork(true), DisableAuditing]
public class FamilyJoinAppService : STNGAppService, IFamilyJoinAppService
{
    private readonly IRepository<Family, Guid> _families;
    private readonly IRepository<FamilyJoinCredential, Guid> _credentials;
    private readonly IRepository<FamilyJoinRequest, Guid> _requests;
    private readonly FamilyManager _parents;
    private readonly FamilyJoinManager _joins;
    private readonly FamilyJoinAttemptLimiter _attempts;
    private readonly IUnitOfWorkManager _units;
    private readonly IOptions<FamilyJoinOptions> _options;

    public FamilyJoinAppService(IRepository<Family, Guid> families, IRepository<FamilyJoinCredential, Guid> credentials,
        IRepository<FamilyJoinRequest, Guid> requests, FamilyManager parents, FamilyJoinManager joins,
        FamilyJoinAttemptLimiter attempts, IUnitOfWorkManager units, IOptions<FamilyJoinOptions> options)
    {
        _families = families; _credentials = credentials; _requests = requests; _parents = parents;
        _joins = joins; _attempts = attempts; _units = units; _options = options;
    }

    public virtual async Task<IssuedFamilyJoinCredentialDto> CreateCredentialAsync(Guid id, CreateFamilyJoinCredentialDto input,
        CancellationToken cancellationToken = default)
    {
        var actor = await CurrentParentAsync(cancellationToken);
        var family = await AdministratorFamilyAsync(id, actor.Id, cancellationToken);
        var issued = await _joins.CreateCredentialAsync(family, actor.Id, input.RequestId, input.Kind, cancellationToken);
        await SaveAsync(cancellationToken);
        return new IssuedFamilyJoinCredentialDto
        {
            Credential = MapCredential(issued.Credential), Secret = issued.Secret,
            JoinLink = issued.Secret != null && input.Kind == FamilyJoinCredentialKind.QRToken
                ? _options.Value.QRJoinBaseUri.TrimEnd('/') + "?token=" + Uri.EscapeDataString(issued.Secret) : null
        };
    }

    public virtual async Task<PagedResultDto<FamilyJoinCredentialDto>> GetCredentialsAsync(Guid id, FamilyPageDto input,
        CancellationToken cancellationToken = default)
    {
        var actor = await CurrentParentAsync(cancellationToken);
        await EnsureAdministratorViewAsync(id, actor.Id, cancellationToken);
        var allowed = await AdministrativeFamiliesAsync(id, actor.Id);
        var now = Clock.Now;
        var query = from credential in await ReadCredentialsAsync()
                    join family in allowed on credential.FamilyId equals family.Id
                    select new FamilyJoinCredentialDto
                    {
                        Id = credential.Id, Kind = credential.Kind,
                        Status = credential.Status == FamilyJoinCredentialStatus.Active && credential.ExpiresAt <= now
                            ? FamilyJoinCredentialStatus.Expired : credential.Status,
                        CreatedByParentId = credential.CreatedByParentId, CreatedAt = credential.CreatedAt,
                        ExpiresAt = credential.ExpiresAt, RevokedAt = credential.RevokedAt, ConcurrencyStamp = credential.ConcurrencyStamp
                    };
        var total = await AsyncExecuter.LongCountAsync(query, cancellationToken);
        var items = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip(input.SkipCount).Take(input.MaxResultCount), cancellationToken);
        return new(total, items);
    }

    public virtual async Task RevokeCredentialAsync(Guid id, Guid credentialId, FamilyMutationDto input,
        CancellationToken cancellationToken = default)
    {
        var actor = await CurrentParentAsync(cancellationToken);
        var family = await AdministratorFamilyAsync(id, actor.Id, cancellationToken);
        var credential = await _credentials.FindAsync(x => x.Id == credentialId && x.FamilyId == id,
            cancellationToken: cancellationToken) ?? throw new BusinessException(FamilyErrorCodes.NotFound);
        if (credential.Status != FamilyJoinCredentialStatus.Revoked) CheckStamp(credential.ConcurrencyStamp, input);
        await _joins.RevokeAsync(family, actor.Id, credential, cancellationToken);
        await SaveAsync(cancellationToken);
    }

    public virtual async Task<FamilyJoinRequestDto> SubmitAsync(SubmitFamilyJoinRequestDto input, CancellationToken cancellationToken = default)
    {
        var actor = await CurrentParentAsync(cancellationToken);
        await _attempts.CheckAsync(actor.Id, cancellationToken);
        var request = await _joins.SubmitAsync(actor.Id, input.RequestId, input.Kind, input.Secret, cancellationToken);
        await SaveAsync(cancellationToken);
        return MapRequest(request);
    }

    public virtual async Task<PagedResultDto<FamilyJoinRequestDto>> GetMineAsync(FamilyJoinRequestPageDto input,
        CancellationToken cancellationToken = default)
    {
        var actor = await CurrentParentAsync(cancellationToken);
        var query = (await ReadRequestsAsync()).Where(x => x.ParentId == actor.Id);
        if (input.Status.HasValue) query = query.Where(x => x.Status == input.Status.Value);
        return await PageRequestsAsync(query, input, cancellationToken);
    }

    public virtual async Task<PagedResultDto<FamilyJoinRequestDto>> GetRequestsAsync(Guid id, FamilyJoinRequestPageDto input,
        CancellationToken cancellationToken = default)
    {
        var actor = await CurrentParentAsync(cancellationToken);
        await EnsureAdministratorViewAsync(id, actor.Id, cancellationToken);
        var allowed = await AdministrativeFamiliesAsync(id, actor.Id);
        var status = input.Status ?? FamilyJoinRequestStatus.Pending;
        var query = from request in await ReadRequestsAsync()
                    join family in allowed on request.FamilyId equals family.Id
                    where request.Status == status
                    select request;
        return await PageRequestsAsync(query, input, cancellationToken);
    }

    public virtual async Task<FamilyJoinRequestDto> ApproveAsync(Guid id, Guid requestId, FamilyMutationDto input,
        CancellationToken cancellationToken = default)
    {
        var actor = await CurrentParentAsync(cancellationToken);
        var family = await AdministratorFamilyAsync(id, actor.Id, cancellationToken);
        var request = await ScopedRequestAsync(id, requestId, cancellationToken);
        if (request.Status != FamilyJoinRequestStatus.Approved) CheckStamp(request.ConcurrencyStamp, input);
        await _joins.ApproveAsync(family, actor.Id, request, cancellationToken);
        await SaveAsync(cancellationToken);
        return MapRequest(request);
    }

    public virtual async Task<FamilyJoinRequestDto> RejectAsync(Guid id, Guid requestId, RejectFamilyJoinRequestDto input,
        CancellationToken cancellationToken = default)
    {
        var actor = await CurrentParentAsync(cancellationToken);
        var family = await AdministratorFamilyAsync(id, actor.Id, cancellationToken);
        var request = await ScopedRequestAsync(id, requestId, cancellationToken);
        var reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim();
        if (request.Status != FamilyJoinRequestStatus.Rejected || request.RejectionReason != reason) CheckStamp(request.ConcurrencyStamp, input);
        await _joins.RejectAsync(family, actor.Id, request, input.Reason, cancellationToken);
        await SaveAsync(cancellationToken);
        return MapRequest(request);
    }

    public virtual async Task<FamilyJoinRequestDto> CancelAsync(Guid requestId, FamilyMutationDto input,
        CancellationToken cancellationToken = default)
    {
        var actor = await CurrentParentAsync(cancellationToken);
        var request = await _requests.FindAsync(x => x.Id == requestId && x.ParentId == actor.Id,
            cancellationToken: cancellationToken) ?? throw new BusinessException(FamilyErrorCodes.NotFound);
        if (request.Status != FamilyJoinRequestStatus.Cancelled) CheckStamp(request.ConcurrencyStamp, input);
        var family = await _families.GetAsync(request.FamilyId, cancellationToken: cancellationToken);
        await _joins.CancelAsync(family, actor.Id, request, cancellationToken);
        await SaveAsync(cancellationToken);
        return MapRequest(request);
    }

    private Task<Parent> CurrentParentAsync(CancellationToken cancellationToken) => _parents.GetParentByUserIdAsync(CurrentUser.GetId(), cancellationToken);
    private Task SaveAsync(CancellationToken cancellationToken) => _units.Current!.SaveChangesAsync(cancellationToken);
    private async Task<IQueryable<Family>> ReadFamiliesAsync() { using (_families.DisableTracking()) return await _families.GetQueryableAsync(); }
    private async Task<IQueryable<FamilyJoinCredential>> ReadCredentialsAsync() { using (_credentials.DisableTracking()) return await _credentials.GetQueryableAsync(); }
    private async Task<IQueryable<FamilyJoinRequest>> ReadRequestsAsync() { using (_requests.DisableTracking()) return await _requests.GetQueryableAsync(); }

    private async Task<IQueryable<Family>> AdministrativeFamiliesAsync(Guid id, Guid parentId) => (await ReadFamiliesAsync())
        .Where(f => f.Id == id && f.Status == FamilyStatus.Active && f.Members.Any(m => m.ParentId == parentId &&
            m.Status == FamilyMemberStatus.Active && (m.Role == FamilyMemberRole.Owner || m.Role == FamilyMemberRole.Admin)));

    private async Task EnsureAdministratorViewAsync(Guid id, Guid parentId, CancellationToken cancellationToken)
    {
        var query = from family in await ReadFamiliesAsync()
                    from member in family.Members
                    where family.Id == id && member.ParentId == parentId && member.Status == FamilyMemberStatus.Active
                    select new { family.Status, member.Role };
        var access = await AsyncExecuter.SingleOrDefaultAsync(query, cancellationToken)
            ?? throw new BusinessException(FamilyErrorCodes.NotFound);
        if (access.Status != FamilyStatus.Active)
            throw new BusinessException(access.Status == FamilyStatus.Archived ? FamilyErrorCodes.Archived : FamilyErrorCodes.NotActive);
        if (access.Role != FamilyMemberRole.Owner && access.Role != FamilyMemberRole.Admin)
            throw new BusinessException(FamilyErrorCodes.InsufficientRole);
    }

    private async Task<Family> AdministratorFamilyAsync(Guid id, Guid parentId, CancellationToken cancellationToken)
    {
        var query = await _families.WithDetailsAsync();
        var family = await AsyncExecuter.SingleOrDefaultAsync(query.Where(f => f.Id == id &&
            f.Members.Any(m => m.ParentId == parentId && m.Status == FamilyMemberStatus.Active)), cancellationToken)
            ?? throw new BusinessException(FamilyErrorCodes.NotFound);
        family.RequireAdministrator(parentId);
        return family;
    }

    private async Task<FamilyJoinRequest> ScopedRequestAsync(Guid id, Guid requestId, CancellationToken cancellationToken) =>
        await _requests.FindAsync(x => x.Id == requestId && x.FamilyId == id, cancellationToken: cancellationToken)
        ?? throw new BusinessException(FamilyErrorCodes.NotFound);

    private async Task<PagedResultDto<FamilyJoinRequestDto>> PageRequestsAsync(IQueryable<FamilyJoinRequest> query,
        FamilyPageDto input, CancellationToken cancellationToken)
    {
        var total = await AsyncExecuter.LongCountAsync(query, cancellationToken);
        var items = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.RequestedAt).ThenBy(x => x.Id)
            .Skip(input.SkipCount).Take(input.MaxResultCount).Select(x => new FamilyJoinRequestDto
            {
                Id = x.Id, FamilyId = x.FamilyId, ParentId = x.ParentId, CredentialId = x.CredentialId, Status = x.Status,
                RequestedAt = x.RequestedAt, ReviewedAt = x.ReviewedAt, ReviewedByParentId = x.ReviewedByParentId,
                RejectionReason = x.RejectionReason, ConcurrencyStamp = x.ConcurrencyStamp
            }), cancellationToken);
        return new(total, items);
    }

    private static void CheckStamp(string stamp, FamilyMutationDto input)
    {
        if (stamp != input.ConcurrencyStamp) throw new BusinessException(FamilyErrorCodes.ConcurrencyConflict);
    }
    private FamilyJoinCredentialDto MapCredential(FamilyJoinCredential x) => new()
    {
        Id = x.Id, Kind = x.Kind, Status = x.Status == FamilyJoinCredentialStatus.Active && x.ExpiresAt <= Clock.Now
            ? FamilyJoinCredentialStatus.Expired : x.Status,
        CreatedByParentId = x.CreatedByParentId, CreatedAt = x.CreatedAt, ExpiresAt = x.ExpiresAt,
        RevokedAt = x.RevokedAt, ConcurrencyStamp = x.ConcurrencyStamp
    };
    private static FamilyJoinRequestDto MapRequest(FamilyJoinRequest x) => new()
    {
        Id = x.Id, FamilyId = x.FamilyId, ParentId = x.ParentId, CredentialId = x.CredentialId, Status = x.Status,
        RequestedAt = x.RequestedAt, ReviewedAt = x.ReviewedAt, ReviewedByParentId = x.ReviewedByParentId,
        RejectionReason = x.RejectionReason, ConcurrencyStamp = x.ConcurrencyStamp
    };
}
