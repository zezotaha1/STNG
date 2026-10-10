using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace STNG.Authentication;

public interface IParentEmailVerificationAppService : IApplicationService
{
    Task<EmailVerificationResultDto> VerifyAsync(VerifyParentEmailDto input);
    Task ResendAsync(ResendVerificationCodeDto input);
}
