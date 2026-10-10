using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using STNG.Parents;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Volo.Abp.Identity;

namespace STNG.EntityFrameworkCore;

public class ParentConfiguration : IEntityTypeConfiguration<Parent>
{
    public void Configure(EntityTypeBuilder<Parent> builder)
    {
        builder.ToTable(STNGConsts.DbTablePrefix + "Parents", STNGConsts.DbSchema);
        builder.ConfigureByConvention();
        builder.Property(x => x.Job).HasMaxLength(Parent.MaxJobLength);
        builder.HasIndex(x => x.UserId).IsUnique();
        builder.HasOne<IdentityUser>().WithOne().HasForeignKey<Parent>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
