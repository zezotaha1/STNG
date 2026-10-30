namespace STNG.Families;

// Persisted values are explicit; changing them requires a data migration.
public enum FamilyStatus { Active = 0, Archiving = 1, Archived = 2, RecoveryRequired = 3 }
public enum FamilyMemberRole { Owner = 0, Admin = 1, Member = 2 }
public enum FamilyMemberStatus { Active = 0, Left = 1, Removed = 2 }
public enum OwnershipTransferStatus { Pending = 0, Accepted = 1, Declined = 2, Cancelled = 3, Expired = 4 }
public enum FamilyMembershipChange { Joined = 0, Left = 1, Removed = 2, Reactivated = 3, Promoted = 4, Demoted = 5 }

public static class FamilyConsts
{
    public const int MaxNameLength = 120;
    public const int DefaultTransferLifetimeHours = 48;
    public const int MaxPageSize = 100;
}

public static class FamilyErrorCodes
{
    public const string NotFound = "STNG.Family:1001";
    public const string NotActive = "STNG.Family:1002";
    public const string InvalidName = "STNG.Family:1003";
    public const string Archived = "STNG.Family:1004";
    public const string ConcurrencyConflict = "STNG.Family:1005";
    public const string AlreadyMember = "STNG.Family:2001";
    public const string MembershipNotFound = "STNG.Family:2002";
    public const string MembershipInactive = "STNG.Family:2003";
    public const string InsufficientRole = "STNG.Family:2004";
    public const string OwnerCannotLeave = "STNG.Family:2005";
    public const string CannotRemoveOwner = "STNG.Family:2006";
    public const string InvalidRoleTransition = "STNG.Family:2007";
    public const string ParentNotEligible = "STNG.Family:2008";
    public const string TransferAlreadyPending = "STNG.Family:3001";
    public const string InvalidOwnershipRecipient = "STNG.Family:3002";
    public const string TransferExpired = "STNG.Family:3003";
    public const string TransferNotPending = "STNG.Family:3004";
    public const string TransferConflict = "STNG.Family:3005";
}
