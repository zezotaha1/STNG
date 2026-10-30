using Localization.Resources.AbpUi;
using STNG.Localization;
using Volo.Abp.Account;
using Volo.Abp.SettingManagement;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement.HttpApi;
using Volo.Abp.Localization;
using System.Net;
using STNG.Families;
using Volo.Abp.AspNetCore.ExceptionHandling;

namespace STNG;

 [DependsOn(
    typeof(STNGApplicationContractsModule),
    typeof(AbpPermissionManagementHttpApiModule),
    typeof(AbpSettingManagementHttpApiModule),
    typeof(AbpAccountHttpApiModule),
    typeof(AbpIdentityHttpApiModule),
    typeof(AbpFeatureManagementHttpApiModule)
    )]
public class STNGHttpApiModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        ConfigureLocalization();
        Configure<AbpExceptionHttpStatusCodeOptions>(options =>
        {
            options.Map(FamilyErrorCodes.NotFound, HttpStatusCode.NotFound);
            options.Map(FamilyErrorCodes.MembershipNotFound, HttpStatusCode.NotFound);
            options.Map(FamilyErrorCodes.InvalidName, HttpStatusCode.BadRequest);
            foreach (var code in new[] { FamilyErrorCodes.MembershipInactive, FamilyErrorCodes.InsufficientRole, FamilyErrorCodes.ParentNotEligible })
                options.Map(code, HttpStatusCode.Forbidden);
            foreach (var code in new[] { FamilyErrorCodes.NotActive, FamilyErrorCodes.Archived, FamilyErrorCodes.ConcurrencyConflict,
                FamilyErrorCodes.AlreadyMember, FamilyErrorCodes.OwnerCannotLeave, FamilyErrorCodes.CannotRemoveOwner,
                FamilyErrorCodes.InvalidRoleTransition, FamilyErrorCodes.TransferAlreadyPending, FamilyErrorCodes.InvalidOwnershipRecipient,
                FamilyErrorCodes.TransferExpired, FamilyErrorCodes.TransferNotPending, FamilyErrorCodes.TransferConflict })
                options.Map(code, HttpStatusCode.Conflict);
        });
    }

    private void ConfigureLocalization()
    {
        Configure<AbpLocalizationOptions>(options =>
        {
            options.Resources
                .Get<STNGResource>()
                .AddBaseTypes(
                    typeof(AbpUiResource)
                );
        });
    }
}
