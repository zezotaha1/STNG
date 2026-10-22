namespace STNG.Permissions;

public static class STNGPermissions
{
    public const string GroupName = "STNG";

    public static class Files
    {
        public const string Default = GroupName + ".Files";
        public const string Upload = Default + ".Upload";
        public const string Attach = Default + ".Attach";
        public const string Detach = Default + ".Detach";
        public const string Replace = Default + ".Replace";
        public const string Delete = Default + ".Delete";
        public const string ChangeVisibility = Default + ".ChangeVisibility";
        public const string Publish = Default + ".Publish";
        public const string ManageAll = Default + ".ManageAll";
        public const string Maintenance = Default + ".Maintenance";
    }
}
