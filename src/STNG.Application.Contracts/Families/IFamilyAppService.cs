using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace STNG.Families;

public interface IFamilyAppService : IApplicationService
{
    Task<FamilyDto> CreateAsync(CreateFamilyDto input, CancellationToken cancellationToken = default);
    Task<PagedResultDto<FamilyDto>> GetMyFamiliesAsync(FamilyPageDto input, CancellationToken cancellationToken = default);
    Task<FamilyDto> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<FamilyDto> UpdateAsync(Guid id, UpdateFamilyDto input, CancellationToken cancellationToken = default);
    Task<PagedResultDto<FamilyMemberDto>> GetMembersAsync(Guid id, FamilyPageDto input, CancellationToken cancellationToken = default);
    Task LeaveAsync(Guid id, FamilyMutationDto input, CancellationToken cancellationToken = default);
    Task<FamilyDto> RemoveMemberAsync(Guid id, Guid parentId, FamilyMutationDto input, CancellationToken cancellationToken = default);
    Task<FamilyDto> PromoteMemberAsync(Guid id, Guid parentId, FamilyMutationDto input, CancellationToken cancellationToken = default);
    Task<FamilyDto> DemoteAdminAsync(Guid id, Guid parentId, FamilyMutationDto input, CancellationToken cancellationToken = default);
    Task<OwnershipTransferDto> InitiateOwnershipTransferAsync(Guid id, InitiateOwnershipTransferDto input, CancellationToken cancellationToken = default);
    Task<PagedResultDto<OwnershipTransferDto>> GetOwnershipTransfersAsync(Guid id, FamilyPageDto input, CancellationToken cancellationToken = default);
    Task<FamilyDto> AcceptOwnershipTransferAsync(Guid id, Guid transferId, FamilyMutationDto input, CancellationToken cancellationToken = default);
    Task<FamilyDto> DeclineOwnershipTransferAsync(Guid id, Guid transferId, FamilyMutationDto input, CancellationToken cancellationToken = default);
    Task<FamilyDto> CancelOwnershipTransferAsync(Guid id, Guid transferId, FamilyMutationDto input, CancellationToken cancellationToken = default);
}
