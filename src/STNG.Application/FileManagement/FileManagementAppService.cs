using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using STNG.Permissions;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Threading;
using Volo.Abp.Uow;
using Volo.Abp.Users;

namespace STNG.FileManagement;

[RemoteService(false)]
public class FileManagementAppService : STNGAppService, IFileManagementAppService
{
    private readonly IRepository<FileAsset, Guid> _files;
    private readonly IFileStorage _storage;
    private readonly IFileUploadStager _stager;
    private readonly IFileProcessingPipeline _processing;
    private readonly IFileAuthorizationService _authorization;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICancellationTokenProvider _cancellationTokenProvider;
    private readonly FileManagementOptions _options;

    public FileManagementAppService(
        IRepository<FileAsset, Guid> files,
        IFileStorage storage,
        IFileUploadStager stager,
        IFileProcessingPipeline processing,
        IFileAuthorizationService authorization,
        IPermissionChecker permissionChecker,
        IUnitOfWorkManager unitOfWorkManager,
        ICancellationTokenProvider cancellationTokenProvider,
        IOptions<FileManagementOptions> options)
    {
        _files = files;
        _storage = storage;
        _stager = stager;
        _processing = processing;
        _authorization = authorization;
        _permissionChecker = permissionChecker;
        _unitOfWorkManager = unitOfWorkManager;
        _cancellationTokenProvider = cancellationTokenProvider;
        _options = options.Value;
    }

    [Authorize(STNGPermissions.Files.Upload)]
    [UnitOfWork(IsDisabled = true)]
    public virtual async Task<FileAssetDto> UploadAsync(UploadFileInput input)
    {
        var cancellationToken = _cancellationTokenProvider.Token;
        var uploaderId = CurrentUser.GetId();
        var ownerId = input.OwnerUserId ?? uploaderId;
        if (ownerId != uploaderId && !await _permissionChecker.IsGrantedAsync(STNGPermissions.Files.ManageAll))
            throw new AbpAuthorizationException(FileManagementErrorCodes.AccessDenied);

        var id = GuidGenerator.Create();
        var extension = Path.GetExtension(input.Content.FileName ?? "");
        var storageKey = CreateStorageKey(id);
        var asset = new FileAsset(
            id,
            CurrentTenant.Id,
            input.Content.FileName ?? "",
            storageKey,
            FileAssetBlobContainer.Name,
            input.Content.ContentType ?? "application/octet-stream",
            extension,
            input.Content.ContentLength ?? 0,
            ownerId,
            uploaderId);

        await InsertAssetAsync(asset, cancellationToken);
        await UpdateAssetAsync(id, x => x.BeginValidation(), cancellationToken);

        var blobSaved = false;
        try
        {
            await using var staged = await _stager.StageAsync(input.Content, cancellationToken);
            await UpdateAssetAsync(id,
                x => x.BeginProcessing(staged.FileName, staged.ContentType, staged.Extension, staged.SizeInBytes),
                cancellationToken);
            await _processing.ProcessAsync(staged, cancellationToken);

            await using (var content = staged.OpenRead())
                await _storage.SaveAsync(storageKey, content, cancellationToken);
            blobSaved = true;

            asset = await UpdateAssetAsync(id, x => x.MarkReady(staged.ContentHash), cancellationToken);
            return Map(asset);
        }
        catch (BusinessException exception)
        {
            if (blobSaved)
                await TryDeleteBlobAsync(storageKey, cancellationToken);
            await TryRecordFailureAsync(id, exception.Code ?? FileManagementErrorCodes.ContentSignatureMismatch, rejected: true, cancellationToken);
            throw;
        }
        catch (Exception exception)
        {
            if (blobSaved)
                await TryDeleteBlobAsync(storageKey, cancellationToken);
            await TryRecordFailureAsync(id, "Upload processing failed.", rejected: false, cancellationToken);
            Logger.LogError(exception, "File upload {FileAssetId} failed.", id);
            throw;
        }
    }

