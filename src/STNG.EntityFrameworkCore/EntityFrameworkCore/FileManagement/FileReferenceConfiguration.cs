using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using STNG.FileManagement;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace STNG.EntityFrameworkCore.FileManagement;

public class FileReferenceConfiguration : IEntityTypeConfiguration<FileReference>
{
    public void Configure(EntityTypeBuilder<FileReference> builder)
    {
        builder.ToTable(STNGConsts.DbTablePrefix + "FileReferences", STNGConsts.DbSchema);
        builder.ConfigureByConvention();

        builder.Property(x => x.ResourceType).IsRequired().HasMaxLength(FileManagementConsts.MaxResourceTypeLength);
        builder.Property(x => x.ResourceId).IsRequired().HasMaxLength(FileManagementConsts.MaxResourceIdLength);
        builder.Property(x => x.Purpose).IsRequired().HasMaxLength(FileManagementConsts.MaxPurposeLength);

        builder.HasIndex(x => new { x.FileAssetId, x.ResourceType, x.ResourceId, x.Purpose }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.ResourceType, x.ResourceId });
    }
}
