namespace STNG.FileManagement;

public static class FileManagementConsts
{
    public const int MaxFileNameLength = 255;
    public const int MaxStorageKeyLength = 320;
    public const int MaxStorageContainerLength = 64;
    public const int MaxContentTypeLength = 128;
    public const int MaxExtensionLength = 16;
    public const int ContentHashLength = 64;
    public const int MaxFailureReasonLength = 512;
    public const int MaxResourceTypeLength = 128;
    public const int MaxResourceIdLength = 128;
    public const int MaxPurposeLength = 64;
    public const int MaxDiscoveryPageSize = 100;
}

public static class FileManagementErrorCodes
{
    public const string Prefix = "STNG:FileManagement:";
    public const string InvalidStateTransition = Prefix + "InvalidStateTransition";
    public const string FileNotReady = Prefix + "FileNotReady";
    public const string FileStillReferenced = Prefix + "FileStillReferenced";
    public const string DuplicateReference = Prefix + "DuplicateReference";
    public const string ReferenceNotFound = Prefix + "ReferenceNotFound";
    public const string RestrictedFileRequiresReference = Prefix + "RestrictedFileRequiresReference";
    public const string InvalidFileName = Prefix + "InvalidFileName";
    public const string ExtensionNotAllowed = Prefix + "ExtensionNotAllowed";
    public const string ContentTypeNotAllowed = Prefix + "ContentTypeNotAllowed";
    public const string FileTooLarge = Prefix + "FileTooLarge";
    public const string EmptyFile = Prefix + "EmptyFile";
    public const string ContentSignatureMismatch = Prefix + "ContentSignatureMismatch";
    public const string ScannerRequired = Prefix + "ScannerRequired";
    public const string ThreatDetected = Prefix + "ThreatDetected";
    public const string AccessDenied = Prefix + "AccessDenied";
    public const string CrossTenantAccessDenied = Prefix + "CrossTenantAccessDenied";
    public const string ResourcePolicyMissing = Prefix + "ResourcePolicyMissing";
    public const string PublicVisibilityRequiresPermission = Prefix + "PublicVisibilityRequiresPermission";
}
