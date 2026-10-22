using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.BlobStoring.Database.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Volo.Abp.FeatureManagement.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using STNG.EntityFrameworkCore.FileManagement;

namespace STNG.EntityFrameworkCore;

[ReplaceDbContext(typeof(IIdentityDbContext))]
[ConnectionStringName("Default")]
public class STNGDbContext :
    AbpDbContext<STNGDbContext>,
    IIdentityDbContext
{
    public DbSet<STNG.Parents.Parent> Parents { get; set; }
    public DbSet<STNG.Authentication.EmailVerificationOtp> EmailVerificationOtps { get; set; }
    public DbSet<STNG.FileManagement.FileAsset> FileAssets { get; set; }
    public DbSet<STNG.FileManagement.FileReference> FileReferences { get; set; }


    #region Entities from the modules

    /* Notice: We only implemented IIdentityProDbContext 
     * and replaced them for this DbContext. This allows you to perform JOIN
     * queries for the entities of these modules over the repositories easily. You
     * typically don't need that for other modules. But, if you need, you can
     * implement the DbContext interface of the needed module and use ReplaceDbContext
     * attribute just like IIdentityProDbContext .
     *
     * More info: Replacing a DbContext of a module ensures that the related module
     * uses this DbContext on runtime. Otherwise, it will use its own DbContext class.
     */

    // Identity
    public DbSet<IdentityUser> Users { get; set; }
    public DbSet<IdentityRole> Roles { get; set; }
    public DbSet<IdentityClaimType> ClaimTypes { get; set; }
    public DbSet<OrganizationUnit> OrganizationUnits { get; set; }
    public DbSet<IdentitySecurityLog> SecurityLogs { get; set; }
    public DbSet<IdentityLinkUser> LinkUsers { get; set; }
    public DbSet<IdentityUserDelegation> UserDelegations { get; set; }
    public DbSet<IdentitySession> Sessions { get; set; }

    #endregion

    public STNGDbContext(DbContextOptions<STNGDbContext> options)
        : base(options)
    {

    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Include modules to your migration db context */

        builder.ConfigurePermissionManagement();
        builder.ConfigureSettingManagement();
        builder.ConfigureBackgroundJobs();
        builder.ConfigureAuditLogging();
        builder.ConfigureFeatureManagement();
        builder.ConfigureIdentity();
        builder.ConfigureOpenIddict();
        builder.ConfigureBlobStoring();
        builder.ApplyConfiguration(new ParentConfiguration());
        builder.ApplyConfiguration(new EmailVerificationOtpConfiguration());
        builder.ApplyConfiguration(new FileAssetConfiguration());
        builder.ApplyConfiguration(new FileReferenceConfiguration());
        // Host-only application: prevent concurrent registrations with the same email.
        builder.Entity<IdentityUser>().HasIndex(x => x.NormalizedEmail).IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [TenantId] IS NULL");

        /* Configure your own tables/entities inside here */

        //builder.Entity<YourEntity>(b =>
        //{
        //    b.ToTable(STNGConsts.DbTablePrefix + "YourEntities", STNGConsts.DbSchema);
        //    b.ConfigureByConvention(); //auto configure for the base class props
        //    //...
        //});
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 } &&
            exception.Entries.Any(entry => entry.Entity is STNG.Parents.Parent or STNG.Authentication.EmailVerificationOtp or IdentityUser))
        {
            // A race that passed the application check still loses at the unique constraint.
            throw new AbpDbConcurrencyException("A concurrent account operation conflicted. Retry the request.", exception);
        }
    }
}
