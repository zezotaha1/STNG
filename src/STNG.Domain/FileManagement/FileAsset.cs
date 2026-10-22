using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace STNG.FileManagement;

public class FileAsset : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    private readonly List<FileReference> _references = [];

    public Guid? TenantId { get; private set; }
    public string OriginalFileName { get; private set; } = null!;
    public string StorageKey { get; private set; } = null!;
    public string StorageContainer { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public string Extension { get; private set; } = null!;
    public long SizeInBytes { get; private set; }
    public string? ContentHash { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid UploadedByUserId { get; private set; }
    public FileVisibility Visibility { get; private set; }
    public bool IsDiscoverable { get; private set; }
    public FileAssetStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public IReadOnlyCollection<FileReference> References => _references.AsReadOnly();

    protected FileAsset()
    {
    }

    public FileAsset(
        Guid id,
        Guid? tenantId,
        string originalFileName,
        string storageKey,
        string storageContainer,
        string contentType,
        string extension,
        long sizeInBytes,
        Guid ownerUserId,
        Guid uploadedByUserId)
        : base(id)
    {
        if (ownerUserId == Guid.Empty)
            throw new ArgumentException("A file owner is required.", nameof(ownerUserId));
        if (uploadedByUserId == Guid.Empty)
            throw new ArgumentException("An uploader is required.", nameof(uploadedByUserId));
        if (sizeInBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(sizeInBytes));

        TenantId = tenantId;
        OwnerUserId = ownerUserId;
        UploadedByUserId = uploadedByUserId;
        OriginalFileName = NormalizeFileName(originalFileName);
        StorageKey = Check.NotNullOrWhiteSpace(storageKey, nameof(storageKey), FileManagementConsts.MaxStorageKeyLength);
        StorageContainer = Check.NotNullOrWhiteSpace(storageContainer, nameof(storageContainer), FileManagementConsts.MaxStorageContainerLength);
        ContentType = NormalizeContentType(contentType);
        Extension = NormalizeExtension(extension);
        SizeInBytes = sizeInBytes;
        Visibility = FileVisibility.OwnerOnly;
        Status = FileAssetStatus.Pending;
    }

    public void BeginValidation()
    {
        EnsureStatus(FileAssetStatus.Pending);
        Status = FileAssetStatus.Validating;
        FailureReason = null;
    }

    public void BeginProcessing(string originalFileName, string contentType, string extension, long sizeInBytes)
    {
        EnsureStatus(FileAssetStatus.Validating);
        if (sizeInBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(sizeInBytes));
        OriginalFileName = NormalizeFileName(originalFileName);
        ContentType = NormalizeContentType(contentType);
        Extension = NormalizeExtension(extension);
        SizeInBytes = sizeInBytes;
        Status = FileAssetStatus.Processing;
    }

    public void MarkReady(string contentHash)
    {
        EnsureStatus(FileAssetStatus.Processing);
        ContentHash = NormalizeHash(contentHash);
        Status = FileAssetStatus.Ready;
        FailureReason = null;
    }

    public void Reject(string reason)
    {
        if (Status is not (FileAssetStatus.Validating or FileAssetStatus.Processing))
            ThrowInvalidTransition(FileAssetStatus.Rejected);

        Status = FileAssetStatus.Rejected;
        FailureReason = NormalizeFailureReason(reason);
    }

    public void MarkFailed(string reason)
    {
        if (Status is FileAssetStatus.Ready or FileAssetStatus.Rejected or FileAssetStatus.Failed or FileAssetStatus.Deleted)
            ThrowInvalidTransition(FileAssetStatus.Failed);

        Status = FileAssetStatus.Failed;
        FailureReason = NormalizeFailureReason(reason);
    }

    public void ReplaceReadyContent(
        string originalFileName,
        string storageKey,
        string contentType,
        string extension,
        long sizeInBytes,
        string contentHash,
        Guid uploadedByUserId)
    {
        EnsureStatus(FileAssetStatus.Ready);
        if (uploadedByUserId == Guid.Empty)
            throw new ArgumentException("An uploader is required.", nameof(uploadedByUserId));
        if (sizeInBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(sizeInBytes));

        OriginalFileName = NormalizeFileName(originalFileName);
        StorageKey = Check.NotNullOrWhiteSpace(storageKey, nameof(storageKey), FileManagementConsts.MaxStorageKeyLength);
        ContentType = NormalizeContentType(contentType);
        Extension = NormalizeExtension(extension);
        SizeInBytes = sizeInBytes;
        ContentHash = NormalizeHash(contentHash);
        UploadedByUserId = uploadedByUserId;
        FailureReason = null;
    }

    public FileReference AttachReference(Guid referenceId, string resourceType, string resourceId, string purpose)
    {
        EnsureStatus(FileAssetStatus.Ready);
        var normalizedType = FileReference.NormalizeResourceType(resourceType);
        var normalizedId = FileReference.NormalizeResourceId(resourceId);
        var normalizedPurpose = FileReference.NormalizePurpose(purpose);

        if (_references.Any(x => x.Matches(normalizedType, normalizedId, normalizedPurpose)))
            throw new BusinessException(FileManagementErrorCodes.DuplicateReference);

        var reference = new FileReference(referenceId, TenantId, Id, normalizedType, normalizedId, normalizedPurpose);
        _references.Add(reference);
        return reference;
    }

    public void DetachReference(Guid referenceId)
    {
        EnsureStatus(FileAssetStatus.Ready);
        var reference = _references.SingleOrDefault(x => x.Id == referenceId)
            ?? throw new BusinessException(FileManagementErrorCodes.ReferenceNotFound);
        if (Visibility == FileVisibility.Restricted && _references.Count == 1)
            throw new BusinessException(FileManagementErrorCodes.RestrictedFileRequiresReference);
        _references.Remove(reference);
    }

    public void ChangeVisibility(FileVisibility visibility, bool isDiscoverable)
    {
        EnsureStatus(FileAssetStatus.Ready);
        if (visibility == FileVisibility.Restricted && _references.Count == 0)
            throw new BusinessException(FileManagementErrorCodes.RestrictedFileRequiresReference);

        Visibility = visibility;
        IsDiscoverable = isDiscoverable;
    }

    public void MarkDeleted()
    {
        EnsureStatus(FileAssetStatus.Ready, FileAssetStatus.Rejected, FileAssetStatus.Failed);
        if (_references.Count != 0)
            throw new BusinessException(FileManagementErrorCodes.FileStillReferenced);
        Status = FileAssetStatus.Deleted;
        IsDiscoverable = false;
    }

    private void EnsureStatus(params FileAssetStatus[] allowed)
    {
        if (!allowed.Contains(Status))
            ThrowInvalidTransition(allowed[0]);
    }

    private void ThrowInvalidTransition(FileAssetStatus target)
    {
        throw new BusinessException(FileManagementErrorCodes.InvalidStateTransition)
            .WithData("CurrentStatus", Status)
            .WithData("TargetStatus", target);
    }

    private static string NormalizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > FileManagementConsts.MaxFileNameLength)
            throw new BusinessException(FileManagementErrorCodes.InvalidFileName);
        var name = value.Trim();
        if (name is "." or ".." || name != Path.GetFileName(name) || name.Contains('/') || name.Contains('\\') ||
            name.Any(char.IsControl) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new BusinessException(FileManagementErrorCodes.InvalidFileName);
        return name;
    }

    private static string NormalizeContentType(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > FileManagementConsts.MaxContentTypeLength)
            throw new BusinessException(FileManagementErrorCodes.ContentTypeNotAllowed);
        return value.Trim().ToLowerInvariant();
    }

    private static string NormalizeExtension(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > FileManagementConsts.MaxExtensionLength)
            throw new BusinessException(FileManagementErrorCodes.ExtensionNotAllowed);
        var extension = value.Trim().ToLowerInvariant();
        return extension.StartsWith('.') ? extension : "." + extension;
    }

    private static string NormalizeHash(string value)
    {
        var hash = Check.NotNullOrWhiteSpace(value, nameof(value), FileManagementConsts.ContentHashLength).Trim().ToLowerInvariant();
        if (hash.Length != FileManagementConsts.ContentHashLength || hash.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("The content hash must be a SHA-256 hexadecimal value.", nameof(value));
        return hash;
    }

    private static string NormalizeFailureReason(string value) =>
        Check.NotNullOrWhiteSpace(value, nameof(value), FileManagementConsts.MaxFailureReasonLength).Trim();
}
