using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using STNG.Parents;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;
using Volo.Abp.Users;

namespace STNG.Families;

[Authorize, RemoteService(false), UnitOfWork(true)]
public class FamilyAppService : STNGAppService, IFamilyAppService
{
    private readonly IRepository<Family, Guid> _families;
    private readonly FamilyManager _manager;
    private readonly OwnershipTransferManager _ownership;
    private readonly IUnitOfWorkManager _units;

    public FamilyAppService(IRepository<Family, Guid> families, FamilyManager manager,
        OwnershipTransferManager ownership, IUnitOfWorkManager units)
    {
        _families = families;
        _manager = manager;
        _ownership = ownership;
        _units = units;
    }

    public virtual async Task<FamilyDto> CreateAsync(CreateFamilyDto input, CancellationToken cancellationToken = default)
    {
        var actor = await CurrentParentAsync(cancellationToken);
        var query = await _families.WithDetailsAsync();
        var existing = await AsyncExecuter.SingleOrDefaultAsync(query.Where(x =>
            x.CreatedByParentId == actor.Id && x.CreationRequestId == input.RequestId), cancellationToken);
        if (existing != null)
        {
            existing.EnsureActive();
            existing.RequireMember(actor.Id);
            return Map(existing, actor.Id);
        }
        var family = await _manager.CreateAsync(actor.Id, new FamilyName(input.Name), input.RequestId, cancellationToken);
        await _families.InsertAsync(family, cancellationToken: cancellationToken);
        await SaveAsync(cancellationToken);
        return Map(family, actor.Id);
    }