    [AllowAnonymous]
    public virtual async Task<FileAssetDto> GetAsync(Guid id)
    {
        var asset = await GetWithReferencesAsync(id, _cancellationTokenProvider.Token);
        await EnsureReadableOrNotFoundAsync(asset, _cancellationTokenProvider.Token);
        return Map(asset);
    }

    [AllowAnonymous]
    public virtual async Task<IRemoteStreamContent> DownloadAsync(Guid id)
    {
        var cancellationToken = _cancellationTokenProvider.Token;
        var asset = await GetWithReferencesAsync(id, cancellationToken);
        await EnsureReadableOrNotFoundAsync(asset, cancellationToken);
        if (asset.Status != FileAssetStatus.Ready)
            throw new BusinessException(FileManagementErrorCodes.FileNotReady);

        var stream = await _storage.OpenReadAsync(asset.StorageKey, cancellationToken);
        return new RemoteStreamContent(stream, asset.OriginalFileName, asset.ContentType, asset.SizeInBytes, disposeStream: true);
    }

    [AllowAnonymous]
    public virtual async Task<PagedResultDto<FileAssetDto>> GetListAsync(GetFileListInput input)
    {
        var cancellationToken = _cancellationTokenProvider.Token;
        var scanLimit = Math.Max(1, _options.MaximumDiscoveryScanCount);
        var query = await _files.WithDetailsAsync(x => x.References);
        query = query.Where(x =>
            x.TenantId == CurrentTenant.Id &&
            x.Status == FileAssetStatus.Ready &&
            x.IsDiscoverable);
        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var filter = input.Filter.Trim();
            query = query.Where(x => x.OriginalFileName.Contains(filter));
        }

