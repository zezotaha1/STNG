using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using STNG.FileManagement;
using STNG.Permissions;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Content;

namespace STNG.Controllers.FileManagement;

[ApiController]
[Route("api/files")]
public class FileManagementController : STNGController
{
    private readonly IFileManagementAppService _files;

    public FileManagementController(IFileManagementAppService files)
    {
        _files = files;
    }

    [HttpPost]
    [Authorize(STNGPermissions.Files.Upload)]
    public Task<FileAssetDto> UploadAsync([FromForm] UploadFileRequest input) =>
        _files.UploadAsync(new UploadFileInput
        {
            Content = new RemoteStreamContent(
                input.File.OpenReadStream(),
                input.File.FileName,
                input.File.ContentType,
                input.File.Length,
                disposeStream: true),
            OwnerUserId = input.OwnerUserId
        });

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadAsync(Guid id)
    {
        var content = await _files.DownloadAsync(id);
        return File(content.GetStream(), content.ContentType ?? "application/octet-stream", content.FileName, enableRangeProcessing: true);
    }

    [HttpGet("{id:guid}/metadata")]
    [AllowAnonymous]
    public Task<FileAssetDto> GetAsync(Guid id) => _files.GetAsync(id);

    [HttpGet]
    [AllowAnonymous]
    public Task<PagedResultDto<FileAssetDto>> GetListAsync([FromQuery] GetFileListInput input) => _files.GetListAsync(input);

    [HttpPut("{id:guid}/content")]
    [Authorize(STNGPermissions.Files.Replace)]
    public Task<FileAssetDto> ReplaceAsync(Guid id, [FromForm] ReplaceFileRequest input) =>
        _files.ReplaceAsync(id, new ReplaceFileContentInput
        {
            Content = new RemoteStreamContent(
                input.File.OpenReadStream(),
                input.File.FileName,
                input.File.ContentType,
                input.File.Length,
                disposeStream: true)
        });

    [HttpPost("{id:guid}/references")]
    [Authorize(STNGPermissions.Files.Attach)]
    public Task<FileReferenceDto> AttachAsync(Guid id, FileReferenceInput input) => _files.AttachAsync(id, input);

    [HttpDelete("{id:guid}/references/{referenceId:guid}")]
    [Authorize(STNGPermissions.Files.Detach)]
    public Task DetachAsync(Guid id, Guid referenceId) => _files.DetachAsync(id, referenceId);

    [HttpPut("{id:guid}/visibility")]
    [Authorize(STNGPermissions.Files.ChangeVisibility)]
    public Task<FileAssetDto> ChangeVisibilityAsync(Guid id, ChangeFileVisibilityInput input) =>
        _files.ChangeVisibilityAsync(id, input);

    [HttpDelete("{id:guid}")]
    [Authorize(STNGPermissions.Files.Delete)]
    public Task DeleteAsync(Guid id) => _files.DeleteAsync(id);

    [HttpPost("maintenance/cleanup-incomplete")]
    [Authorize(STNGPermissions.Files.Maintenance)]
    public Task<FileCleanupResultDto> CleanupIncompleteAsync() => _files.CleanupIncompleteAsync();
}

public class UploadFileRequest
{
    [Required]
    public IFormFile File { get; set; } = null!;
    public Guid? OwnerUserId { get; set; }
}

public class ReplaceFileRequest
{
    [Required]
    public IFormFile File { get; set; } = null!;
}
