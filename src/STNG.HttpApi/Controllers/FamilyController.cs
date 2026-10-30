using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using STNG.Families;
using Volo.Abp.Application.Dtos;

namespace STNG.Controllers;

[ApiController, Authorize(Policy = "ParentBearer")]
[Route("api/families")]
public class FamilyController : STNGController
{
    private readonly IFamilyAppService _families;
    public FamilyController(IFamilyAppService families) => _families = families;

    [HttpPost]
    public Task<FamilyDto> CreateAsync(CreateFamilyDto input, CancellationToken cancellationToken) =>
        _families.CreateAsync(input, cancellationToken);

    [HttpGet("mine")]
    public Task<PagedResultDto<FamilyDto>> GetMineAsync([FromQuery] FamilyPageDto input, CancellationToken cancellationToken) =>
        _families.GetMyFamiliesAsync(input, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<FamilyDto> GetAsync(Guid id, CancellationToken cancellationToken) => _families.GetAsync(id, cancellationToken);

    [HttpPut("{id:guid}")]
    public Task<FamilyDto> UpdateAsync(Guid id, UpdateFamilyDto input, CancellationToken cancellationToken) =>
        _families.UpdateAsync(id, input, cancellationToken);

    [HttpGet("{id:guid}/members")]
    public Task<PagedResultDto<FamilyMemberDto>> GetMembersAsync(Guid id, [FromQuery] FamilyPageDto input, CancellationToken cancellationToken) =>
        _families.GetMembersAsync(id, input, cancellationToken);

    [HttpPost("{id:guid}/leave")]
    public Task LeaveAsync(Guid id, FamilyMutationDto input, CancellationToken cancellationToken) =>
        _families.LeaveAsync(id, input, cancellationToken);

    [HttpPost("{id:guid}/members/{parentId:guid}/remove")]
    public Task<FamilyDto> RemoveMemberAsync(Guid id, Guid parentId, FamilyMutationDto input, CancellationToken cancellationToken) =>
        _families.RemoveMemberAsync(id, parentId, input, cancellationToken);

    [HttpPost("{id:guid}/members/{parentId:guid}/promote")]
    public Task<FamilyDto> PromoteMemberAsync(Guid id, Guid parentId, FamilyMutationDto input, CancellationToken cancellationToken) =>
        _families.PromoteMemberAsync(id, parentId, input, cancellationToken);

    [HttpPost("{id:guid}/members/{parentId:guid}/demote")]
    public Task<FamilyDto> DemoteAdminAsync(Guid id, Guid parentId, FamilyMutationDto input, CancellationToken cancellationToken) =>
        _families.DemoteAdminAsync(id, parentId, input, cancellationToken);

    [HttpPost("{id:guid}/ownership-transfers")]
    public Task<OwnershipTransferDto> InitiateTransferAsync(Guid id, InitiateOwnershipTransferDto input, CancellationToken cancellationToken) =>
        _families.InitiateOwnershipTransferAsync(id, input, cancellationToken);

    [HttpGet("{id:guid}/ownership-transfers")]
    public Task<PagedResultDto<OwnershipTransferDto>> GetTransfersAsync(Guid id, [FromQuery] FamilyPageDto input, CancellationToken cancellationToken) =>
        _families.GetOwnershipTransfersAsync(id, input, cancellationToken);

    [HttpPost("{id:guid}/ownership-transfers/{transferId:guid}/accept")]
    public Task<FamilyDto> AcceptTransferAsync(Guid id, Guid transferId, FamilyMutationDto input, CancellationToken cancellationToken) =>
        _families.AcceptOwnershipTransferAsync(id, transferId, input, cancellationToken);

    [HttpPost("{id:guid}/ownership-transfers/{transferId:guid}/decline")]
    public Task<FamilyDto> DeclineTransferAsync(Guid id, Guid transferId, FamilyMutationDto input, CancellationToken cancellationToken) =>
        _families.DeclineOwnershipTransferAsync(id, transferId, input, cancellationToken);

    [HttpPost("{id:guid}/ownership-transfers/{transferId:guid}/cancel")]
    public Task<FamilyDto> CancelTransferAsync(Guid id, Guid transferId, FamilyMutationDto input, CancellationToken cancellationToken) =>
        _families.CancelOwnershipTransferAsync(id, transferId, input, cancellationToken);
}
