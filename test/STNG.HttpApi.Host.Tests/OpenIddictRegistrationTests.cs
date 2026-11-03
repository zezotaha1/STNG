using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Shouldly;
using STNG.Authentication;
using STNG.EntityFrameworkCore;
using STNG.OpenIddict;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.OpenIddict.Applications;
using Xunit;

namespace STNG;

[CollectionDefinition("OpenIddict registration", DisableParallelization = true)]
public class OpenIddictRegistrationCollection { }

[Collection("OpenIddict registration")]
public class OpenIddictRegistrationTests
{
    private const string SwaggerClientId = "STNG_Swagger";
    private const string RootUrlKey = "OpenIddict:Applications:STNG_Swagger:RootUrl";
    private const string RootUrlVariable = "OpenIddict__Applications__STNG_Swagger__RootUrl";
    private const string PublicRoot = "https://zezotaha.ddns.net:44391";
    private const string LocalRoot = "https://localhost:44391";

    [Fact]
    public async Task Development_migrator_configuration_repairs_existing_client_without_duplication_or_mobile_changes()
    {
        await WithHostAsync(LocalRoot, async host =>
        {
            var original = await ReadClientAsync(host, SwaggerClientId);
            AssertRegistration(original, LocalRoot);
            var mobile = await ReadClientAsync(host, ParentAuthenticationConsts.MobileClientId);

            // Bind the actual checked-in Development step's environment setting
            // over DbMigrator's shipped defaults, using the .NET env provider.
            var configuredRoot = ReadDevelopmentMigratorRootUrl();
            await ReseedAsync(host, configuredRoot);
            await ReseedAsync(host, configuredRoot);

            var updated = await ReadClientAsync(host, SwaggerClientId);
            AssertRegistration(updated, PublicRoot);
            updated.Id.ShouldBe(original.Id);
            await host.InUnitAsync(async services =>
            {
                var db = await services.GetRequiredService<IDbContextProvider<STNGDbContext>>().GetDbContextAsync();
                (await db.Set<OpenIddictApplication>().CountAsync(x => x.ClientId == SwaggerClientId)).ShouldBe(1);
            }, transactional: false);

            var mobileAfter = await ReadClientAsync(host, ParentAuthenticationConsts.MobileClientId);
            mobileAfter.Id.ShouldBe(mobile.Id);
            mobileAfter.ApplicationType.ShouldBe(mobile.ApplicationType);
            mobileAfter.ClientType.ShouldBe(mobile.ClientType);
            mobileAfter.ClientUri.ShouldBe(mobile.ClientUri);
            mobileAfter.RedirectUris.ShouldBe(mobile.RedirectUris);
            Permissions(mobileAfter).ShouldBe(Permissions(mobile));

            // Exercise the real OpenIddict pipeline after reseeding, including
            // its application cache and strict redirect URI validation.
            await AssertAuthorizationRedirectAsync(host, PublicRoot, HttpStatusCode.Redirect);
            await AssertAuthorizationRedirectAsync(host, LocalRoot, HttpStatusCode.BadRequest);
        });
    }

    [Fact]
    public async Task New_public_swagger_client_has_exact_callback_and_rejects_unregistered_redirects()
    {
        await WithHostAsync(PublicRoot + "/", async host =>
        {
            AssertRegistration(await ReadClientAsync(host, SwaggerClientId), PublicRoot);
            await AssertAuthorizationRedirectAsync(host, PublicRoot, HttpStatusCode.Redirect);
            await AssertAuthorizationRedirectAsync(host, "https://unregistered.example.invalid", HttpStatusCode.BadRequest);
        });
    }

    [Theory]
    [InlineData("https://production.example.invalid/")]
    [InlineData("https://localhost:44391/")]
    public async Task Explicit_other_environment_root_is_preserved(string root)
    {
        await WithHostAsync(root, async host =>
        {
            var original = await ReadClientAsync(host, SwaggerClientId);
            AssertRegistration(original, root.TrimEnd('/'));
            await ReseedAsync(host, root);
            var updated = await ReadClientAsync(host, SwaggerClientId);
            AssertRegistration(updated, root.TrimEnd('/'));
            updated.Id.ShouldBe(original.Id);
        });
    }

    private static void AssertRegistration(OpenIddictApplication client, string root)
    {
        client.ApplicationType.ShouldBe("web");
        client.ClientType.ShouldBe("public");
        client.ClientUri.ShouldBe(root + "/swagger");
        JsonSerializer.Deserialize<string[]>(client.RedirectUris!)!.ShouldBe(new[] { root + "/swagger/oauth2-redirect.html" });
        var permissions = Permissions(client);
        permissions.ShouldContain(OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode);
        permissions.ShouldNotContain(OpenIddictConstants.Permissions.GrantTypes.Password);
        permissions.ShouldNotContain(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials);
    }

