using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using STNG.EntityFrameworkCore;
using STNG.Families;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Uow;
using Xunit;

namespace STNG;

[Collection("Authentication host")]
public class FamilyReviewRegressionTests
{
    private readonly AuthenticationHostFixture _host;
    private readonly FamilyTestClient _api;

    public FamilyReviewRegressionTests(AuthenticationHostFixture host)
    {
        _host = host;
        _api = new FamilyTestClient(host);
    }

    [Theory]
    [InlineData(true, "inactive")]
    [InlineData(false, "inactive")]
    [InlineData(true, "unverified")]
    [InlineData(false, "unverified")]
    [InlineData(true, "locked")]
    [InlineData(false, "locked")]
    public async Task Transfer_rechecks_persisted_eligibility_even_when_the_user_is_already_tracked(bool previousOwner, string change)
    {
        var owner = await _api.ParentAsync();
        var recipient = await _api.ParentAsync();
        var family = await _api.CreateAsync(owner);
        await _api.AddApprovedAsync(family.Id, owner, recipient);
        var transfer = await _api.InitiateAsync(owner, family, recipient);
        var subject = previousOwner ? owner : recipient;

        await _host.InUnitAsync(async sp =>
        {
            var aggregate = await sp.GetRequiredService<IRepository<Family, Guid>>().GetAsync(family.Id);
            var trackedUser = await sp.GetRequiredService<IRepository<IdentityUser, Guid>>().GetAsync(subject.UserId);
            trackedUser.IsActive.ShouldBeTrue();
            trackedUser.EmailConfirmed.ShouldBeTrue();

            // An independent, committed account change must override the older tracked snapshot.
            await _host.InUnitAsync(async other =>
            {
                var user = await other.GetRequiredService<IRepository<IdentityUser, Guid>>().GetAsync(subject.UserId);
                if (change == "inactive") user.SetIsActive(false);
                if (change == "unverified") user.SetEmailConfirmed(false);
                if (change == "locked")
                    (await other.GetRequiredService<IdentityUserManager>()
                        .SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddDays(1))).Succeeded.ShouldBeTrue();
            });

            var error = await Should.ThrowAsync<BusinessException>(() => sp.GetRequiredService<OwnershipTransferManager>()
                .AcceptAsync(aggregate, recipient.ParentId, transfer.Id));
            error.Code.ShouldBe(FamilyErrorCodes.ParentNotEligible);
        });

