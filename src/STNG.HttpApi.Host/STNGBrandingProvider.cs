using Microsoft.Extensions.Localization;
using STNG.Localization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Ui.Branding;

namespace STNG;

[Dependency(ReplaceServices = true)]
public class STNGBrandingProvider : DefaultBrandingProvider
{
    private IStringLocalizer<STNGResource> _localizer;

    public STNGBrandingProvider(IStringLocalizer<STNGResource> localizer)
    {
        _localizer = localizer;
    }

    public override string AppName => _localizer["AppName"];
}
