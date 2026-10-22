using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using STNG.FileManagement;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace STNG.EntityFrameworkCore.FileManagement;

public class FileAssetConfiguration : IEntityTypeConfiguration<FileAsset>
{
    public void Configure(EntityTypeBuilder<FileAsset> builder)
    {
        builder.ToTable(STNGConsts.DbTablePrefix + "FileAssets", STNGConsts.DbSchema);
        builder.ConfigureByConvention();

        builder.Property(x => x.OriginalFileName).IsRequired().HasMaxLength(FileManagementConsts.MaxFileNameLength);
        builder.Property(x => x.StorageKey).IsRequired().HasMaxLength(FileManagementConsts.MaxStorageKeyLength);
        builder.Property(x => x.StorageContainer).IsRequired().HasMaxLength(FileManagementConsts.MaxStorageContainerLength);
        builder.Property(x => x.ContentType).IsRequired().HasMaxLength(FileManagementConsts.MaxContentTypeLength);
        builder.Property(x => x.Extension).IsRequired().HasMaxLength(FileManagementConsts.MaxExtensionLength);
        builder.Property(x => x.ContentHash).HasMaxLength(FileManagementConsts.ContentHashLength).IsFixedLength();
        builder.Property(x => x.FailureReason).HasMaxLength(FileManagementConsts.MaxFailureReasonLength);
        builder.Property(x => x.SizeInBytes).IsRequired();
        builder.Property(x => x.Visibility).IsRequired();
        builder.Property(x => x.Status).IsRequired();

        builder.HasIndex(x => x.StorageKey).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Status, x.IsDiscoverable, x.CreationTime });
        builder.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.CreationTime });

        builder.HasMany(x => x.References)
            .WithOne()
            .HasForeignKey(x => x.FileAssetId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.References).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
