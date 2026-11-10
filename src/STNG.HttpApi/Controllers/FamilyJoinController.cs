using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using STNG.Families;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Auditing;

namespace STNG.Controllers;

[ApiController, Authorize(Policy = "ParentBearer"), DisableAuditing]
[Route("api")]
public class FamilyJoinController : STNGController
{
    private readonly IFamilyJoinAppService _joins;
    public FamilyJoinController(IFamilyJoinAppService joins) => _joins = joins;

    [HttpPost("families/{id:guid}/join-credentials")]
    public Task<IssuedFamilyJoinCredentialDto> CreateCredentialAsync(Guid id, CreateFamilyJoinCredentialDto input, CancellationToken cancellationToken) =>
        _joins.CreateCredentialAsync(id, input, cancellationToken);

    [HttpGet("families/{id:guid}/join-credentials")]
    public Task<PagedResultDto<FamilyJoinCredentialDto>> GetCredentialsAsync(Guid id, [FromQuery] FamilyPageDto input, CancellationToken cancellationToken) =>
        _joins.GetCredentialsAsync(id, input, cancellationToken);

    [HttpPost("families/{id:guid}/join-credentials/{credentialId:guid}/revoke")]
    public Task RevokeCredentialAsync(Guid id, Guid credentialId, FamilyMutationDto input, CancellationToken cancellationToken) =>
        _joins.RevokeCredentialAsync(id, credentialId, input, cancellationToken);

    [HttpPost("family-join-requests"), EnableRateLimiting(FamilyJoinConsts.SubmissionRateLimitPolicy)]
    public Task<FamilyJoinRequestDto> SubmitAsync(SubmitFamilyJoinRequestDto input, CancellationToken cancellationToken) =>
        _joins.SubmitAsync(input, cancellationToken);

    [HttpGet("me/family-join-requests")]
    public Task<PagedResultDto<FamilyJoinRequestDto>> GetMineAsync([FromQuery] FamilyJoinRequestPageDto input, CancellationToken cancellationToken) =>
        _joins.GetMineAsync(input, cancellationToken);

    [HttpGet("families/{id:guid}/join-requests")]
    public Task<PagedResultDto<FamilyJoinRequestDto>> GetRequestsAsync(Guid id, [FromQuery] FamilyJoinRequestPageDto input, CancellationToken cancellationToken) =>
        _joins.GetRequestsAsync(id, input, cancellationToken);

    [HttpPost("families/{id:guid}/join-requests/{requestId:guid}/approve")]
    public Task<FamilyJoinRequestDto> ApproveAsync(Guid id, Guid requestId, FamilyMutationDto input, CancellationToken cancellationToken) =>
        _joins.ApproveAsync(id, requestId, input, cancellationToken);

    [HttpPost("families/{id:guid}/join-requests/{requestId:guid}/reject")]
    public Task<FamilyJoinRequestDto> RejectAsync(Guid id, Guid requestId, RejectFamilyJoinRequestDto input, CancellationToken cancellationToken) =>
        _joins.RejectAsync(id, requestId, input, cancellationToken);

    [HttpPost("family-join-requests/{requestId:guid}/cancel")]
    public Task<FamilyJoinRequestDto> CancelAsync(Guid requestId, FamilyMutationDto input, CancellationToken cancellationToken) =>
        _joins.CancelAsync(requestId, input, cancellationToken);
}
