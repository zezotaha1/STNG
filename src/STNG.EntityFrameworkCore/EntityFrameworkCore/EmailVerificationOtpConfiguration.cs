using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using STNG.Authentication;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Volo.Abp.Identity;

namespace STNG.EntityFrameworkCore;

public class EmailVerificationOtpConfiguration : IEntityTypeConfiguration<EmailVerificationOtp>
{
    public void Configure(EntityTypeBuilder<EmailVerificationOtp> builder)
    {
        builder.ToTable(STNGConsts.DbTablePrefix + "EmailVerificationOtps", STNGConsts.DbSchema);
        builder.ConfigureByConvention();
        builder.Property(x => x.Purpose).IsRequired().HasMaxLength(64);
        builder.Property(x => x.CodeHash).IsRequired().HasMaxLength(128);
        builder.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.UserId, x.Purpose, x.LastSentAt });
        builder.HasIndex(x => new { x.UserId, x.Purpose }).IsUnique()
            .HasFilter("[UsedAt] IS NULL AND [InvalidatedAt] IS NULL");
    }
}
