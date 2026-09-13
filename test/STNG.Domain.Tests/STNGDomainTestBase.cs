using Volo.Abp.Modularity;

namespace STNG;

/* Inherit from this class for your domain layer tests. */
public abstract class STNGDomainTestBase<TStartupModule> : STNGTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