    public virtual async Task<PagedResultDto<FamilyDto>> GetMyFamiliesAsync(FamilyPageDto input, CancellationToken cancellationToken = default)
    {
        var actor = await CurrentParentAsync(cancellationToken);
        var query = FamilyViews(await ReadFamiliesAsync(), actor.Id).Where(x => x.Status == FamilyStatus.Active);
        var total = await AsyncExecuter.LongCountAsync(query, cancellationToken);
        var items = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.CreationTime).ThenBy(x => x.Id)
            .Skip(input.SkipCount).Take(input.MaxResultCount), cancellationToken);
        return new PagedResultDto<FamilyDto>(total, items);
    }

    public virtual async Task<FamilyDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var actor = await CurrentParentAsync(cancellationToken);
        return await AccessibleFamilyViewAsync(id, actor.Id, cancellationToken);
    }

    public virtual Task<FamilyDto> UpdateAsync(Guid id, UpdateFamilyDto input, CancellationToken cancellationToken = default) =>
        ApplyAsync(id, input, (family, actor) => family.Rename(actor, new FamilyName(input.Name)), cancellationToken);

    public virtual async Task<PagedResultDto<FamilyMemberDto>> GetMembersAsync(Guid id, FamilyPageDto input,
        CancellationToken cancellationToken = default)
    {
        var actor = await CurrentParentAsync(cancellationToken);
        await AccessibleFamilyViewAsync(id, actor.Id, cancellationToken);
        var members = (await ReadFamiliesAsync()).Where(x => x.Id == id && x.Status == FamilyStatus.Active &&
            x.Members.Any(m => m.ParentId == actor.Id && m.Status == FamilyMemberStatus.Active)).SelectMany(x => x.Members);
        var total = await AsyncExecuter.LongCountAsync(members, cancellationToken);
        var items = await AsyncExecuter.ToListAsync(members.OrderBy(x => x.JoinedAt).ThenBy(x => x.Id)
            .Skip(input.SkipCount).Take(input.MaxResultCount).Select(x => new FamilyMemberDto
            {
                Id = x.Id, ParentId = x.ParentId, Role = x.Role, Status = x.Status,
                JoinedAt = x.JoinedAt, LeftAt = x.LeftAt, RemovedByParentId = x.RemovedByParentId
            }), cancellationToken);
        return new PagedResultDto<FamilyMemberDto>(total, items);
    }

    public virtual async Task LeaveAsync(Guid id, FamilyMutationDto input, CancellationToken cancellationToken = default)
    {
        var actor = await CurrentParentAsync(cancellationToken);
        var family = await AccessibleFamilyAsync(id, actor.Id, cancellationToken);
        CheckStamp(family, input);
        family.Leave(actor.Id, Clock.Now);
        await SaveAsync(cancellationToken);
    }

    public virtual Task<FamilyDto> RemoveMemberAsync(Guid id, Guid parentId, FamilyMutationDto input,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(id, input, (family, actor) => family.RemoveMember(actor, parentId, Clock.Now), cancellationToken);

    public virtual Task<FamilyDto> PromoteMemberAsync(Guid id, Guid parentId, FamilyMutationDto input,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(id, input, (family, actor) => family.PromoteToAdmin(actor, parentId, Clock.Now), cancellationToken);

    public virtual Task<FamilyDto> DemoteAdminAsync(Guid id, Guid parentId, FamilyMutationDto input,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(id, input, (family, actor) => family.DemoteAdmin(actor, parentId, Clock.Now), cancellationToken);

    public virtual async Task<OwnershipTransferDto> InitiateOwnershipTransferAsync(Guid id, InitiateOwnershipTransferDto input,
        CancellationToken cancellationToken = default)
    {
        var actor = await CurrentParentAsync(cancellationToken);
        var family = await AccessibleFamilyAsync(id, actor.Id, cancellationToken);
        var existing = family.OwnershipTransfers.SingleOrDefault(x => x.Id == input.RequestId);
        if (existing != null)
        {
            if (existing.FromParentId != actor.Id || existing.ToParentId != input.ToParentId)
                throw new BusinessException(FamilyErrorCodes.TransferConflict);
            return MapTransfer(existing, family.ConcurrencyStamp);
        }
        CheckStamp(family, input);
        var transfer = await _ownership.InitiateAsync(family, actor.Id, input.RequestId, input.ToParentId, cancellationToken);
        await SaveAsync(cancellationToken);
        return MapTransfer(transfer, family.ConcurrencyStamp);
    }

    public virtual async Task<PagedResultDto<OwnershipTransferDto>> GetOwnershipTransfersAsync(Guid id, FamilyPageDto input,
        CancellationToken cancellationToken = default)
    {
        var actor = await CurrentParentAsync(cancellationToken);
        await AccessibleFamilyViewAsync(id, actor.Id, cancellationToken);
        var query = from family in await ReadFamiliesAsync()
                    from membership in family.Members
                    from transfer in family.OwnershipTransfers
                    where family.Id == id && family.Status == FamilyStatus.Active &&
                          membership.ParentId == actor.Id && membership.Status == FamilyMemberStatus.Active &&
                          (membership.Role == FamilyMemberRole.Owner || transfer.FromParentId == actor.Id || transfer.ToParentId == actor.Id)
                    select new OwnershipTransferDto
                    {
                        Id = transfer.Id, FromParentId = transfer.FromParentId, ToParentId = transfer.ToParentId,
                        Status = transfer.Status, RequestedAt = transfer.RequestedAt, ExpiresAt = transfer.ExpiresAt,
                        AcceptedAt = transfer.AcceptedAt, CancelledAt = transfer.CancelledAt,
                        FamilyConcurrencyStamp = family.ConcurrencyStamp
                    };
        var total = await AsyncExecuter.LongCountAsync(query, cancellationToken);
        var items = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.RequestedAt).ThenBy(x => x.Id)
            .Skip(input.SkipCount).Take(input.MaxResultCount), cancellationToken);
        return new PagedResultDto<OwnershipTransferDto>(total, items);
    }

    public virtual async Task<FamilyDto> AcceptOwnershipTransferAsync(Guid id, Guid transferId, FamilyMutationDto input,
        CancellationToken cancellationToken = default)
    {
        var actor = await CurrentParentAsync(cancellationToken);
        var family = await AccessibleFamilyAsync(id, actor.Id, cancellationToken);
        var transfer = family.OwnershipTransfers.SingleOrDefault(x => x.Id == transferId);
        if (transfer?.Status != OwnershipTransferStatus.Accepted || transfer.ToParentId != actor.Id)
            CheckStamp(family, input);
        await _ownership.AcceptAsync(family, actor.Id, transferId, cancellationToken);
        await SaveAsync(cancellationToken);
        return Map(family, actor.Id);
    }

    public virtual Task<FamilyDto> DeclineOwnershipTransferAsync(Guid id, Guid transferId, FamilyMutationDto input,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(id, input, (family, actor) => family.DeclineOwnershipTransfer(actor, transferId, Clock.Now), cancellationToken);

    public virtual Task<FamilyDto> CancelOwnershipTransferAsync(Guid id, Guid transferId, FamilyMutationDto input,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(id, input, (family, actor) => family.CancelOwnershipTransfer(actor, transferId, Clock.Now), cancellationToken);

    private async Task<FamilyDto> ApplyAsync(Guid id, FamilyMutationDto input, Action<Family, Guid> change,
        CancellationToken cancellationToken)
    {
        var actor = await CurrentParentAsync(cancellationToken);
        var family = await AccessibleFamilyAsync(id, actor.Id, cancellationToken);
        CheckStamp(family, input);
        change(family, actor.Id);
        await SaveAsync(cancellationToken);
        return Map(family, actor.Id);
    }

    private Task<Parent> CurrentParentAsync(CancellationToken cancellationToken) =>
        _manager.GetParentByUserIdAsync(CurrentUser.GetId(), cancellationToken);

    private async Task<IQueryable<Family>> ReadFamiliesAsync()
    {
        // ABP adds AsNoTracking to the returned query; restore the repository's tracking
        // setting immediately so mutation queries continue to load tracked aggregates.
        using (_families.DisableTracking())
            return await _families.GetQueryableAsync();
    }

    private static IQueryable<FamilyDto> FamilyViews(IQueryable<Family> families, Guid parentId) =>
        from family in families
        from membership in family.Members
        where membership.ParentId == parentId && membership.Status == FamilyMemberStatus.Active
        select new FamilyDto
        {
            Id = family.Id, Name = family.Name.Value, ImageFileAssetId = family.ImageFileAssetId,
            CreatedByParentId = family.CreatedByParentId, Status = family.Status, ConcurrencyStamp = family.ConcurrencyStamp,
            CreationTime = family.CreationTime, LastModificationTime = family.LastModificationTime,
            MyRole = membership.Role
        };

    private async Task<FamilyDto> AccessibleFamilyViewAsync(Guid id, Guid parentId, CancellationToken cancellationToken)
    {
        var query = FamilyViews((await ReadFamiliesAsync()).Where(x => x.Id == id), parentId);
        var family = await AsyncExecuter.SingleOrDefaultAsync(query, cancellationToken)
            ?? throw new BusinessException(FamilyErrorCodes.NotFound);
        if (family.Status != FamilyStatus.Active)
            throw new BusinessException(family.Status == FamilyStatus.Archived ? FamilyErrorCodes.Archived : FamilyErrorCodes.NotActive);
        return family;
    }

    private async Task<Family> AccessibleFamilyAsync(Guid id, Guid parentId, CancellationToken cancellationToken)
    {
        // The same response for an unknown family and a family belonging to someone else.
        var query = await _families.WithDetailsAsync();
        var family = await AsyncExecuter.SingleOrDefaultAsync(query.Where(x => x.Id == id &&
            x.Members.Any(m => m.ParentId == parentId && m.Status == FamilyMemberStatus.Active)), cancellationToken)
            ?? throw new BusinessException(FamilyErrorCodes.NotFound);
        family.EnsureActive();
        return family;
    }

    private static void CheckStamp(Family family, FamilyMutationDto input)
    {
        if (family.ConcurrencyStamp != input.ConcurrencyStamp) throw new BusinessException(FamilyErrorCodes.ConcurrencyConflict);
    }

    private Task SaveAsync(CancellationToken cancellationToken) => _units.Current!.SaveChangesAsync(cancellationToken);

    private static FamilyDto Map(Family family, Guid parentId) => new()
    {
        Id = family.Id, Name = family.Name.Value, ImageFileAssetId = family.ImageFileAssetId,
        CreatedByParentId = family.CreatedByParentId, Status = family.Status, ConcurrencyStamp = family.ConcurrencyStamp,
        CreationTime = family.CreationTime, LastModificationTime = family.LastModificationTime,
        MyRole = family.RequireMember(parentId).Role
    };

    private static OwnershipTransferDto MapTransfer(OwnershipTransfer transfer, string stamp) => new()
    {
        Id = transfer.Id, FromParentId = transfer.FromParentId, ToParentId = transfer.ToParentId, Status = transfer.Status,
        RequestedAt = transfer.RequestedAt, ExpiresAt = transfer.ExpiresAt, AcceptedAt = transfer.AcceptedAt,
        CancelledAt = transfer.CancelledAt, FamilyConcurrencyStamp = stamp
    };
}
