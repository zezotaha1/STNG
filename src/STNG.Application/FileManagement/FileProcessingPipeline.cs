using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace STNG.FileManagement;

public interface IFileProcessingPipeline
{
    Task ProcessAsync(StagedFile staged, CancellationToken cancellationToken = default);
}

public class FileProcessingPipeline : IFileProcessingPipeline, ITransientDependency
{
    private readonly IReadOnlyList<IFileProcessingHandler> _handlers;
    private readonly IReadOnlyList<IFileSecurityScanner> _scanners;
    private readonly FileManagementOptions _options;

    public FileProcessingPipeline(
        IEnumerable<IFileProcessingHandler> handlers,
        IEnumerable<IFileSecurityScanner> scanners,
        IOptions<FileManagementOptions> options)
    {
        _handlers = handlers.OrderBy(x => x.Order).ToList();
        _scanners = scanners.ToList();
        _options = options.Value;
    }

    public async Task ProcessAsync(StagedFile staged, CancellationToken cancellationToken = default)
    {
        var context = new FileScanContext(staged.FileName, staged.ContentType, staged.Extension, staged.SizeInBytes, staged.ContentHash);

        foreach (var handler in _handlers)
        {
            await using var content = staged.OpenRead();
            await handler.ProcessAsync(context, content, cancellationToken);
        }

        if (_options.RequireSecurityScan && _scanners.Count == 0)
            throw new BusinessException(FileManagementErrorCodes.ScannerRequired);

        foreach (var scanner in _scanners)
        {
            await using var content = staged.OpenRead();
            var result = await scanner.ScanAsync(context, content, cancellationToken);
            if (result == FileScanResult.ThreatDetected)
                throw new BusinessException(FileManagementErrorCodes.ThreatDetected).WithData("Scanner", scanner.Name);
            if (result != FileScanResult.Clean)
                throw new BusinessException(FileManagementErrorCodes.ScannerRequired).WithData("Scanner", scanner.Name);
        }
    }
}
