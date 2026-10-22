using STNG.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;

namespace STNG.Permissions;

public class STNGPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(STNGPermissions.GroupName);
        var files = group.AddPermission(STNGPermissions.Files.Default, L("Permission:Files"));
        files.AddChild(STNGPermissions.Files.Upload, L("Permission:Files.Upload"));
        files.AddChild(STNGPermissions.Files.Attach, L("Permission:Files.Attach"));
        files.AddChild(STNGPermissions.Files.Detach, L("Permission:Files.Detach"));
        files.AddChild(STNGPermissions.Files.Replace, L("Permission:Files.Replace"));
        files.AddChild(STNGPermissions.Files.Delete, L("Permission:Files.Delete"));
        files.AddChild(STNGPermissions.Files.ChangeVisibility, L("Permission:Files.ChangeVisibility"));
        files.AddChild(STNGPermissions.Files.Publish, L("Permission:Files.Publish"));
        files.AddChild(STNGPermissions.Files.ManageAll, L("Permission:Files.ManageAll"));
        files.AddChild(STNGPermissions.Files.Maintenance, L("Permission:Files.Maintenance"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<STNGResource>(name);
    }
}
