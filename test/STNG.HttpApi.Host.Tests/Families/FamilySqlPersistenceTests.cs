using System;
using System.Linq;
using System.Threading;
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
using Volo.Abp.Uow;
using Xunit;

namespace STNG;

[Collection("Authentication host")]
public class FamilySqlPersistenceTests
{
    private readonly AuthenticationHostFixture _host;
    private readonly FamilyTestClient _api;
    public FamilySqlPersistenceTests(AuthenticationHostFixture host) { _host = host; _api = new(host); }

    private static Task<STNGDbContext> DbAsync(IServiceProvider sp) =>
        sp.GetRequiredService<IDbContextProvider<STNGDbContext>>().GetDbContextAsync();

    [Fact]
    public async Task Concurrent_creation_retries_produce_one_family_and_one_initial_owner()
    {
        var owner = await _api.ParentAsync();
        var requestId = Guid.NewGuid();
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => _api.SendAsync(owner,
            System.Net.Http.HttpMethod.Post, "/api/families", new CreateFamilyDto { RequestId = requestId, Name = "Same request" })));
        foreach (var response in responses)
            response.StatusCode.ShouldBeOneOf(System.Net.HttpStatusCode.OK, System.Net.HttpStatusCode.Conflict);
        responses.Count(x => x.IsSuccessStatusCode).ShouldBeGreaterThanOrEqualTo(1);
        var replay = await _api.CreateAsync(owner, requestId);
        await _host.InUnitAsync(async sp =>
        {
            var db = await DbAsync(sp);
            (await db.Families.CountAsync(x => x.CreatedByParentId == owner.ParentId && x.CreationRequestId == requestId)).ShouldBe(1);
            (await db.FamilyMembers.CountAsync(x => x.FamilyId == replay.Id)).ShouldBe(1);
            (await db.FamilyMembershipHistory.CountAsync(x => x.FamilyId == replay.Id)).ShouldBe(1);
        });
    }

    [Fact]
    public async Task Creation_and_initial_owner_roll_back_together()
    {
        var owner = await _api.ParentAsync();
        var requestId = Guid.NewGuid();
        await Should.ThrowAsync<InvalidOperationException>(() => _host.InUnitAsync(async sp =>
        {
            var family = await sp.GetRequiredService<FamilyManager>().CreateAsync(owner.ParentId, new FamilyName("Rollback"), requestId);
            await sp.GetRequiredService<IRepository<Family, Guid>>().InsertAsync(family, autoSave: true);
            throw new InvalidOperationException("Simulated failure after inserting the owner.");
        }));
        await _host.InUnitAsync(async sp =>
        {
            var db = await DbAsync(sp);
            (await db.Families.AnyAsync(x => x.CreationRequestId == requestId)).ShouldBeFalse();
            (await db.FamilyMembers.AnyAsync(x => x.ParentId == owner.ParentId)).ShouldBeFalse();
            (await db.FamilyMembershipHistory.AnyAsync(x => x.ParentId == owner.ParentId)).ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Transfer_role_changes_status_and_history_roll_back_together()
    {
        var owner = await _api.ParentAsync();
        var recipient = await _api.ParentAsync();
        var family = await _api.CreateAsync(owner);
        await _api.AddApprovedAsync(family.Id, owner, recipient);
        var transfer = await _api.InitiateAsync(owner, family, recipient);
        await Should.ThrowAsync<InvalidOperationException>(() => _host.InUnitAsync(async sp =>
        {
            var aggregate = await sp.GetRequiredService<IRepository<Family, Guid>>().GetAsync(family.Id);
            await sp.GetRequiredService<OwnershipTransferManager>().AcceptAsync(aggregate, recipient.ParentId, transfer.Id);
            await sp.GetRequiredService<IUnitOfWorkManager>().Current!.SaveChangesAsync();
            throw new InvalidOperationException("Simulated failure after changing the owner.");
        }));
        await _host.InUnitAsync(async sp =>
        {
            var db = await DbAsync(sp);
            var aggregate = await db.Families.Include(x => x.Members).Include(x => x.OwnershipTransfers).SingleAsync(x => x.Id == family.Id);
            aggregate.RequireMember(owner.ParentId).Role.ShouldBe(FamilyMemberRole.Owner);
            aggregate.RequireMember(recipient.ParentId).Role.ShouldBe(FamilyMemberRole.Member);
            aggregate.OwnershipTransfers.Single().Status.ShouldBe(OwnershipTransferStatus.Pending);
            (await db.FamilyMembershipHistory.CountAsync(x => x.FamilyId == family.Id)).ShouldBe(2);
        });
    }

    [Fact]
    public async Task Database_rejects_duplicate_memberships_and_a_second_active_owner()
    {
        var owner = await _api.ParentAsync();
        var member = await _api.ParentAsync();
        var family = await _api.CreateAsync(owner);
        await _api.AddApprovedAsync(family.Id, owner, member);
        var duplicate = await Should.ThrowAsync<SqlException>(() => _host.InUnitAsync(async sp =>
        {
            var db = await DbAsync(sp);
            var newId = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AppFamilyMembers (Id, FamilyId, ParentId, Role, Status, JoinedAt, CreationTime) SELECT {newId}, FamilyId, ParentId, Role, Status, JoinedAt, CreationTime FROM AppFamilyMembers WHERE FamilyId = {family.Id} AND ParentId = {member.ParentId}");
        }));
        duplicate.Number.ShouldBeOneOf(2601, 2627);
        var secondOwner = await Should.ThrowAsync<SqlException>(() => _host.InUnitAsync(async sp =>
        {
            var db = await DbAsync(sp);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AppFamilyMembers SET Role = 0 WHERE FamilyId = {family.Id} AND ParentId = {member.ParentId}");
        }));
        secondOwner.Number.ShouldBeOneOf(2601, 2627);
        (await _api.GetAsync(owner, family.Id)).MyRole.ShouldBe(FamilyMemberRole.Owner);
    }

    [Fact]
    public async Task Database_rejects_a_second_pending_transfer_and_allows_replacing_an_expired_one()
    {
        var owner = await _api.ParentAsync();
        var recipient = await _api.ParentAsync();
        var family = await _api.CreateAsync(owner);
        await _api.AddApprovedAsync(family.Id, owner, recipient);
        var transfer = await _api.InitiateAsync(owner, family, recipient);
        var duplicate = await Should.ThrowAsync<SqlException>(() => _host.InUnitAsync(async sp =>
        {
            var db = await DbAsync(sp);
            var id = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AppOwnershipTransfers (Id, FamilyId, FromParentId, ToParentId, Status, RequestedAt, ExpiresAt, CreationTime) SELECT {id}, FamilyId, FromParentId, ToParentId, Status, RequestedAt, ExpiresAt, CreationTime FROM AppOwnershipTransfers WHERE Id = {transfer.Id}");
        }));
        duplicate.Number.ShouldBeOneOf(2601, 2627);
        await _host.InUnitAsync(async sp =>
        {
            var aggregate = await sp.GetRequiredService<IRepository<Family, Guid>>().GetAsync(family.Id);
            var old = aggregate.OwnershipTransfers.Single();
            aggregate.InitiateOwnershipTransfer(owner.ParentId, Guid.NewGuid(), recipient.ParentId, old.ExpiresAt, old.ExpiresAt.AddDays(1));
        });
        await _host.InUnitAsync(async sp =>
        {
            var db = await DbAsync(sp);
            (await db.OwnershipTransfers.CountAsync(x => x.FamilyId == family.Id && x.Status == OwnershipTransferStatus.Pending)).ShouldBe(1);
            (await db.OwnershipTransfers.SingleAsync(x => x.Id == transfer.Id)).Status.ShouldBe(OwnershipTransferStatus.Expired);
        });
    }

    private async Task<string[]> RaceAsync(Guid familyId, Action<Family, int> change)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loaded = 0;
        async Task<string> AttemptAsync(int attempt)
        {
            try
            {
                await _host.InUnitAsync(async sp =>
                {
                    var family = await sp.GetRequiredService<IRepository<Family, Guid>>().GetAsync(familyId);
                    change(family, attempt);
                    if (Interlocked.Increment(ref loaded) == 2) ready.TrySetResult();
                    await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
                });
                return "success";
            }
            catch (BusinessException exception) when (exception.Code == FamilyErrorCodes.ConcurrencyConflict)
            {
                return exception.Code;
            }
        }
        return await Task.WhenAll(AttemptAsync(0), AttemptAsync(1));
    }

    [Fact]
    public async Task Concurrent_transfer_initiations_have_exactly_one_winner()
    {
        var owner = await _api.ParentAsync();
        var recipient = await _api.ParentAsync();
        var family = await _api.CreateAsync(owner);
        await _api.AddApprovedAsync(family.Id, owner, recipient);
        var outcomes = await RaceAsync(family.Id, (f, _) => f.InitiateOwnershipTransfer(owner.ParentId,
            Guid.NewGuid(), recipient.ParentId, _host.Clock.Now, _host.Clock.Now.AddDays(1)));
        outcomes.Count(x => x == "success").ShouldBe(1);
        outcomes.Count(x => x == FamilyErrorCodes.ConcurrencyConflict).ShouldBe(1);
        await _host.InUnitAsync(async sp =>
        {
            var db = await DbAsync(sp);
            (await db.OwnershipTransfers.CountAsync(x => x.FamilyId == family.Id)).ShouldBe(1);
            (await db.FamilyMembers.CountAsync(x => x.FamilyId == family.Id && x.Role == FamilyMemberRole.Owner && x.Status == FamilyMemberStatus.Active)).ShouldBe(1);
        });
    }

    [Fact]
    public async Task Concurrent_acceptances_change_ownership_once_and_preserve_history()
    {
        var owner = await _api.ParentAsync();
        var recipient = await _api.ParentAsync();
        var family = await _api.CreateAsync(owner);
        await _api.AddApprovedAsync(family.Id, owner, recipient);
        var transfer = await _api.InitiateAsync(owner, family, recipient);
        var outcomes = await RaceAsync(family.Id, (f, _) => f.CompleteOwnershipTransfer(recipient.ParentId, transfer.Id, _host.Clock.Now));
        outcomes.Count(x => x == "success").ShouldBe(1);
        outcomes.Count(x => x == FamilyErrorCodes.ConcurrencyConflict).ShouldBe(1);
        await _host.InUnitAsync(async sp =>
        {
            var db = await DbAsync(sp);
            (await db.FamilyMembers.SingleAsync(x => x.FamilyId == family.Id && x.Role == FamilyMemberRole.Owner)).ParentId.ShouldBe(recipient.ParentId);
            (await db.FamilyMembershipHistory.CountAsync(x => x.FamilyId == family.Id)).ShouldBe(4);
        });
    }

    [Fact]
    public async Task Concurrent_membership_creation_produces_one_membership_and_one_join_event()
    {
        var owner = await _api.ParentAsync();
        var member = await _api.ParentAsync();
        var family = await _api.CreateAsync(owner);
        var outcomes = await RaceAsync(family.Id, (f, _) =>
            f.AddMember(owner.ParentId, Guid.NewGuid(), member.ParentId, _host.Clock.Now));
        outcomes.Count(x => x == "success").ShouldBe(1);
        outcomes.Count(x => x == FamilyErrorCodes.ConcurrencyConflict).ShouldBe(1);
        await _host.InUnitAsync(async sp =>
        {
            var db = await DbAsync(sp);
            (await db.FamilyMembers.CountAsync(x => x.FamilyId == family.Id && x.ParentId == member.ParentId)).ShouldBe(1);
            (await db.FamilyMembershipHistory.CountAsync(x => x.FamilyId == family.Id && x.ParentId == member.ParentId &&
                x.Change == FamilyMembershipChange.Joined)).ShouldBe(1);
        });
    }

    [Fact]
    public async Task Concurrent_admin_demotion_prevents_a_stale_privileged_mutation_from_also_committing()
    {
        var owner = await _api.ParentAsync();
        var admin = await _api.ParentAsync();
        var target = await _api.ParentAsync();
        var family = await _api.CreateAsync(owner);
        await _api.AddApprovedAsync(family.Id, owner, admin);
        await _api.AddApprovedAsync(family.Id, owner, target);
        await _host.InUnitAsync(async sp =>
        {
            var aggregate = await sp.GetRequiredService<IRepository<Family, Guid>>().GetAsync(family.Id);
            aggregate.PromoteToAdmin(owner.ParentId, admin.ParentId, _host.Clock.Now);
        });
        var outcomes = await RaceAsync(family.Id, (f, i) =>
        {
            if (i == 0) f.DemoteAdmin(owner.ParentId, admin.ParentId, _host.Clock.Now);
            else f.RemoveMember(admin.ParentId, target.ParentId, _host.Clock.Now);
        });
        outcomes.Count(x => x == "success").ShouldBe(1);
        outcomes.Count(x => x == FamilyErrorCodes.ConcurrencyConflict).ShouldBe(1);
        await _host.InUnitAsync(async sp =>
        {
            var db = await DbAsync(sp);
            var role = (await db.FamilyMembers.SingleAsync(x => x.FamilyId == family.Id && x.ParentId == admin.ParentId)).Role;
            var targetStatus = (await db.FamilyMembers.SingleAsync(x => x.FamilyId == family.Id && x.ParentId == target.ParentId)).Status;
            targetStatus.ShouldBe(role == FamilyMemberRole.Member ? FamilyMemberStatus.Active : FamilyMemberStatus.Removed);
        });
    }

    [Fact]
    public async Task Concurrent_membership_changes_share_the_root_concurrency_protocol()
    {
        var owner = await _api.ParentAsync();
        var member = await _api.ParentAsync();
        var family = await _api.CreateAsync(owner);
        await _api.AddApprovedAsync(family.Id, owner, member);
        var outcomes = await RaceAsync(family.Id, (f, i) =>
        {
            if (i == 0) f.PromoteToAdmin(owner.ParentId, member.ParentId, _host.Clock.Now);
            else f.RemoveMember(owner.ParentId, member.ParentId, _host.Clock.Now);
        });
        outcomes.Count(x => x == "success").ShouldBe(1);
        outcomes.Count(x => x == FamilyErrorCodes.ConcurrencyConflict).ShouldBe(1);
        await _host.InUnitAsync(async sp =>
        {
            var db = await DbAsync(sp);
            (await db.FamilyMembers.CountAsync(x => x.FamilyId == family.Id && x.Status == FamilyMemberStatus.Active && x.Role == FamilyMemberRole.Owner)).ShouldBe(1);
            (await db.FamilyMembershipHistory.CountAsync(x => x.FamilyId == family.Id)).ShouldBe(3);
        });
    }

    [Fact]
    public async Task Sql_server_model_matches_migrations_and_records_have_deliberate_auditing()
    {
        await _host.InUnitAsync(async sp =>
        {
            var db = await DbAsync(sp);
            db.Database.IsSqlServer().ShouldBeTrue();
            db.Database.HasPendingModelChanges().ShouldBeFalse();
            var family = db.Model.FindEntityType(typeof(Family))!;
            family.FindProperty(nameof(Family.Version))!.IsConcurrencyToken.ShouldBeTrue();
            family.FindProperty(nameof(Family.ConcurrencyStamp))!.IsConcurrencyToken.ShouldBeTrue();
            family.FindProperty("IsDeleted").ShouldBeNull();
            db.Model.FindEntityType(typeof(FamilyMember))!.FindProperty("IsDeleted").ShouldBeNull();
            db.Model.FindEntityType(typeof(OwnershipTransfer))!.FindProperty("IsDeleted").ShouldBeNull();
            db.Model.FindEntityType(typeof(FamilyMembershipHistory))!.FindProperty("LastModificationTime").ShouldBeNull();
        });
    }
}
