using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;

namespace STNG.FileManagement;

public class AbpBlobFileStorage : IFileStorage, ITransientDependency
{
    private readonly IBlobContainer<FileAssetBlobContainer> _container;

    public AbpBlobFileStorage(IBlobContainer<FileAssetBlobContainer> container)
    {
        _container = container;
    }

    public Task SaveAsync(string storageKey, Stream stream, CancellationToken cancellationToken = default) =>
        _container.SaveAsync(storageKey, stream, overrideExisting: false, cancellationToken);

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) =>
        _container.GetAsync(storageKey, cancellationToken);

    public Task DeleteIfExistsAsync(string storageKey, CancellationToken cancellationToken = default) =>
        _container.DeleteAsync(storageKey, cancellationToken);
}
