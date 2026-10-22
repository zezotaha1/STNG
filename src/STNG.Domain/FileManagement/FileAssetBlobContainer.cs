using Volo.Abp.BlobStoring;

namespace STNG.FileManagement;

[BlobContainerName(Name)]
public sealed class FileAssetBlobContainer
{
    public const string Name = "file-assets";
}
