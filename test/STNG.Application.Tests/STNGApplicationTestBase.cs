using Volo.Abp.Modularity;

namespace STNG;

public abstract class STNGApplicationTestBase<TStartupModule> : STNGTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
