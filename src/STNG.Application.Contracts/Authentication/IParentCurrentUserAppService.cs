using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace STNG.Authentication;

public interface IParentCurrentUserAppService : IApplicationService
{
    Task<ParentCurrentUserDto> GetAsync();
}
