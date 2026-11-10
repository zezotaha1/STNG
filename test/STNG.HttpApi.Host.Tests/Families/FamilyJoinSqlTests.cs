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
public class FamilyJoinSqlTests
{
    private readonly AuthenticationHostFixture _host;
    private readonly FamilyJoinTestClient _join;
    public FamilyJoinSqlTests(AuthenticationHostFixture host) { _host = host; _join = new(host); }
    private static Task<STNGDbContext> DbAsync(IServiceProvider sp) => sp.GetRequiredService<IDbContextProvider<STNGDbContext>>().GetDbContextAsync();

    // Both transactions finish loading and performing domain decisions before either saves.
    private async Task RaceAsync(Func<IServiceProvider, int, Task> mutate)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        async Task<bool> Attempt(int attempt)
        {
            try
            {
                await _host.InUnitAsync(async sp =>
                {
                    await mutate(sp, attempt);
                    if (Interlocked.Increment(ref count) == 2) ready.TrySetResult();
                    await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
                });
                return true;
            }
            catch (BusinessException exception) when (exception.Code == FamilyErrorCodes.ConcurrencyConflict) { return false; }
        }
        (await Task.WhenAll(Attempt(0), Attempt(1))).Count(x => x).ShouldBe(1);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("cancel")]
    [InlineData("revoke")]
    [InlineData("archive")]
    [InlineData("demote")]
    [InlineData("membership")]
    [InlineData("reactivate")]
    public async Task Approval_and_competing_mutations_share_the_family_root_transaction(string competing)
    {
        var owner = await _join.Core.ParentAsync(); var reviewer = await _join.Core.ParentAsync(); var applicant = await _join.Core.ParentAsync();
        var family = await _join.Core.CreateAsync(owner);
        await _join.Core.AddApprovedAsync(family.Id, owner, reviewer);
        await _host.InUnitAsync(async sp => (await sp.GetRequiredService<IRepository<Family, Guid>>().GetAsync(family.Id))
            .PromoteToAdmin(owner.ParentId, reviewer.ParentId, _host.Clock.Now));
        if (competing == "reactivate")
        {
            await _join.Core.AddApprovedAsync(family.Id, owner, applicant);
            await _host.InUnitAsync(async sp => (await sp.GetRequiredService<IRepository<Family, Guid>>().GetAsync(family.Id))
                .RemoveMember(owner.ParentId, applicant.ParentId, _host.Clock.Now));
        }
        var issued = await _join.IssueAsync(owner, family.Id);
        var pending = await _join.SubmitAsync(applicant, issued);
        await RaceAsync(async (sp, attempt) =>
        {
            var aggregate = await sp.GetRequiredService<IRepository<Family, Guid>>().GetAsync(family.Id);
            var request = await sp.GetRequiredService<IRepository<FamilyJoinRequest, Guid>>().GetAsync(pending.Id);
            var manager = sp.GetRequiredService<FamilyJoinManager>();
            if (attempt == 0 || competing == "approve") await manager.ApproveAsync(aggregate, attempt == 0 ? reviewer.ParentId : owner.ParentId, request);
            else if (competing == "reject") await manager.RejectAsync(aggregate, owner.ParentId, request, "Not approved");
            else if (competing == "cancel") await manager.CancelAsync(aggregate, applicant.ParentId, request);
            else if (competing == "revoke") await manager.RevokeAsync(aggregate, owner.ParentId,
                await sp.GetRequiredService<IRepository<FamilyJoinCredential, Guid>>().GetAsync(issued.Credential.Id));
            else if (competing == "archive") aggregate.BeginArchiving(owner.ParentId, _host.Clock.Now);
            else if (competing == "demote") aggregate.DemoteAdmin(owner.ParentId, reviewer.ParentId, _host.Clock.Now);
            else await sp.GetRequiredService<FamilyManager>().AddApprovedMemberAsync(aggregate, owner.ParentId, applicant.ParentId);
        });
        await _host.InUnitAsync(async sp =>
        {
            var db = await DbAsync(sp);
            var request = await db.FamilyJoinRequests.SingleAsync(x => x.Id == pending.Id);
            var members = await db.FamilyMembers.Where(x => x.FamilyId == family.Id && x.ParentId == applicant.ParentId).ToListAsync();
            members.Count.ShouldBeLessThanOrEqualTo(1);
            var active = members.Any(x => x.Status == FamilyMemberStatus.Active);
            if (competing is "membership" or "reactivate") active.ShouldBeTrue();
            else active.ShouldBe(request.Status == FamilyJoinRequestStatus.Approved);
            if (active) members.Single().Role.ShouldBe(FamilyMemberRole.Member);
            if (competing == "demote")
                (await db.FamilyMembers.SingleAsync(x => x.FamilyId == family.Id && x.ParentId == reviewer.ParentId)).Role
                    .ShouldBe(request.Status == FamilyJoinRequestStatus.Approved ? FamilyMemberRole.Admin : FamilyMemberRole.Member);
            if (competing == "archive") (await db.Families.SingleAsync(x => x.Id == family.Id)).Status
                .ShouldBe(request.Status == FamilyJoinRequestStatus.Approved ? FamilyStatus.Active : FamilyStatus.Archiving);
            (await db.FamilyMembers.CountAsync(x => x.FamilyId == family.Id && x.Role == FamilyMemberRole.Owner && x.Status == FamilyMemberStatus.Active)).ShouldBe(1);
            (await db.FamilyMembershipHistory.CountAsync(x => x.FamilyId == family.Id && x.ParentId == applicant.ParentId))
                .ShouldBe(competing == "reactivate" ? 3 : active ? 1 : 0);
        });
    }

    [Fact]
    public async Task Duplicate_pending_submissions_have_one_winner_even_with_different_request_ids()
    {
        var owner = await _join.Core.ParentAsync(); var applicant = await _join.Core.ParentAsync();
        var family = await _join.Core.CreateAsync(owner); var issued = await _join.IssueAsync(owner, family.Id);
        await RaceAsync(async (sp, _) => await sp.GetRequiredService<FamilyJoinManager>()
            .SubmitAsync(applicant.ParentId, Guid.NewGuid(), issued.Credential.Kind, issued.Secret!));
        await _host.InUnitAsync(async sp =>
        {
            var db = await DbAsync(sp);
            (await db.FamilyJoinRequests.CountAsync(x => x.FamilyId == family.Id && x.ParentId == applicant.ParentId)).ShouldBe(1);
            (await db.FamilyMembers.AnyAsync(x => x.FamilyId == family.Id && x.ParentId == applicant.ParentId)).ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Concurrent_rotation_leaves_one_active_credential_and_cancels_the_original_pending_request()
    {
        var owner = await _join.Core.ParentAsync(); var applicant = await _join.Core.ParentAsync();
        var family = await _join.Core.CreateAsync(owner); var issued = await _join.IssueAsync(owner, family.Id);
        var pending = await _join.SubmitAsync(applicant, issued);
        await RaceAsync(async (sp, _) =>
        {
            var root = await sp.GetRequiredService<IRepository<Family, Guid>>().GetAsync(family.Id);
            await sp.GetRequiredService<FamilyJoinManager>().CreateCredentialAsync(root, owner.ParentId, Guid.NewGuid(), FamilyJoinCredentialKind.ManualCode);
        });
        await _host.InUnitAsync(async sp =>
        {
            var db = await DbAsync(sp);
            (await db.FamilyJoinCredentials.CountAsync(x => x.FamilyId == family.Id)).ShouldBe(2);
            (await db.FamilyJoinCredentials.CountAsync(x => x.FamilyId == family.Id && x.Status == FamilyJoinCredentialStatus.Active)).ShouldBe(1);
            (await db.FamilyJoinRequests.SingleAsync(x => x.Id == pending.Id)).Status.ShouldBe(FamilyJoinRequestStatus.Cancelled);
        });
    }

    [Fact]
    public async Task An_older_tracked_credential_cannot_hide_revocation_when_the_new_family_version_is_loaded()
    {
        var owner = await _join.Core.ParentAsync(); var applicant = await _join.Core.ParentAsync();
        var family = await _join.Core.CreateAsync(owner); var issued = await _join.IssueAsync(owner, family.Id);
        await _host.InUnitAsync(async sp =>
        {
            var stale = await sp.GetRequiredService<IRepository<FamilyJoinCredential, Guid>>().GetAsync(issued.Credential.Id);
            stale.Status.ShouldBe(FamilyJoinCredentialStatus.Active);
            await _host.InUnitAsync(async other =>
            {
                var root = await other.GetRequiredService<IRepository<Family, Guid>>().GetAsync(family.Id);
                var credential = await other.GetRequiredService<IRepository<FamilyJoinCredential, Guid>>().GetAsync(issued.Credential.Id);
                await other.GetRequiredService<FamilyJoinManager>().RevokeAsync(root, owner.ParentId, credential);
            });
            var failure = await Should.ThrowAsync<BusinessException>(() => sp.GetRequiredService<FamilyJoinManager>()
                .SubmitAsync(applicant.ParentId, Guid.NewGuid(), issued.Credential.Kind, issued.Secret!));
            failure.Code.ShouldBe(FamilyJoinErrorCodes.Unavailable);
            (await (await DbAsync(sp)).FamilyJoinRequests.AnyAsync(x => x.FamilyId == family.Id)).ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Database_constraints_reject_duplicate_pending_rows_active_kinds_and_cross_family_credentials()
    {
        var owner = await _join.Core.ParentAsync(); var applicant = await _join.Core.ParentAsync();
        var family = await _join.Core.CreateAsync(owner); var other = await _join.Core.CreateAsync(owner);
        var code = await _join.IssueAsync(owner, family.Id); var request = await _join.SubmitAsync(applicant, code);
        var duplicate = await Should.ThrowAsync<SqlException>(() => _host.InUnitAsync(async sp =>
        {
            var db = await DbAsync(sp); var id = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AppFamilyJoinRequests (Id,FamilyId,ParentId,CredentialId,Status,RequestedAt,CreationTime,ExtraProperties,ConcurrencyStamp) SELECT {id},FamilyId,ParentId,CredentialId,Status,RequestedAt,CreationTime,ExtraProperties,ConcurrencyStamp FROM AppFamilyJoinRequests WHERE Id={request.Id}");
        }));
        duplicate.Number.ShouldBeOneOf(2601, 2627);
        duplicate = await Should.ThrowAsync<SqlException>(() => _host.InUnitAsync(async sp =>
        {
            var db = await DbAsync(sp); var id = Guid.NewGuid(); var hash = new string('A', 64);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AppFamilyJoinCredentials (Id,FamilyId,Kind,SecretHash,Status,CreatedByParentId,CreatedAt,ExpiresAt,CreationTime,ExtraProperties,ConcurrencyStamp) SELECT {id},FamilyId,Kind,{hash},Status,CreatedByParentId,CreatedAt,ExpiresAt,CreationTime,ExtraProperties,ConcurrencyStamp FROM AppFamilyJoinCredentials WHERE Id={code.Credential.Id}");
        }));
        duplicate.Number.ShouldBeOneOf(2601, 2627);
        var fk = await Should.ThrowAsync<SqlException>(() => _host.InUnitAsync(async sp =>
        {
            var db = await DbAsync(sp);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AppFamilyJoinRequests SET FamilyId={other.Id} WHERE Id={request.Id}");
        }));
        fk.Number.ShouldBe(547);
    }

    [Fact]
    public async Task Join_records_are_retained_and_cannot_be_modified_without_the_family_root_protocol()
    {
        var owner = await _join.Core.ParentAsync(); var family = await _join.Core.CreateAsync(owner);
        var code = await _join.IssueAsync(owner, family.Id);
        await Should.ThrowAsync<InvalidOperationException>(() => _host.InUnitAsync(async sp =>
        {
            var db = await DbAsync(sp);
            db.Remove(await db.FamilyJoinCredentials.SingleAsync(x => x.Id == code.Credential.Id));
        }));
        await Should.ThrowAsync<InvalidOperationException>(() => _host.InUnitAsync(async sp =>
        {
            var db = await DbAsync(sp);
            var entity = await db.FamilyJoinCredentials.SingleAsync(x => x.Id == code.Credential.Id);
            db.Entry(entity).Property(x => x.SecretHash).CurrentValue = new string('B', 64);
        }));
        await _host.InUnitAsync(async sp =>
        {
            var db = await DbAsync(sp);
            foreach (var type in new[] { typeof(FamilyJoinCredential), typeof(FamilyJoinRequest) })
            {
                db.Model.FindEntityType(type)!.FindProperty("ConcurrencyStamp")!.IsConcurrencyToken.ShouldBeTrue();
                db.Model.FindEntityType(type)!.FindProperty("IsDeleted").ShouldBeNull();
            }
            db.Database.HasPendingModelChanges().ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Failure_after_ordered_retirement_rolls_back_request_membership_and_history_to_the_savepoint()
    {
        var owner = await _join.Core.ParentAsync(); var applicant = await _join.Core.ParentAsync();
        var family = await _join.Core.CreateAsync(owner);
        var request = await _join.SubmitAsync(applicant, await _join.IssueAsync(owner, family.Id));
        try
        {
            await _host.InUnitAsync(async sp => await (await DbAsync(sp)).Database.ExecuteSqlRawAsync("CREATE TRIGGER TR_Join_RejectUpdate ON AppFamilyJoinRequests AFTER UPDATE AS BEGIN SET NOCOUNT ON; END"));
            using var scope = _host.Services.CreateScope(); var sp = scope.ServiceProvider;
            using var unit = sp.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true);
            var root = await sp.GetRequiredService<IRepository<Family, Guid>>().GetAsync(family.Id);
            var pending = await sp.GetRequiredService<IRepository<FamilyJoinRequest, Guid>>().GetAsync(request.Id);
            await sp.GetRequiredService<FamilyJoinManager>().ApproveAsync(root, owner.ParentId, pending);
            var failure = await Should.ThrowAsync<DbUpdateException>(() => unit.SaveChangesAsync());
            failure.InnerException.ShouldBeOfType<SqlException>().Number.ShouldBe(334);
            var db = await DbAsync(sp);
            (await db.FamilyJoinRequests.AsNoTracking().SingleAsync(x => x.Id == request.Id)).Status.ShouldBe(FamilyJoinRequestStatus.Pending);
            (await db.FamilyMembers.AsNoTracking().AnyAsync(x => x.FamilyId == family.Id && x.ParentId == applicant.ParentId)).ShouldBeFalse();
            (await db.FamilyMembershipHistory.AsNoTracking().CountAsync(x => x.FamilyId == family.Id)).ShouldBe(1);
        }
        finally { await _host.InUnitAsync(async sp => await (await DbAsync(sp)).Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS TR_Join_RejectUpdate")); }
    }
}
