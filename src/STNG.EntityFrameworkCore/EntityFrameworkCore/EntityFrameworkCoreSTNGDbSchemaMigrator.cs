using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using STNG.Data;
using Volo.Abp.DependencyInjection;

namespace STNG.EntityFrameworkCore;

public class EntityFrameworkCoreSTNGDbSchemaMigrator
    : ISTNGDbSchemaMigrator, ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;

    public EntityFrameworkCoreSTNGDbSchemaMigrator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task MigrateAsync()
    {
        /* We intentionally resolving the STNGDbContext
         * from IServiceProvider (instead of directly injecting it)
         * to properly get the connection string of the current tenant in the
         * current scope.
         */

        await _serviceProvider
            .GetRequiredService<STNGDbContext>()
            .Database
            .MigrateAsync();
    }
}
