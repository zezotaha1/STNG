using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Content;
using Volo.Abp.DependencyInjection;

namespace STNG.FileManagement;

public sealed class StagedFile : IAsyncDisposable
{
    private readonly ILogger _logger;
    public string Path { get; }
    public string FileName { get; }
    public string ContentType { get; }
    public string Extension { get; }
    public long SizeInBytes { get; }
    public string ContentHash { get; }

    public StagedFile(
        string path,
        string fileName,
        string contentType,
        string extension,
        long sizeInBytes,
        string contentHash,
        ILogger logger)
    {
        Path = path;
        FileName = fileName;
        ContentType = contentType;
        Extension = extension;
        SizeInBytes = sizeInBytes;
        ContentHash = contentHash;
        _logger = logger;
    }

    public FileStream OpenRead() => new(Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);

    public ValueTask DisposeAsync()
    {
        try
        {
            if (File.Exists(Path))
                File.Delete(Path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Could not remove file-management staging object {StagingFile}.", Path);
        }
        return ValueTask.CompletedTask;
    }
}

public interface IFileUploadStager
{
    Task<StagedFile> StageAsync(IRemoteStreamContent content, CancellationToken cancellationToken = default);
    Task<int> CleanupStaleAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default);
}

public class FileUploadStager : IFileUploadStager, ITransientDependency
{
    private readonly FileManagementOptions _options;
    private readonly ILogger<FileUploadStager> _logger;

    public FileUploadStager(IOptions<FileManagementOptions> options, ILogger<FileUploadStager> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<int> CleanupStaleAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default)
    {
        var stagingRoot = ResolveStagingRoot();
        if (!Directory.Exists(stagingRoot))
            return Task.FromResult(0);

        var cleaned = 0;
        foreach (var path in Directory.EnumerateFiles(stagingRoot, "*.upload", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.GetLastWriteTimeUtc(path) >= olderThanUtc)
                    continue;
                File.Delete(path);
                cleaned++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(exception, "Could not remove stale file-management staging object {StagingFile}.", path);
            }
        }
        return Task.FromResult(cleaned);
    }

    public async Task<StagedFile> StageAsync(IRemoteStreamContent content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var fileName = ValidateFileName(content.FileName);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var contentType = (content.ContentType ?? "").Trim().ToLowerInvariant();
        ValidateMetadata(extension, contentType, content.ContentLength);

        var stagingRoot = ResolveStagingRoot();
        Directory.CreateDirectory(stagingRoot);
        var stagedPath = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N") + ".upload");
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        long length = 0;

        try
        {
            string contentHash;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = content.GetStream())
            await using (var target = new FileStream(stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    length += read;
                    if (length > _options.MaximumFileSizeBytes)
                        throw new BusinessException(FileManagementErrorCodes.FileTooLarge);
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                await target.FlushAsync(cancellationToken);

                if (length == 0)
                    throw new BusinessException(FileManagementErrorCodes.EmptyFile);
                contentHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }

            var staged = new StagedFile(stagedPath, fileName, contentType, extension, length, contentHash, _logger);
            await ValidateSignatureAsync(staged, cancellationToken);
            return staged;
        }
        catch
        {
            if (File.Exists(stagedPath))
                File.Delete(stagedPath);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void ValidateMetadata(string extension, string contentType, long? declaredLength)
    {
        if (!_options.AllowedExtensions.Contains(extension))
            throw new BusinessException(FileManagementErrorCodes.ExtensionNotAllowed).WithData("Extension", extension);
        if (!_options.AllowedContentTypes.Contains(contentType))
            throw new BusinessException(FileManagementErrorCodes.ContentTypeNotAllowed).WithData("ContentType", contentType);
        if (declaredLength is > 0 && declaredLength > _options.MaximumFileSizeBytes)
            throw new BusinessException(FileManagementErrorCodes.FileTooLarge);

        var expected = extension switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => null
        };
        if (expected != null && !string.Equals(expected, contentType, StringComparison.OrdinalIgnoreCase))
            throw new BusinessException(FileManagementErrorCodes.ContentSignatureMismatch);
    }

    private async Task ValidateSignatureAsync(StagedFile staged, CancellationToken cancellationToken)
    {
        if (!_options.RequireKnownContentSignature)
            return;

        var signature = new byte[8];
        await using var stream = staged.OpenRead();
        var count = await stream.ReadAsync(signature, cancellationToken);
        var valid = staged.Extension switch
        {
            ".pdf" => count >= 5 && signature.AsSpan(0, 5).SequenceEqual("%PDF-"u8),
            ".png" => count >= 8 && signature.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }),
            ".jpg" or ".jpeg" => count >= 3 && signature[0] == 0xff && signature[1] == 0xd8 && signature[2] == 0xff,
            _ => false
        };
        if (!valid)
            throw new BusinessException(FileManagementErrorCodes.ContentSignatureMismatch);
    }

    private static string ValidateFileName(string? value)
    {
        var fileName = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > FileManagementConsts.MaxFileNameLength ||
            fileName is "." or ".." || fileName != Path.GetFileName(fileName) || fileName.Contains('/') || fileName.Contains('\\') ||
            fileName.Any(char.IsControl) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new BusinessException(FileManagementErrorCodes.InvalidFileName);
        return fileName;
    }

    private string ResolveStagingRoot()
    {
        var configured = _options.StagingRoot;
        if (!string.IsNullOrWhiteSpace(configured))
            return EnsureAbsolute(configured);
        if (!string.IsNullOrWhiteSpace(_options.StorageRoot))
            return Path.Combine(EnsureAbsolute(_options.StorageRoot), ".staging");
        return Path.Combine(Path.GetTempPath(), "STNG", "file-staging");
    }

    private static string EnsureAbsolute(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("File-management storage paths must be absolute.");
        return Path.GetFullPath(path);
    }
}
