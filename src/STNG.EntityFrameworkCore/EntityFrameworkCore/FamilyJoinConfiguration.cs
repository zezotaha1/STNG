using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using STNG.Families;
using STNG.Parents;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace STNG.EntityFrameworkCore;

public class FamilyJoinCredentialConfiguration : IEntityTypeConfiguration<FamilyJoinCredential>
{
    public void Configure(EntityTypeBuilder<FamilyJoinCredential> builder)
    {
        builder.ToTable(STNGConsts.DbTablePrefix + "FamilyJoinCredentials", STNGConsts.DbSchema);
        builder.ConfigureByConvention();
        builder.Property(x => x.SecretHash).HasMaxLength(FamilyJoinConsts.HashLength).IsUnicode(false).IsRequired();
        builder.HasIndex(x => new { x.Kind, x.SecretHash }).IsUnique();
        builder.HasIndex(x => new { x.FamilyId, x.CreatedAt, x.Id });
        builder.HasIndex(x => new { x.FamilyId, x.Kind }).IsUnique().HasFilter("[Status] = 0")
            .HasDatabaseName("IX_AppFamilyJoinCredentials_ActiveKind");
        builder.HasAlternateKey(x => new { x.FamilyId, x.Id });
        builder.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FamilyMember>().WithMany().HasForeignKey(x => new { x.FamilyId, ParentId = x.CreatedByParentId })
            .HasPrincipalKey(x => new { x.FamilyId, x.ParentId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FamilyMember>().WithMany().HasForeignKey(x => new { x.FamilyId, ParentId = x.RevokedByParentId })
            .HasPrincipalKey(x => new { x.FamilyId, x.ParentId }).OnDelete(DeleteBehavior.Restrict);
    }
}

public class FamilyJoinRequestConfiguration : IEntityTypeConfiguration<FamilyJoinRequest>
{
    public void Configure(EntityTypeBuilder<FamilyJoinRequest> builder)
    {
        builder.ToTable(STNGConsts.DbTablePrefix + "FamilyJoinRequests", STNGConsts.DbSchema);
        builder.ConfigureByConvention();
        builder.Property(x => x.RejectionReason).HasMaxLength(FamilyJoinConsts.MaxRejectionReasonLength);
        builder.HasIndex(x => new { x.FamilyId, x.ParentId }).IsUnique().HasFilter("[Status] = 0")
            .HasDatabaseName("IX_AppFamilyJoinRequests_PendingParent");
        builder.HasIndex(x => new { x.ParentId, x.RequestedAt, x.Id });
        builder.HasIndex(x => new { x.FamilyId, x.Status, x.RequestedAt, x.Id });
        builder.HasIndex(x => new { x.CredentialId, x.Status });
        builder.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Parent>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FamilyJoinCredential>().WithMany().HasForeignKey(x => new { x.FamilyId, Id = x.CredentialId })
            .HasPrincipalKey(x => new { x.FamilyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FamilyMember>().WithMany().HasForeignKey(x => new { x.FamilyId, ParentId = x.ReviewedByParentId })
            .HasPrincipalKey(x => new { x.FamilyId, x.ParentId }).OnDelete(DeleteBehavior.Restrict);
    }
}
