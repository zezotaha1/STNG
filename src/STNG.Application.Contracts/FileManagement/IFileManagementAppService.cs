using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace STNG.FileManagement;

public interface IFileManagementAppService : IApplicationService
{
    Task<FileAssetDto> UploadAsync(UploadFileInput input);
    Task<FileAssetDto> GetAsync(Guid id);
    Task<IRemoteStreamContent> DownloadAsync(Guid id);
    Task<PagedResultDto<FileAssetDto>> GetListAsync(GetFileListInput input);
    Task<FileAssetDto> ReplaceAsync(Guid id, ReplaceFileContentInput input);
    Task<FileReferenceDto> AttachAsync(Guid id, FileReferenceInput input);
    Task DetachAsync(Guid id, Guid referenceId);
    Task DeleteAsync(Guid id);
    Task<FileAssetDto> ChangeVisibilityAsync(Guid id, ChangeFileVisibilityInput input);
    Task<FileCleanupResultDto> CleanupIncompleteAsync();
}
