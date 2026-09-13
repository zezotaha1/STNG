using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace STNG.Data;

/* This is used if database provider does't define
 * ISTNGDbSchemaMigrator implementation.
 */
public class NullSTNGDbSchemaMigrator : ISTNGDbSchemaMigrator, ITransientDependency
{
    public Task MigrateAsync()
    {
        return Task.CompletedTask;
    }
}
