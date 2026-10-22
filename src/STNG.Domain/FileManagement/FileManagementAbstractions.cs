using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace STNG.FileManagement;

public sealed record FileAccessPolicyContext(
    Guid FileAssetId,
    Guid? TenantId,
    Guid? UserId,
    string ResourceType,
    string ResourceId,
    string Purpose,
    FileAccessOperation Operation);

public interface IFileAccessPolicy
{
    string ResourceType { get; }
    Task<bool> IsGrantedAsync(FileAccessPolicyContext context, CancellationToken cancellationToken = default);
}

public interface IFileStorage
{
    Task SaveAsync(string storageKey, Stream stream, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);
    Task DeleteIfExistsAsync(string storageKey, CancellationToken cancellationToken = default);
}

public sealed record FileScanContext(
    string FileName,
    string ContentType,
    string Extension,
    long SizeInBytes,
    string ContentHash);

public interface IFileSecurityScanner
{
    string Name { get; }
    Task<FileScanResult> ScanAsync(FileScanContext context, Stream content, CancellationToken cancellationToken = default);
}

public interface IFileProcessingHandler
{
    int Order { get; }
    Task ProcessAsync(FileScanContext context, Stream content, CancellationToken cancellationToken = default);
}
