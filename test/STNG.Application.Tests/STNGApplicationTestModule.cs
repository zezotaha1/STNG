using Volo.Abp.Modularity;

namespace STNG;

[DependsOn(
    typeof(STNGApplicationModule),
    typeof(STNGDomainTestModule)
)]
public class STNGApplicationTestModule : AbpModule
{

}
