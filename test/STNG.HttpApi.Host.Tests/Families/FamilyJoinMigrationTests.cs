using System;
using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using STNG.EntityFrameworkCore;
using STNG.FileManagement;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace STNG;

[CollectionDefinition("Family join migration", DisableParallelization = true)]
public class FamilyJoinMigrationCollection { }

[Collection("Family join migration")]
public class FamilyJoinMigrationTests
{
    private const string Develop = "20261027193809_AddFamilyCore";
    private const string Feature = "20261030154452_AddFamilyJoinRequests";
    private readonly ITestOutputHelper _output;
    public FamilyJoinMigrationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Develop_upgrade_rollback_and_generated_sql_reupgrade_preserve_existing_parent_family_and_file_data()
    {
        // A separate fixture owns this entire database. No other test's rows or
        // shared/server configuration are involved in this destructive round trip.
        var host = new AuthenticationHostFixture();
        try
        {
            await host.InitializeAsync();
            string connection = null!;
            await host.InUnitAsync(async sp => connection = (await sp.GetRequiredService<IDbContextProvider<STNGDbContext>>()
                .GetDbContextAsync()).Database.GetConnectionString()!, transactional: false);
            var target = new SqlConnectionStringBuilder(connection);
            target.DataSource.ShouldBe("(localdb)\\MSSQLLocalDB");
            target.InitialCatalog.ShouldStartWith("STNG_Auth_Tests_");
            await using var db = new STNGDbContext(new DbContextOptionsBuilder<STNGDbContext>().UseSqlServer(connection).Options);
            var migrator = db.GetService<IMigrator>();
            var migrations = db.GetService<IMigrationsAssembly>();
            var designer = migrations.CreateMigration(migrations.Migrations[Feature], db.Database.ProviderName!);
            var designerModel = db.GetService<IModelRuntimeInitializer>().Initialize(designer.TargetModel, designTime: true);
            db.GetService<IMigrationsModelDiffer>().HasDifferences(designerModel.GetRelationalModel(),
                db.GetService<IDesignTimeModel>().Model.GetRelationalModel()).ShouldBeFalse();
            _output.WriteLine("Full migration Designer target model exactly matches the current relational model.");
            (await db.Database.GetAppliedMigrationsAsync()).Last().ShouldBe(Feature);
            await migrator.MigrateAsync(Develop);
            (await db.Database.GetAppliedMigrationsAsync()).Last().ShouldBe(Develop);
            await db.Database.OpenConnectionAsync();
            var sql = db.Database.GetDbConnection();
            (await ScalarAsync<int>(sql, "SELECT COUNT(*) FROM sys.tables WHERE name IN ('AppFamilyJoinCredentials','AppFamilyJoinRequests')")).ShouldBe(0);
            var baselineSchema = await SchemaAsync(sql);

            var api = new FamilyJoinTestClient(host);
            var owner = await api.Core.ParentAsync(); var applicant = await api.Core.ParentAsync();
            var family = await api.Core.CreateAsync(owner);
            var fileId = Guid.NewGuid();
            await host.InUnitAsync(async sp => await sp.GetRequiredService<IRepository<FileAsset, Guid>>().InsertAsync(new FileAsset(
                fileId, null, "migration-marker.txt", $"migration/{fileId:N}", "default", "text/plain", ".txt", 1, owner.UserId, owner.UserId)));
            var baselineData = await DataAsync(sql, family.Id, fileId);
            _output.WriteLine("Develop schema accepts existing Parent registration, Family creation and FileAsset persistence.");

            var up = migrator.GenerateScript(Develop, Feature, MigrationsSqlGenerationOptions.Idempotent);
            var down = migrator.GenerateScript(Feature, Develop);
            Regex.Matches(up, @"CREATE TABLE \[").Count.ShouldBe(2);
            up.ShouldNotContain("DROP TABLE");
            Regex.Matches(up, @"CREATE (UNIQUE )?INDEX \[").Count.ShouldBe(11);
            down.IndexOf("DROP TABLE [AppFamilyJoinRequests]", StringComparison.Ordinal)
                .ShouldBeLessThan(down.IndexOf("DROP TABLE [AppFamilyJoinCredentials]", StringComparison.Ordinal));
            Regex.Matches(down, @"DROP TABLE \[").Count.ShouldBe(2);

            await migrator.MigrateAsync(Feature);
            await VerifyJoinSchemaAsync(sql);
            (await DataAsync(sql, family.Id, fileId)).ShouldBe(baselineData);
            db.Database.HasPendingModelChanges().ShouldBeFalse();
            await api.SubmitAsync(applicant, await api.IssueAsync(owner, family.Id));
            (await ScalarAsync<int>(sql, "SELECT COUNT(*) FROM AppFamilyJoinRequests")).ShouldBe(1);
            var beforeDown = await DataAsync(sql, family.Id, fileId);
            _output.WriteLine("Develop -> feature: 2 tables, 31 columns, 7 restrictive FKs, 3 keys and 11 explicit indexes verified; legitimate join submission works.");

            await migrator.MigrateAsync(Develop);
            (await SchemaAsync(sql)).ShouldBe(baselineSchema);
            (await DataAsync(sql, family.Id, fileId)).ShouldBe(beforeDown);
            (await db.Database.GetAppliedMigrationsAsync()).Last().ShouldBe(Develop);
            _output.WriteLine("Feature -> develop: original schema and Parent/Family/member/history/FileAsset rows preserved; join rows intentionally removed by Down().");

            // Execute the actual generated deployment SQL, twice, against the
            // established baseline using one connection for transaction/GO batches.
            await ExecuteScriptAsync(sql, up);
            await ExecuteScriptAsync(sql, up);
            await VerifyJoinSchemaAsync(sql);
            (await DataAsync(sql, family.Id, fileId)).ShouldBe(beforeDown);
            (await db.Database.GetAppliedMigrationsAsync()).Last().ShouldBe(Feature);
            (await ScalarAsync<int>(sql, "SELECT COUNT(*) FROM AppFamilyJoinRequests")).ShouldBe(0);
            db.Database.HasPendingModelChanges().ShouldBeFalse();
            _output.WriteLine("Generated idempotent SQL applies from develop and replays safely; no pending model changes. Disposable database deleted by fixture cleanup.");
        }
        finally { await ((IAsyncLifetime)host).DisposeAsync(); }
    }

