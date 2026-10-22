namespace STNG.FileManagement;

public enum FileVisibility
{
    OwnerOnly = 0,
    Restricted = 1,
    Public = 2
}

public enum FileAssetStatus
{
    Pending = 0,
    Validating = 1,
    Processing = 2,
    Ready = 3,
    Rejected = 4,
    Failed = 5,
    Deleted = 6
}

public enum FileAccessOperation
{
    Read = 0,
    Discover = 1,
    Upload = 2,
    Attach = 3,
    Detach = 4,
    Replace = 5,
    Delete = 6,
    ChangeVisibility = 7
}

public enum FileScanResult
{
    Clean = 0,
    ThreatDetected = 1,
    Failed = 2
}
