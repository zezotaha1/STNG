using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using STNG.Families;
using STNG.Parents;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace STNG.EntityFrameworkCore;

public class FamilyConfiguration : IEntityTypeConfiguration<Family>
{
    public void Configure(EntityTypeBuilder<Family> builder)
    {
        builder.ToTable(STNGConsts.DbTablePrefix + "Families", STNGConsts.DbSchema);
        builder.ConfigureByConvention();
        builder.Property(x => x.Name).HasConversion(x => x.Value, x => new FamilyName(x))
            .HasMaxLength(FamilyConsts.MaxNameLength).IsRequired();
        builder.Property(x => x.Status).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.CreatedByParentId, x.CreationRequestId }).IsUnique();
        builder.HasOne<Parent>().WithMany().HasForeignKey(x => x.CreatedByParentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Members).WithOne().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.OwnershipTransfers).WithOne().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.MembershipHistory).WithOne().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Members).HasField("_members").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.OwnershipTransfers).HasField("_ownershipTransfers").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.MembershipHistory).HasField("_membershipHistory").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class FamilyMemberConfiguration : IEntityTypeConfiguration<FamilyMember>
{
    public void Configure(EntityTypeBuilder<FamilyMember> builder)
    {
        builder.ToTable(STNGConsts.DbTablePrefix + "FamilyMembers", STNGConsts.DbSchema);
        builder.ConfigureByConvention();
        builder.HasIndex(x => x.FamilyId).HasDatabaseName("IX_AppFamilyMembers_ActiveOwner")
            .IsUnique().HasFilter("[Role] = 0 AND [Status] = 0");
        builder.HasIndex(x => new { x.ParentId, x.Status });
        builder.HasOne<Parent>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Parent>().WithMany().HasForeignKey(x => x.RemovedByParentId).OnDelete(DeleteBehavior.Restrict);
        // Also serves as the unique membership constraint and the principal key for transfers.
        builder.HasAlternateKey(x => new { x.FamilyId, x.ParentId });
    }
}

public class OwnershipTransferConfiguration : IEntityTypeConfiguration<OwnershipTransfer>
{
    public void Configure(EntityTypeBuilder<OwnershipTransfer> builder)
    {
        builder.ToTable(STNGConsts.DbTablePrefix + "OwnershipTransfers", STNGConsts.DbSchema);
        builder.ConfigureByConvention();
        builder.HasIndex(x => x.FamilyId).HasDatabaseName("IX_AppOwnershipTransfers_Pending")
            .IsUnique().HasFilter("[Status] = 0");
        builder.HasOne<FamilyMember>().WithMany().HasForeignKey(x => new { x.FamilyId, ParentId = x.FromParentId })
            .HasPrincipalKey(x => new { x.FamilyId, x.ParentId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FamilyMember>().WithMany().HasForeignKey(x => new { x.FamilyId, ParentId = x.ToParentId })
            .HasPrincipalKey(x => new { x.FamilyId, x.ParentId }).OnDelete(DeleteBehavior.Restrict);
    }
}

public class FamilyMembershipHistoryConfiguration : IEntityTypeConfiguration<FamilyMembershipHistory>
{
    public void Configure(EntityTypeBuilder<FamilyMembershipHistory> builder)
    {
        builder.ToTable(STNGConsts.DbTablePrefix + "FamilyMembershipHistory", STNGConsts.DbSchema);
        builder.ConfigureByConvention();
        builder.HasIndex(x => new { x.FamilyId, x.OccurredAt });
        builder.HasOne<FamilyMember>().WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Parent>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Parent>().WithMany().HasForeignKey(x => x.ActorParentId).OnDelete(DeleteBehavior.Restrict);
    }
}