    private static async Task VerifyJoinSchemaAsync(DbConnection sql)
    {
        (await ScalarAsync<int>(sql, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('AppFamilyJoinCredentials')")).ShouldBe(16);
        (await ScalarAsync<int>(sql, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('AppFamilyJoinRequests')")).ShouldBe(15);
        (await ScalarAsync<int>(sql, "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID('AppFamilyJoinCredentials'),OBJECT_ID('AppFamilyJoinRequests')) AND delete_referential_action=0 AND is_disabled=0 AND is_not_trusted=0")).ShouldBe(7);
        (await ScalarAsync<int>(sql, "SELECT COUNT(*) FROM sys.key_constraints WHERE parent_object_id IN (OBJECT_ID('AppFamilyJoinCredentials'),OBJECT_ID('AppFamilyJoinRequests'))")).ShouldBe(3);
        (await ScalarAsync<int>(sql, "SELECT COUNT(*) FROM sys.indexes WHERE object_id IN (OBJECT_ID('AppFamilyJoinCredentials'),OBJECT_ID('AppFamilyJoinRequests')) AND is_primary_key=0 AND is_unique_constraint=0 AND index_id>0")).ShouldBe(11);
        (await ScalarAsync<int>(sql, "SELECT COUNT(*) FROM sys.indexes WHERE name IN ('IX_AppFamilyJoinCredentials_ActiveKind','IX_AppFamilyJoinRequests_PendingParent') AND is_unique=1 AND has_filter=1")).ShouldBe(2);
        (await ScalarAsync<int>(sql, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('AppFamilyJoinCredentials') AND name='SecretHash' AND TYPE_NAME(user_type_id)='varchar' AND max_length=64 AND is_nullable=0")).ShouldBe(1);
        (await ScalarAsync<int>(sql, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('AppFamilyJoinRequests') AND name='RejectionReason' AND TYPE_NAME(user_type_id)='nvarchar' AND max_length=512 AND is_nullable=1")).ShouldBe(1);
    }

    private static async Task ExecuteScriptAsync(DbConnection sql, string script)
    {
        foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            await using var command = sql.CreateCommand(); command.CommandText = batch;
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<T> ScalarAsync<T>(DbConnection sql, string query)
    {
        await using var command = sql.CreateCommand(); command.CommandText = query;
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static Task<string> DataAsync(DbConnection sql, Guid family, Guid file) => ScalarAsync<string>(sql, $"""
        SELECT
          (SELECT Id,UserId,IsDeleted FROM AppParents ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES) AS Parents,
          (SELECT Id,Name,CreatedByParentId,Version,ConcurrencyStamp FROM AppFamilies WHERE Id='{family:D}' FOR JSON PATH) AS Families,
          (SELECT Id,ParentId,Role,Status FROM AppFamilyMembers WHERE FamilyId='{family:D}' ORDER BY Id FOR JSON PATH) AS Members,
          (SELECT Id,ParentId,Change,Role FROM AppFamilyMembershipHistory WHERE FamilyId='{family:D}' ORDER BY Id FOR JSON PATH) AS History,
          (SELECT Id,StorageKey,Status,IsDeleted FROM AppFileAssets WHERE Id='{file:D}' FOR JSON PATH) AS Files
        FOR JSON PATH,WITHOUT_ARRAY_WRAPPER
        """);

    private static Task<string> SchemaAsync(DbConnection sql) => ScalarAsync<string>(sql, """
        SELECT t.name,
          (SELECT c.name,TYPE_NAME(c.user_type_id) AS type,c.max_length,c.precision,c.scale,c.is_nullable,
             dc.definition AS defaultValue
           FROM sys.columns c LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id
           WHERE c.object_id=t.object_id ORDER BY c.column_id FOR JSON PATH,INCLUDE_NULL_VALUES) AS columns,
          (SELECT i.name,i.is_unique,i.is_primary_key,i.is_unique_constraint,i.filter_definition,
             (SELECT c.name,ic.key_ordinal,ic.is_descending_key,ic.is_included_column
              FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
              WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id ORDER BY ic.index_column_id FOR JSON PATH) AS fields
           FROM sys.indexes i WHERE i.object_id=t.object_id AND i.index_id>0 ORDER BY i.name FOR JSON PATH,INCLUDE_NULL_VALUES) AS indexes,
          (SELECT fk.name,OBJECT_NAME(fk.referenced_object_id) AS principal,fk.delete_referential_action,fk.update_referential_action,
             (SELECT COL_NAME(fkc.parent_object_id,fkc.parent_column_id) AS dependent,COL_NAME(fkc.referenced_object_id,fkc.referenced_column_id) AS principal
              FROM sys.foreign_key_columns fkc WHERE fkc.constraint_object_id=fk.object_id ORDER BY fkc.constraint_column_id FOR JSON PATH) AS fields
           FROM sys.foreign_keys fk WHERE fk.parent_object_id=t.object_id ORDER BY fk.name FOR JSON PATH) AS foreignKeys
        FROM sys.tables t WHERE t.is_ms_shipped=0 ORDER BY t.name FOR JSON PATH
        """);
}
