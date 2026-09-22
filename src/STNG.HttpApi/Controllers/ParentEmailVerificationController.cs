using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using STNG.Authentication;
using Volo.Abp.Auditing;

namespace STNG.Controllers;

[AllowAnonymous, DisableAuditing]
[Route("api/auth/parent")]
public class ParentEmailVerificationController : STNGController
{
    private readonly IParentEmailVerificationAppService _verification;
    public ParentEmailVerificationController(IParentEmailVerificationAppService verification) => _verification = verification;

    [HttpPost("verify-email")]
    public Task<EmailVerificationResultDto> VerifyAsync(VerifyParentEmailDto input) => _verification.VerifyAsync(input);

    [HttpPost("resend-verification-code")]
    public Task ResendAsync(ResendVerificationCodeDto input) => _verification.ResendAsync(input);
}