        var candidates = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.CreationTime).Take(scanLimit), cancellationToken);
        var authorized = new List<FileAsset>();
        foreach (var candidate in candidates)
        {
            if (await _authorization.IsGrantedAsync(candidate, FileAccessOperation.Discover, cancellationToken))
                authorized.Add(candidate);
        }

        var maxResultCount = Math.Clamp(input.MaxResultCount, 1, FileManagementConsts.MaxDiscoveryPageSize);
        var items = authorized.Skip(Math.Max(0, input.SkipCount)).Take(maxResultCount).Select(Map).ToList();
        return new PagedResultDto<FileAssetDto>(authorized.Count, items);
    }

    [Authorize(STNGPermissions.Files.Replace)]
    [UnitOfWork(IsDisabled = true)]
    public virtual async Task<FileAssetDto> ReplaceAsync(Guid id, ReplaceFileContentInput input)
    {
        var cancellationToken = _cancellationTokenProvider.Token;
        var current = await GetWithReferencesInNewUnitAsync(id, cancellationToken);
        await _authorization.EnsureGrantedAsync(current, FileAccessOperation.Replace, cancellationToken);

        await using var staged = await _stager.StageAsync(input.Content, cancellationToken);
        await _processing.ProcessAsync(staged, cancellationToken);
        var newStorageKey = CreateStorageKey(id);
        await using (var content = staged.OpenRead())
            await _storage.SaveAsync(newStorageKey, content, cancellationToken);

        try
        {
            var oldStorageKey = current.StorageKey;
            current = await UpdateAssetAsync(id, asset =>
            {
                asset.ReplaceReadyContent(
                    staged.FileName,
                    newStorageKey,
                    staged.ContentType,
                    staged.Extension,
                    staged.SizeInBytes,
                    staged.ContentHash,
                    CurrentUser.GetId());
            }, cancellationToken, FileAccessOperation.Replace);

            await TryDeleteBlobAsync(oldStorageKey, cancellationToken);
            return Map(current);
        }
        catch
        {
            await TryDeleteBlobAsync(newStorageKey, cancellationToken);
            throw;
        }
    }

    [Authorize(STNGPermissions.Files.Attach)]
    [UnitOfWork(IsDisabled = true)]
    public virtual async Task<FileReferenceDto> AttachAsync(Guid id, FileReferenceInput input)
    {
        var cancellationToken = _cancellationTokenProvider.Token;
        using var unit = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var asset = await GetWithReferencesAsync(id, cancellationToken);
        await _authorization.EnsureGrantedAsync(asset, FileAccessOperation.Attach, cancellationToken);
        await _authorization.EnsureResourceGrantedAsync(asset, input, FileAccessOperation.Attach, cancellationToken);
        var reference = asset.AttachReference(GuidGenerator.Create(), input.ResourceType, input.ResourceId, input.Purpose);
        await _files.UpdateAsync(asset, autoSave: true, cancellationToken);
        await unit.CompleteAsync(cancellationToken);
        return Map(reference);
    }

    [Authorize(STNGPermissions.Files.Detach)]
    [UnitOfWork(IsDisabled = true)]
    public virtual async Task DetachAsync(Guid id, Guid referenceId)
    {
        var cancellationToken = _cancellationTokenProvider.Token;
        using var unit = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var asset = await GetWithReferencesAsync(id, cancellationToken);
        await _authorization.EnsureGrantedAsync(asset, FileAccessOperation.Detach, cancellationToken);
        var reference = asset.References.SingleOrDefault(x => x.Id == referenceId)
            ?? throw new BusinessException(FileManagementErrorCodes.ReferenceNotFound);
        await _authorization.EnsureResourceGrantedAsync(asset, new FileReferenceInput
        {
            ResourceType = reference.ResourceType,
            ResourceId = reference.ResourceId,
            Purpose = reference.Purpose
        }, FileAccessOperation.Detach, cancellationToken);
        asset.DetachReference(referenceId);
        await _files.UpdateAsync(asset, autoSave: true, cancellationToken);
        await unit.CompleteAsync(cancellationToken);
    }

    [Authorize(STNGPermissions.Files.Delete)]
    [UnitOfWork(IsDisabled = true)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var cancellationToken = _cancellationTokenProvider.Token;
        string storageKey;
        using (var unit = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var asset = await GetWithReferencesAsync(id, cancellationToken);
            await _authorization.EnsureGrantedAsync(asset, FileAccessOperation.Delete, cancellationToken);
            asset.MarkDeleted();
            storageKey = asset.StorageKey;
            await _files.DeleteAsync(asset, autoSave: true, cancellationToken);
            await unit.CompleteAsync(cancellationToken);
        }
        await TryDeleteBlobAsync(storageKey, cancellationToken);
    }

    [Authorize(STNGPermissions.Files.ChangeVisibility)]
    [UnitOfWork(IsDisabled = true)]
    public virtual async Task<FileAssetDto> ChangeVisibilityAsync(Guid id, ChangeFileVisibilityInput input)
    {
        var cancellationToken = _cancellationTokenProvider.Token;
        using var unit = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var asset = await GetWithReferencesAsync(id, cancellationToken);
        await _authorization.EnsureGrantedAsync(asset, FileAccessOperation.ChangeVisibility, cancellationToken);
        if (input.Visibility == FileVisibility.Public &&
            !await _permissionChecker.IsGrantedAsync(STNGPermissions.Files.Publish))
            throw new AbpAuthorizationException(FileManagementErrorCodes.PublicVisibilityRequiresPermission);

        asset.ChangeVisibility(input.Visibility, input.IsDiscoverable);
        await _files.UpdateAsync(asset, autoSave: true, cancellationToken);
        await unit.CompleteAsync(cancellationToken);
        return Map(asset);
    }

    [Authorize(STNGPermissions.Files.Maintenance)]
    [UnitOfWork(IsDisabled = true)]
    public virtual async Task<FileCleanupResultDto> CleanupIncompleteAsync()
    {
        var cancellationToken = _cancellationTokenProvider.Token;
        var cutoff = Clock.Now.Subtract(_options.IncompleteUploadRetention);
        var query = await _files.GetQueryableAsync();
        var candidates = await AsyncExecuter.ToListAsync(query.Where(x =>
            x.TenantId == CurrentTenant.Id &&
            x.CreationTime < cutoff &&
            (x.Status == FileAssetStatus.Pending || x.Status == FileAssetStatus.Validating || x.Status == FileAssetStatus.Processing)), cancellationToken);

        var cleaned = 0;
        foreach (var candidate in candidates)
        {
            await TryDeleteBlobAsync(candidate.StorageKey, cancellationToken);
            await TryRecordFailureAsync(candidate.Id, "Incomplete upload expired.", rejected: false, cancellationToken);
            cleaned++;
        }
        cleaned += await _stager.CleanupStaleAsync(cutoff, cancellationToken);
        return new FileCleanupResultDto { InspectedCount = candidates.Count, CleanedCount = cleaned };
    }

    private async Task InsertAssetAsync(FileAsset asset, CancellationToken cancellationToken)
    {
        using var unit = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        await _files.InsertAsync(asset, autoSave: true, cancellationToken);
        await unit.CompleteAsync(cancellationToken);
    }

    private async Task<FileAsset> UpdateAssetAsync(
        Guid id,
        Action<FileAsset> action,
        CancellationToken cancellationToken,
        FileAccessOperation? reauthorize = null)
    {
        using var unit = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var asset = await GetWithReferencesAsync(id, cancellationToken);
        if (reauthorize.HasValue)
            await _authorization.EnsureGrantedAsync(asset, reauthorize.Value, cancellationToken);
        action(asset);
        await _files.UpdateAsync(asset, autoSave: true, cancellationToken);
        await unit.CompleteAsync(cancellationToken);
        return asset;
    }

    private async Task<FileAsset> GetWithReferencesInNewUnitAsync(Guid id, CancellationToken cancellationToken)
    {
        using var unit = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var asset = await GetWithReferencesAsync(id, cancellationToken);
        await unit.CompleteAsync(cancellationToken);
        return asset;
    }

    private async Task<FileAsset> GetWithReferencesAsync(Guid id, CancellationToken cancellationToken)
    {
        var query = await _files.WithDetailsAsync(x => x.References);
        return await AsyncExecuter.FirstOrDefaultAsync(query.Where(x => x.Id == id), cancellationToken)
            ?? throw new EntityNotFoundException(typeof(FileAsset), id);
    }

    private async Task EnsureReadableOrNotFoundAsync(FileAsset asset, CancellationToken cancellationToken)
    {
        if (!await _authorization.IsGrantedAsync(asset, FileAccessOperation.Read, cancellationToken))
            throw new EntityNotFoundException(typeof(FileAsset), asset.Id);
    }

    private async Task TryRecordFailureAsync(Guid id, string reason, bool rejected, CancellationToken cancellationToken)
    {
        try
        {
            await UpdateAssetAsync(id, asset =>
            {
                if (rejected && asset.Status is FileAssetStatus.Validating or FileAssetStatus.Processing)
                    asset.Reject(reason);
                else
                    asset.MarkFailed(reason);
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Could not record the failed state for file asset {FileAssetId}.", id);
        }
    }

    private async Task TryDeleteBlobAsync(string storageKey, CancellationToken cancellationToken)
    {
        try
        {
            await _storage.DeleteIfExistsAsync(storageKey, cancellationToken);
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Could not delete file storage object {StorageKey}; maintenance cleanup is required.", storageKey);
        }
    }

    private static string CreateStorageKey(Guid assetId) => $"{assetId:N}/{Guid.NewGuid():N}";

    private static FileAssetDto Map(FileAsset asset) => new()
    {
        Id = asset.Id,
        OriginalFileName = asset.OriginalFileName,
        ContentType = asset.ContentType,
        Extension = asset.Extension,
        SizeInBytes = asset.SizeInBytes,
        ContentHash = asset.ContentHash,
        Visibility = asset.Visibility,
        IsDiscoverable = asset.IsDiscoverable,
        Status = asset.Status,
        CreationTime = asset.CreationTime,
        CreatorId = asset.CreatorId,
        LastModificationTime = asset.LastModificationTime,
        LastModifierId = asset.LastModifierId
    };

    private static FileReferenceDto Map(FileReference reference) => new()
    {
        Id = reference.Id,
        ResourceType = reference.ResourceType,
        ResourceId = reference.ResourceId,
        Purpose = reference.Purpose
    };
}
