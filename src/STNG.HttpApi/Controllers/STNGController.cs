using STNG.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace STNG.Controllers;

/* Inherit your controllers from this class.
 */
public abstract class STNGController : AbpControllerBase
{
    protected STNGController()
    {
        LocalizationResource = typeof(STNGResource);
    }
}
