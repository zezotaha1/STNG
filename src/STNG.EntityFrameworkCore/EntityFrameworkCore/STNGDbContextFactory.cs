using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace STNG.EntityFrameworkCore;

/* This class is needed for EF Core console commands
 * (like Add-Migration and Update-Database commands) */
public class STNGDbContextFactory : IDesignTimeDbContextFactory<STNGDbContext>
{
    public STNGDbContext CreateDbContext(string[] args)
    {
        var configuration = BuildConfiguration();
        
        STNGEfCoreEntityExtensionMappings.Configure();

        var builder = new DbContextOptionsBuilder<STNGDbContext>()
            .UseSqlServer(configuration.GetConnectionString("Default"));
        
        return new STNGDbContext(builder.Options);
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../STNG.DbMigrator/"))
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables();

        return builder.Build();
    }
}
