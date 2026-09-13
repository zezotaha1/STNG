using Volo.Abp.Modularity;

namespace STNG;

[DependsOn(
    typeof(STNGDomainModule),
    typeof(STNGTestBaseModule)
)]
public class STNGDomainTestModule : AbpModule
{

}
