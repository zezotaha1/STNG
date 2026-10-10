using Volo.Abp.Auditing;

namespace STNG.Authentication;

[DisableAuditing]
public class ResendVerificationCodeDto
{
    public string Email { get; set; } = "";
}

[DisableAuditing]
public class VerifyParentEmailDto : ResendVerificationCodeDto
{
    public string Otp { get; set; } = "";
}

public class EmailVerificationResultDto
{
    public bool Verified { get; set; }
}