        await _host.InUnitAsync(async sp =>
        {
            var db = await sp.GetRequiredService<IDbContextProvider<STNGDbContext>>().GetDbContextAsync();
            (await db.FamilyMembers.SingleAsync(x => x.FamilyId == family.Id && x.Role == FamilyMemberRole.Owner))
                .ParentId.ShouldBe(owner.ParentId);
            (await db.OwnershipTransfers.SingleAsync(x => x.Id == transfer.Id)).Status.ShouldBe(OwnershipTransferStatus.Pending);
        });
    }

    [Fact]
    public async Task Demotion_revokes_administration_with_the_existing_token_and_a_fresh_stamp()
    {
        var owner = await _api.ParentAsync();
        var admin = await _api.ParentAsync();
        var family = await _api.CreateAsync(owner);
        await _api.AddApprovedAsync(family.Id, owner, admin);
        var current = await _api.GetAsync(owner, family.Id);
        (await _api.SendAsync(owner, HttpMethod.Post, $"/api/families/{family.Id}/members/{admin.ParentId}/promote",
            FamilyTestClient.Stamp(current))).EnsureSuccessStatusCode();
        current = await _api.GetAsync(admin, family.Id);
        current.MyRole.ShouldBe(FamilyMemberRole.Admin);
        (await _api.SendAsync(owner, HttpMethod.Post, $"/api/families/{family.Id}/members/{admin.ParentId}/demote",
            FamilyTestClient.Stamp(current))).EnsureSuccessStatusCode();
        current = await _api.GetAsync(admin, family.Id);
        current.MyRole.ShouldBe(FamilyMemberRole.Member);
        var denied = await _api.SendAsync(admin, HttpMethod.Put, $"/api/families/{family.Id}", new UpdateFamilyDto
        { Name = "Denied after demotion", ConcurrencyStamp = current.ConcurrencyStamp });
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync()).ShouldContain(FamilyErrorCodes.InsufficientRole);
        (await _api.GetAsync(owner, family.Id)).Name.ShouldBe("Test Family");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reactivation_preserves_the_member_identity_and_history_without_restoring_admin_access(bool removed)
    {
        var owner = await _api.ParentAsync();
        var member = await _api.ParentAsync();
        var family = await _api.CreateAsync(owner);
        await _api.AddApprovedAsync(family.Id, owner, member);
        var current = await _api.GetAsync(owner, family.Id);
        (await _api.SendAsync(owner, HttpMethod.Post, $"/api/families/{family.Id}/members/{member.ParentId}/promote",
            FamilyTestClient.Stamp(current))).EnsureSuccessStatusCode();
        var transfer = await _api.InitiateAsync(owner, family, member);
        current = await _api.GetAsync(owner, family.Id);
        var end = removed
            ? await _api.SendAsync(owner, HttpMethod.Post, $"/api/families/{family.Id}/members/{member.ParentId}/remove", FamilyTestClient.Stamp(current))
            : await _api.SendAsync(member, HttpMethod.Post, $"/api/families/{family.Id}/leave", FamilyTestClient.Stamp(current));
        end.EnsureSuccessStatusCode();
        (await _api.SendAsync(member, HttpMethod.Get, $"/api/families/{family.Id}/members")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var listed = await _api.SendAsync(owner, HttpMethod.Get, $"/api/families/{family.Id}/members");
        listed.EnsureSuccessStatusCode();
        var retained = (await listed.Content.ReadFromJsonAsync<PagedResultDto<FamilyMemberDto>>())!.Items
            .Single(x => x.ParentId == member.ParentId);
        retained.Status.ShouldBe(removed ? FamilyMemberStatus.Removed : FamilyMemberStatus.Left);
        await _api.AddApprovedAsync(family.Id, owner, member);
        (await _api.GetAsync(member, family.Id)).MyRole.ShouldBe(FamilyMemberRole.Member);

        await _host.InUnitAsync(async sp =>
        {
            var db = await sp.GetRequiredService<IDbContextProvider<STNGDbContext>>().GetDbContextAsync();
            var reactivated = await db.FamilyMembers.SingleAsync(x => x.FamilyId == family.Id && x.ParentId == member.ParentId);
            reactivated.Id.ShouldBe(retained.Id);
            reactivated.Status.ShouldBe(FamilyMemberStatus.Active);
            reactivated.Role.ShouldBe(FamilyMemberRole.Member);
            (await db.FamilyMembershipHistory.CountAsync(x => x.MemberId == retained.Id)).ShouldBe(4);
            (await db.OwnershipTransfers.SingleAsync(x => x.Id == transfer.Id)).Status.ShouldBe(OwnershipTransferStatus.Cancelled);
        });
    }

    [Fact]
    public async Task A_database_failure_after_ordered_updates_restores_the_owner_and_pending_transfer_before_outer_rollback()
    {
        var owner = await _api.ParentAsync();
        var recipient = await _api.ParentAsync();
        var family = await _api.CreateAsync(owner);
        await _api.AddApprovedAsync(family.Id, owner, recipient);
        var transfer = await _api.InitiateAsync(owner, family, recipient);
        try
        {
            // An enabled trigger makes SQL Server reject EF's OUTPUT clause (error 334),
            // after the preliminary ExecuteUpdate statements, which have no OUTPUT clause.
            // The trigger exists only in this fixture's disposable database.
            await _host.InUnitAsync(async sp =>
            {
                var db = await sp.GetRequiredService<IDbContextProvider<STNGDbContext>>().GetDbContextAsync();
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE TRIGGER TR_FamilyReview_RejectPromotion ON AppFamilyMembers AFTER UPDATE AS
                    BEGIN
                        SET NOCOUNT ON;
                    END
                    """);
            });

            using var scope = _host.Services.CreateScope();
            var sp = scope.ServiceProvider;
            using var unit = sp.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true);
            var aggregate = await sp.GetRequiredService<IRepository<Family, Guid>>().GetAsync(family.Id);
            await sp.GetRequiredService<OwnershipTransferManager>().AcceptAsync(aggregate, recipient.ParentId, transfer.Id);
            var failure = await Should.ThrowAsync<DbUpdateException>(() => unit.SaveChangesAsync());
            failure.InnerException.ShouldBeOfType<SqlException>().Number.ShouldBe(334);
            var db = await sp.GetRequiredService<IDbContextProvider<STNGDbContext>>().GetDbContextAsync();
            (await db.FamilyMembers.AsNoTracking().SingleAsync(x => x.FamilyId == family.Id && x.Role == FamilyMemberRole.Owner))
                .ParentId.ShouldBe(owner.ParentId);
            (await db.OwnershipTransfers.AsNoTracking().SingleAsync(x => x.Id == transfer.Id)).Status.ShouldBe(OwnershipTransferStatus.Pending);
            (await db.FamilyMembershipHistory.AsNoTracking().CountAsync(x => x.FamilyId == family.Id)).ShouldBe(2);
            // A failed context is discarded; it must not be completed or retried with its stale tracked changes.
        }
        finally
        {
            await _host.InUnitAsync(async sp =>
            {
                var db = await sp.GetRequiredService<IDbContextProvider<STNGDbContext>>().GetDbContextAsync();
                await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS TR_FamilyReview_RejectPromotion");
            });
        }
    }
}
