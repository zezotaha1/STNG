namespace STNG.Families;

public enum FamilyJoinCredentialKind { ManualCode = 0, QRToken = 1 }
public enum FamilyJoinCredentialStatus { Active = 0, Revoked = 1, Expired = 2 }
public enum FamilyJoinRequestStatus { Pending = 0, Approved = 1, Rejected = 2, Cancelled = 3 }

public static class FamilyJoinConsts
{
    public const int ManualCodeLength = 20;
    public const int QRTokenLength = 43;
    public const int MaxSecretInputLength = 128;
    public const int HashLength = 64;
    public const int MaxRejectionReasonLength = 256;
    public const string SubmissionRateLimitPolicy = "FamilyJoinSubmission";
}

public static class FamilyJoinErrorCodes
{
    public const string Unavailable = "STNG.Family.Join:1001";
    public const string PendingRequestExists = "STNG.Family.Join:1002";
    public const string InvalidTransition = "STNG.Family.Join:1003";
    public const string CannotReviewOwnRequest = "STNG.Family.Join:1004";
    public const string RequestConflict = "STNG.Family.Join:1005";
    public const string AttemptsLimited = "STNG.Family.Join:1006";
    public const string InvalidInput = "STNG.Family.Join:1007";
}
