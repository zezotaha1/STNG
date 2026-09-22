using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace STNG.Authentication;

public interface IParentRegistrationAppService : IApplicationService
{
    Task RegisterAsync(RegisterParentDto input);
}
