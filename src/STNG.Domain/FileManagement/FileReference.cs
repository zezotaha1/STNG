using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace STNG.FileManagement;

public class FileReference : AuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid FileAssetId { get; private set; }
    public string ResourceType { get; private set; } = null!;
    public string ResourceId { get; private set; } = null!;
    public string Purpose { get; private set; } = null!;

    protected FileReference()
    {
    }

    internal FileReference(Guid id, Guid? tenantId, Guid fileAssetId, string resourceType, string resourceId, string purpose)
        : base(id)
    {
        if (fileAssetId == Guid.Empty)
            throw new ArgumentException("A file asset is required.", nameof(fileAssetId));

        TenantId = tenantId;
        FileAssetId = fileAssetId;
        ResourceType = NormalizeResourceType(resourceType);
        ResourceId = NormalizeResourceId(resourceId);
        Purpose = NormalizePurpose(purpose);
    }

    internal bool Matches(string resourceType, string resourceId, string purpose) =>
        string.Equals(ResourceType, resourceType, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(ResourceId, resourceId, StringComparison.Ordinal) &&
        string.Equals(Purpose, purpose, StringComparison.OrdinalIgnoreCase);

    internal static string NormalizeResourceType(string value) =>
        Check.NotNullOrWhiteSpace(value, nameof(value), FileManagementConsts.MaxResourceTypeLength).Trim();

    internal static string NormalizeResourceId(string value) =>
        Check.NotNullOrWhiteSpace(value, nameof(value), FileManagementConsts.MaxResourceIdLength).Trim();

    internal static string NormalizePurpose(string value) =>
        Check.NotNullOrWhiteSpace(value, nameof(value), FileManagementConsts.MaxPurposeLength).Trim();
}
