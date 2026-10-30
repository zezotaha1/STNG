using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using STNG.Localization;
using STNG.MultiTenancy;
using System;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement.Identity;
using Volo.Abp.SettingManagement;
using Volo.Abp.BlobStoring.Database;
using Volo.Abp.Caching;
using Volo.Abp.OpenIddict;
using Volo.Abp.PermissionManagement.OpenIddict;
using Volo.Abp.AuditLogging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Emailing;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity;
using STNG.FileManagement;

namespace STNG;

[DependsOn(
    typeof(STNGDomainSharedModule),
    typeof(AbpAuditLoggingDomainModule),
    typeof(AbpCachingModule),
    typeof(AbpBackgroundJobsDomainModule),
    typeof(AbpFeatureManagementDomainModule),
    typeof(AbpPermissionManagementDomainIdentityModule),
    typeof(AbpPermissionManagementDomainOpenIddictModule),
    typeof(AbpSettingManagementDomainModule),
    typeof(AbpEmailingModule),
    typeof(AbpIdentityDomainModule),
    typeof(AbpOpenIddictDomainModule),
    typeof(BlobStoringDatabaseDomainModule)    )]
public class STNGDomainModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<Volo.Abp.Timing.AbpClockOptions>(options => options.Kind = DateTimeKind.Utc);
        Configure<AbpMultiTenancyOptions>(options =>
        {
            options.IsEnabled = MultiTenancyConsts.IsEnabled;
        });



        context.Services.AddOptions<STNG.Authentication.EmailOtpOptions>()
            .Bind(context.Services.GetConfiguration().GetSection("EmailOtp"))
            .Validate(options => System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
                options, new System.ComponentModel.DataAnnotations.ValidationContext(options), null, true),
                "Invalid email OTP configuration.");

        context.Services.AddOptions<STNG.Families.OwnershipTransferOptions>()
            .Bind(context.Services.GetConfiguration().GetSection("Family:OwnershipTransfer"))
            .Validate(options => options.Lifetime > TimeSpan.Zero,
                "Ownership transfer lifetime must be positive.");

        context.Services.AddOptions<FileManagementOptions>()
            .Bind(context.Services.GetConfiguration().GetSection(FileManagementOptions.SectionName))
            .Validate(options => options.MaximumFileSizeBytes > 0, "Maximum file size must be positive.")
            .Validate(options => options.MaximumDiscoveryScanCount > 0, "Discovery scan limit must be positive.")
            .Validate(options => options.AllowedExtensions.Count > 0, "At least one file extension must be allowed.")
            .Validate(options => options.AllowedContentTypes.Count > 0, "At least one content type must be allowed.");
    }
}