    private static string[] Permissions(OpenIddictApplication client) =>
        JsonSerializer.Deserialize<string[]>(client.Permissions!)!.OrderBy(x => x, StringComparer.Ordinal).ToArray();

    private static async Task AssertAuthorizationRedirectAsync(AuthenticationHostFixture host, string root, HttpStatusCode expected)
    {
        var response = await host.Client.GetAsync("/connect/authorize?client_id=" + SwaggerClientId +
            "&response_type=code&scope=STNG&redirect_uri=" + Uri.EscapeDataString(root + "/swagger/oauth2-redirect.html"));
        response.StatusCode.ShouldBe(expected);
        if (expected == HttpStatusCode.Redirect)
        {
            response.Headers.Location.ShouldNotBeNull();
            response.Headers.Location!.OriginalString.ShouldContain("/Account/Login");
        }
        else
        {
            var body = await response.Content.ReadAsStringAsync();
            body.ShouldContain("invalid_request");
            body.ShouldContain("ID2043");
        }
    }

    private static async Task<OpenIddictApplication> ReadClientAsync(AuthenticationHostFixture host, string clientId)
    {
        OpenIddictApplication client = null!;
        await host.InUnitAsync(async services =>
        {
            var db = await services.GetRequiredService<IDbContextProvider<STNGDbContext>>().GetDbContextAsync();
            client = await db.Set<OpenIddictApplication>().AsNoTracking().SingleAsync(x => x.ClientId == clientId);
        }, transactional: false);
        return client;
    }

    private static Task ReseedAsync(AuthenticationHostFixture host, string root) => host.InUnitAsync(async services =>
    {
        services.GetRequiredService<IConfiguration>()[RootUrlKey] = root;
        await services.GetRequiredService<OpenIddictDataSeedContributor>().SeedAsync(new DataSeedContext());
    });

    private static async Task WithHostAsync(string root, Func<AuthenticationHostFixture, Task> test)
    {
        var host = new SwaggerRegistrationHost(root);
        try
        {
            await host.InitializeAsync();
            await test(host);
        }
        finally
        {
            await host.DisposeAsync();
            await DeleteOwnedDatabaseAsync(host.ConnectionString);
        }
    }

    private static async Task DeleteOwnedDatabaseAsync(string connectionString)
    {
        var target = new SqlConnectionStringBuilder(connectionString);
        const string prefix = "STNG_Auth_Tests_";
        if (target.DataSource != "(localdb)\\MSSQLLocalDB" || !target.InitialCatalog.StartsWith(prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(target.InitialCatalog[prefix.Length..], "N", out _))
            throw new InvalidOperationException("Cleanup requires this fixture's disposable LocalDB catalog.");

        // EF EnsureDeleted clears every SqlClient pool. Other test hosts can
        // still be alive; clear only the pool for this fixture's owned catalog.
        using (var pooled = new SqlConnection(connectionString)) SqlConnection.ClearPool(pooled);
        var catalog = target.InitialCatalog;
        target.InitialCatalog = "master";
        target.Pooling = false;
        await using var master = new SqlConnection(target.ConnectionString);
        await master.OpenAsync();
        await using var command = master.CreateCommand();
        var quotedCatalog = new SqlCommandBuilder().QuoteIdentifier(catalog);
        command.CommandText = "IF DB_ID(@catalog) IS NOT NULL BEGIN ALTER DATABASE " + quotedCatalog +
            " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + quotedCatalog + "; END";
        command.Parameters.AddWithValue("@catalog", catalog);
        await command.ExecuteNonQueryAsync();
    }

    private static string ReadDevelopmentMigratorRootUrl()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "STNG.slnx")))
            root = root.Parent ?? throw new DirectoryNotFoundException("Repository root");
        var workflow = File.ReadAllText(Path.Combine(root.FullName, ".github", "workflows", "development.yml"));
        var start = workflow.IndexOf("      - name: Run STNG DbMigrator", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        var end = workflow.IndexOf("        run: |", start, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start);
        var setting = Regex.Match(workflow[start..end], @"^\s+" + RootUrlVariable + @":\s*(\S+)\s*$", RegexOptions.Multiline);
        // A unique prefix exercises env binding without changing any host's
        // process-wide OpenIddict setting while other tests may be running.
        var prefix = "STNG_Registration_Test_" + Guid.NewGuid().ToString("N") + "_";
        var variable = prefix + RootUrlVariable;
        try
        {
            if (setting.Success) Environment.SetEnvironmentVariable(variable, setting.Groups[1].Value.Trim('\'', '"'));
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(root.FullName, "src", "STNG.DbMigrator", "appsettings.json"))
                .AddEnvironmentVariables(prefix).Build();
            return configuration[RootUrlKey]!;
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    private sealed class SwaggerRegistrationHost(string root) : AuthenticationHostFixture
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenIddict:Applications:STNG_Swagger:ClientId"] = SwaggerClientId,
                [RootUrlKey] = root
            }));
        }
    }
}
