using STNG.Localization;
using Volo.Abp.Application.Services;

namespace STNG;

/* Inherit your application services from this class.
 */
public abstract class STNGAppService : ApplicationService
{
    protected STNGAppService()
    {
        LocalizationResource = typeof(STNGResource);
    }
}
