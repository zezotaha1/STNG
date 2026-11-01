using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace STNG.Families;

public interface IFamilyJoinAppService : IApplicationService
{
    Task<IssuedFamilyJoinCredentialDto> CreateCredentialAsync(Guid id, CreateFamilyJoinCredentialDto input, CancellationToken cancellationToken = default);
    Task<PagedResultDto<FamilyJoinCredentialDto>> GetCredentialsAsync(Guid id, FamilyPageDto input, CancellationToken cancellationToken = default);
    Task RevokeCredentialAsync(Guid id, Guid credentialId, FamilyMutationDto input, CancellationToken cancellationToken = default);
    Task<FamilyJoinRequestDto> SubmitAsync(SubmitFamilyJoinRequestDto input, CancellationToken cancellationToken = default);
    Task<PagedResultDto<FamilyJoinRequestDto>> GetMineAsync(FamilyJoinRequestPageDto input, CancellationToken cancellationToken = default);
    Task<PagedResultDto<FamilyJoinRequestDto>> GetRequestsAsync(Guid id, FamilyJoinRequestPageDto input, CancellationToken cancellationToken = default);
    Task<FamilyJoinRequestDto> ApproveAsync(Guid id, Guid requestId, FamilyMutationDto input, CancellationToken cancellationToken = default);
    Task<FamilyJoinRequestDto> RejectAsync(Guid id, Guid requestId, RejectFamilyJoinRequestDto input, CancellationToken cancellationToken = default);
    Task<FamilyJoinRequestDto> CancelAsync(Guid requestId, FamilyMutationDto input, CancellationToken cancellationToken = default);
}
