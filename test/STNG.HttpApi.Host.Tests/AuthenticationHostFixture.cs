using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using STNG.Authentication;
using STNG.EntityFrameworkCore;
using Volo.Abp.AspNetCore.Mvc.Libs;
using Volo.Abp.Data;
using Volo.Abp.Emailing;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;
using Xunit;

namespace STNG;

public class AuthenticationHostFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _connection = $"Server=(localdb)\\MSSQLLocalDB;Database=STNG_Auth_Tests_{Guid.NewGuid():N};Trusted_Connection=True;TrustServerCertificate=True";
    public HttpClient Client { get; private set; } = null!;
    public RecordingEmailSender Email { get; } = new();
    public TestClock Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _connection,
            ["AuthServer:Authority"] = "https://localhost:44390",
            ["AuthServer:RequireHttpsMetadata"] = "true",
            ["Authentication:RequestsPerMinute"] = "1000",
            ["Serilog:MinimumLevel:Default"] = "Warning",
            ["Serilog:MinimumLevel:Override:OpenIddict"] = "Warning",
            ["OpenIddict:Applications:STNG_App:ClientId"] = "",
            ["OpenIddict:Applications:STNG_Swagger:ClientId"] = ""
        }));
        builder.ConfigureTestServices(services =>
        {
            services.Configure<AbpDbConnectionOptions>(options => options.ConnectionStrings.Default = _connection);
            services.Configure<AbpMvcLibsOptions>(options =>
            {
                options.CheckLibs = false;
            });
            services.Configure<EmailOtpOptions>(options => options.HashKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            services.AddSingleton<IEmailSender>(Email);
            services.AddSingleton<Volo.Abp.Timing.IClock>(Clock);
        });
    }

    public async Task InitializeAsync()
    {
        STNGEfCoreEntityExtensionMappings.Configure();

        await using (var db = new STNGDbContext(
            new DbContextOptionsBuilder<STNGDbContext>()
                .UseSqlServer(_connection)
                .Options))
        {
            await db.Database.MigrateAsync();
        }

        Client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost:44390"),
            AllowAutoRedirect = false
        });

        await InUnitAsync(async sp =>
        {
            await sp.GetRequiredService<IDataSeeder>().SeedAsync();
        }, transactional: false);
    }

    public async Task InUnitAsync(Func<IServiceProvider, Task> action, bool transactional = true)
    {
        using var scope = Services.CreateScope();
        using var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: transactional);
        await action(scope.ServiceProvider);
        await unit.CompleteAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (Client is not null)
        {
            Client.Dispose();
        }

        await DisposeAsync();
        await using var db = new STNGDbContext(new DbContextOptionsBuilder<STNGDbContext>().UseSqlServer(_connection).Options);
        await db.Database.EnsureDeletedAsync();
    }
}

[CollectionDefinition("Authentication host", DisableParallelization = true)]
public class AuthenticationHostCollection : ICollectionFixture<AuthenticationHostFixture> { }
