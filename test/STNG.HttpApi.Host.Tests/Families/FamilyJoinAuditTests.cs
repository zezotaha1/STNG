using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using STNG.EntityFrameworkCore;
using STNG.Families;
using Volo.Abp;
using Volo.Abp.AuditLogging;
using Volo.Abp.Auditing;
using Volo.Abp.EntityFrameworkCore;
using Xunit;

namespace STNG;

[Collection("Family join migration")]
public class FamilyJoinAuditTests
{
    private sealed class AuditHost : AuthenticationHostFixture
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.Configure<AbpAuditingOptions>(options =>
            {
                options.IsEnabled = true;
                options.HideErrors = false;
                options.EntityHistorySelectors.Add(new NamedTypeSelector("Family credential review", type =>
                    type == typeof(Family) || type == typeof(FamilyJoinCredential)));
            }));
        }
    }

    [Fact]
    public async Task Issuance_and_successful_or_failed_submission_do_not_write_capabilities_or_hashes_to_abp_audits()
    {
        var host = new AuditHost();
        try
        {
            await host.InitializeAsync();
            var api = new FamilyJoinTestClient(host);
            var owner = await api.Core.ParentAsync(); var applicant = await api.Core.ParentAsync();
            var control = Guid.NewGuid(); var family = await api.Core.CreateAsync(owner, control);
            var issued = await api.IssueAsync(owner, family.Id);
            await api.SubmitAsync(applicant, issued);
            (await api.Core.SendAsync(applicant, HttpMethod.Post, "/api/family-join-requests",
                new SubmitFamilyJoinRequestDto { RequestId = Guid.NewGuid(), Kind = FamilyJoinCredentialKind.QRToken, Secret = issued.Secret! }))
                .StatusCode.ShouldBe(HttpStatusCode.NotFound);
            await host.InUnitAsync(async sp =>
            {
                var db = await sp.GetRequiredService<IDbContextProvider<STNGDbContext>>().GetDbContextAsync();
                // Positive controls ensure these are active auditing/history paths,
                // rather than vacuous assertions over disabled or empty audit storage.
                (await db.Set<AuditLogAction>().AnyAsync(x => x.Parameters.Contains(control.ToString()))).ShouldBeTrue();
                (await db.Set<EntityChange>().AnyAsync(x => x.EntityTypeFullName == typeof(Family).FullName)).ShouldBeTrue();
                (await db.Set<AuditLogAction>().AnyAsync(x => x.Parameters.Contains(issued.Secret!))).ShouldBeFalse();
                (await db.Set<EntityChange>().AnyAsync(x => x.EntityTypeFullName == typeof(FamilyJoinCredential).FullName)).ShouldBeFalse();
                // Assertions use booleans so even a regression cannot echo secrets.
                var hash = (await db.FamilyJoinCredentials.SingleAsync(x => x.Id == issued.Credential.Id)).SecretHash;
                (await db.Set<EntityPropertyChange>().AnyAsync(x =>
                    (x.NewValue != null && (x.NewValue.Contains(issued.Secret!) || x.NewValue.Contains(hash))) ||
                    (x.OriginalValue != null && (x.OriginalValue.Contains(issued.Secret!) || x.OriginalValue.Contains(hash)))))
                    .ShouldBeFalse();
            }, transactional: false);
        }
        finally { await ((IAsyncLifetime)host).DisposeAsync(); }
    }
}
