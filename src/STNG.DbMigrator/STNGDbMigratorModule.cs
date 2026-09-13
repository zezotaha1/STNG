using STNG.EntityFrameworkCore;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace STNG.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(STNGEntityFrameworkCoreModule),
    typeof(STNGApplicationContractsModule)
)]
public class STNGDbMigratorModule : AbpModule
{
}
