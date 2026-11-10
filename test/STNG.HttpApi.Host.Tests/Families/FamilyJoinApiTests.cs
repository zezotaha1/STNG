using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using STNG.EntityFrameworkCore;
using STNG.Families;
using STNG.Parents;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;
using Xunit;

namespace STNG;

[Collection("Authentication host")]
public class FamilyJoinApiTests
{
    private readonly AuthenticationHostFixture _host;
    private readonly FamilyJoinTestClient _join;
    private FamilyTestClient Core => _join.Core;
    public FamilyJoinApiTests(AuthenticationHostFixture host) { _host = host; _join = new(host); }
    private static Task<STNGDbContext> DbAsync(IServiceProvider sp) => sp.GetRequiredService<IDbContextProvider<STNGDbContext>>().GetDbContextAsync();
    private async Task ErrorAsync(FamilyTestActor actor, string path, object body, HttpStatusCode status, string? code = null)
    {
        var response = await Core.SendAsync(actor, HttpMethod.Post, path, body);
        response.StatusCode.ShouldBe(status);
        if (code != null) (await response.Content.ReadAsStringAsync()).ShouldContain(code);
    }

    [Theory]
    [InlineData(FamilyJoinCredentialKind.ManualCode)]
    [InlineData(FamilyJoinCredentialKind.QRToken)]
    public async Task Legitimate_join_requires_approval_and_retries_never_reveal_the_secret(FamilyJoinCredentialKind kind)
    {
        var owner = await Core.ParentAsync(); var applicant = await Core.ParentAsync();
        var family = await Core.CreateAsync(owner);
        var issued = await _join.IssueAsync(owner, family.Id, kind);
        issued.Secret.ShouldNotBeNullOrEmpty();
        if (kind == FamilyJoinCredentialKind.QRToken) (issued.JoinLink == "stng://family/join?token=" + issued.Secret).ShouldBeTrue("QR payload must match without printing its token");
        else issued.JoinLink.ShouldBeNull();
        var replay = await _join.IssueAsync(owner, family.Id, kind, issued.Credential.Id);
        replay.Secret.ShouldBeNull(); replay.JoinLink.ShouldBeNull();
        var pending = await _join.SubmitAsync(applicant, issued);
        pending.ParentId.ShouldBe(applicant.ParentId); pending.Status.ShouldBe(FamilyJoinRequestStatus.Pending);
        (await Core.SendAsync(applicant, HttpMethod.Get, $"/api/families/{family.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _join.SubmitAsync(applicant, issued, pending.Id)).Id.ShouldBe(pending.Id);
        await ErrorAsync(applicant, "/api/family-join-requests", new { RequestId = Guid.NewGuid(), Kind = kind, Secret = issued.Secret },
            HttpStatusCode.Conflict, FamilyJoinErrorCodes.PendingRequestExists);
        var approved = await _join.DecideAsync(owner, pending, "approve");
        approved.Status.ShouldBe(FamilyJoinRequestStatus.Approved);
        (await _join.DecideAsync(owner, pending, "approve")).ReviewedAt.ShouldBe(approved.ReviewedAt);
        (await Core.GetAsync(applicant, family.Id)).MyRole.ShouldBe(FamilyMemberRole.Member);
        await _host.InUnitAsync(async sp =>
        {
            var db = await DbAsync(sp);
            var stored = await db.FamilyJoinCredentials.SingleAsync(x => x.Id == issued.Credential.Id);
            (stored.SecretHash == issued.Secret).ShouldBeFalse(); stored.SecretHash.Length.ShouldBe(64);
            (await db.FamilyMembers.CountAsync(x => x.FamilyId == family.Id && x.ParentId == applicant.ParentId)).ShouldBe(1);
            (await db.FamilyMembershipHistory.CountAsync(x => x.FamilyId == family.Id && x.ParentId == applicant.ParentId)).ShouldBe(1);
        });
        var list = await Core.SendAsync(owner, HttpMethod.Get, $"/api/families/{family.Id}/join-credentials");
        var text = await list.Content.ReadAsStringAsync();
        text.Contains(issued.Secret!, StringComparison.Ordinal).ShouldBeFalse("Ordinary lists must not contain the capability");
        text.ShouldNotContain("secretHash", Case.Insensitive);
        var ordinary = await Core.SendAsync(owner, HttpMethod.Get, $"/api/families/{family.Id}");
        (await ordinary.Content.ReadAsStringAsync()).Contains(issued.Secret!, StringComparison.Ordinal).ShouldBeFalse();
        await ErrorAsync(owner, $"/api/families/{family.Id}/join-requests/{pending.Id}/reject",
            new FamilyMutationDto { ConcurrencyStamp = approved.ConcurrencyStamp }, HttpStatusCode.Conflict, FamilyJoinErrorCodes.InvalidTransition);
    }

    [Fact]
    public async Task Rotation_and_explicit_revocation_cancel_pending_requests_but_natural_expiry_does_not()
    {
        var owner = await Core.ParentAsync(); var applicant = await Core.ParentAsync();
        var family = await Core.CreateAsync(owner); var first = await _join.IssueAsync(owner, family.Id);
        var pending = await _join.SubmitAsync(applicant, first);
        var rotated = await _join.IssueAsync(owner, family.Id);
        var immediateCodes = await Core.SendAsync(owner, HttpMethod.Get, $"/api/families/{family.Id}/join-credentials");
        (await immediateCodes.Content.ReadFromJsonAsync<PagedResultDto<FamilyJoinCredentialDto>>())!.Items.Single(x => x.Id == rotated.Credential.Id)
            .ConcurrencyStamp.ShouldBe(rotated.Credential.ConcurrencyStamp, "Issuance must return the persisted stamp");
        var own = await Core.SendAsync(applicant, HttpMethod.Get, "/api/me/family-join-requests");
        (await own.Content.ReadFromJsonAsync<PagedResultDto<FamilyJoinRequestDto>>())!.Items.Single(x => x.Id == pending.Id)
            .Status.ShouldBe(FamilyJoinRequestStatus.Cancelled);
        await ErrorAsync(applicant, "/api/family-join-requests", new { RequestId = Guid.NewGuid(), Kind = 0, Secret = first.Secret },
            HttpStatusCode.NotFound, FamilyJoinErrorCodes.Unavailable);
        pending = await _join.SubmitAsync(applicant, rotated);
        var currentCodes = await Core.SendAsync(owner, HttpMethod.Get, $"/api/families/{family.Id}/join-credentials");
        var currentCode = (await currentCodes.Content.ReadFromJsonAsync<PagedResultDto<FamilyJoinCredentialDto>>())!.Items.Single(x => x.Id == rotated.Credential.Id);
        currentCode.ConcurrencyStamp.ShouldBe(rotated.Credential.ConcurrencyStamp, "Read-only submission must not change the credential stamp");
        var revoke = $"/api/families/{family.Id}/join-credentials/{rotated.Credential.Id}/revoke";
        await ErrorAsync(owner, revoke, new FamilyMutationDto { ConcurrencyStamp = "stale" }, HttpStatusCode.Conflict);
        var revokeResponse = await Core.SendAsync(owner, HttpMethod.Post, revoke, new FamilyMutationDto { ConcurrencyStamp = rotated.Credential.ConcurrencyStamp });
        revokeResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent, await revokeResponse.Content.ReadAsStringAsync());
        (await Core.SendAsync(owner, HttpMethod.Post, revoke, new FamilyMutationDto { ConcurrencyStamp = rotated.Credential.ConcurrencyStamp })).EnsureSuccessStatusCode();
        var expiring = await _join.IssueAsync(owner, family.Id);
        pending = await _join.SubmitAsync(applicant, expiring);
        _host.Clock.Advance(TimeSpan.FromDays(2));
        await ErrorAsync(await Core.ParentAsync(), "/api/family-join-requests", new { RequestId = Guid.NewGuid(), Kind = 0, Secret = expiring.Secret },
            HttpStatusCode.NotFound, FamilyJoinErrorCodes.Unavailable);
        await _join.IssueAsync(owner, family.Id); // Retiring an expired code preserves its valid pending request.
        (await _join.DecideAsync(owner, pending, "approve")).Status.ShouldBe(FamilyJoinRequestStatus.Approved);
    }

    [Fact]
    public async Task Parent_identity_visibility_permissions_and_resource_ids_are_scoped()
    {
        var owner = await Core.ParentAsync(); var applicant = await Core.ParentAsync(); var outsider = await Core.ParentAsync();
        var family = await Core.CreateAsync(owner); var other = await Core.CreateAsync(outsider);
        var code = await _join.IssueAsync(owner, family.Id);
        var request = await _join.PostAsync<FamilyJoinRequestDto>(applicant, "/api/family-join-requests",
            new { RequestId = Guid.NewGuid(), Kind = 0, Secret = code.Secret, ParentId = owner.ParentId, FamilyId = other.Id });
        request.ParentId.ShouldBe(applicant.ParentId); request.FamilyId.ShouldBe(family.Id);
        var own = await Core.SendAsync(outsider, HttpMethod.Get, "/api/me/family-join-requests");
        (await own.Content.ReadFromJsonAsync<PagedResultDto<FamilyJoinRequestDto>>())!.TotalCount.ShouldBe(0);
        foreach (var suffix in new[] { "join-requests", "join-credentials" })
            (await Core.SendAsync(outsider, HttpMethod.Get, $"/api/families/{family.Id}/{suffix}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await ErrorAsync(outsider, $"/api/family-join-requests/{request.Id}/cancel", new FamilyMutationDto { ConcurrencyStamp = request.ConcurrencyStamp }, HttpStatusCode.NotFound);
        await ErrorAsync(outsider, $"/api/families/{other.Id}/join-requests/{request.Id}/approve", new FamilyMutationDto { ConcurrencyStamp = request.ConcurrencyStamp }, HttpStatusCode.NotFound);
        await ErrorAsync(outsider, $"/api/families/{other.Id}/join-credentials/{code.Credential.Id}/revoke", new FamilyMutationDto { ConcurrencyStamp = code.Credential.ConcurrencyStamp }, HttpStatusCode.NotFound);
        await _join.DecideAsync(owner, request, "approve");
        await ErrorAsync(applicant, $"/api/families/{family.Id}/join-credentials", new { RequestId = Guid.NewGuid(), Kind = 0 }, HttpStatusCode.Forbidden);
        (await Core.SendAsync(applicant, HttpMethod.Get, $"/api/families/{family.Id}/join-requests")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var second = await _join.SubmitAsync(outsider, code);
        await ErrorAsync(applicant, $"/api/families/{family.Id}/join-requests/{second.Id}/approve", new FamilyMutationDto { ConcurrencyStamp = second.ConcurrencyStamp }, HttpStatusCode.Forbidden);
        var fresh = await Core.GetAsync(owner, family.Id);
        (await Core.SendAsync(owner, HttpMethod.Post, $"/api/families/{family.Id}/members/{applicant.ParentId}/promote", FamilyTestClient.Stamp(fresh))).EnsureSuccessStatusCode();
        (await _join.DecideAsync(applicant, second, "approve")).Status.ShouldBe(FamilyJoinRequestStatus.Approved);
        fresh = await Core.GetAsync(owner, family.Id);
        (await Core.SendAsync(owner, HttpMethod.Post, $"/api/families/{family.Id}/members/{applicant.ParentId}/demote", FamilyTestClient.Stamp(fresh))).EnsureSuccessStatusCode();
        await ErrorAsync(applicant, $"/api/families/{family.Id}/join-credentials", new { RequestId = Guid.NewGuid(), Kind = 1 }, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_requester_who_later_becomes_admin_still_cannot_review_their_own_request()
    {
        var owner = await Core.ParentAsync(); var applicant = await Core.ParentAsync(); var family = await Core.CreateAsync(owner);
        var pending = await _join.SubmitAsync(applicant, await _join.IssueAsync(owner, family.Id));
        await Core.AddApprovedAsync(family.Id, owner, applicant);
        await _host.InUnitAsync(async sp => (await sp.GetRequiredService<IRepository<Family, Guid>>().GetAsync(family.Id))
            .PromoteToAdmin(owner.ParentId, applicant.ParentId, _host.Clock.Now));
        foreach (var action in new[] { "approve", "reject" })
            await ErrorAsync(applicant, $"/api/families/{family.Id}/join-requests/{pending.Id}/{action}",
                new FamilyMutationDto { ConcurrencyStamp = pending.ConcurrencyStamp }, HttpStatusCode.Forbidden, FamilyJoinErrorCodes.CannotReviewOwnRequest);
    }

    [Fact]
    public async Task Reject_cancel_stale_stamps_and_reapplication_preserve_terminal_records()
    {
        var owner = await Core.ParentAsync(); var applicant = await Core.ParentAsync(); var family = await Core.CreateAsync(owner);
        var code = await _join.IssueAsync(owner, family.Id); var request = await _join.SubmitAsync(applicant, code);
        await ErrorAsync(owner, $"/api/families/{family.Id}/join-requests/{request.Id}/approve", new FamilyMutationDto { ConcurrencyStamp = "stale" }, HttpStatusCode.Conflict, FamilyErrorCodes.ConcurrencyConflict);
        var rejected = await _join.PostAsync<FamilyJoinRequestDto>(owner, $"/api/families/{family.Id}/join-requests/{request.Id}/reject",
            new RejectFamilyJoinRequestDto { ConcurrencyStamp = request.ConcurrencyStamp, Reason = "  Try again later  " });
        rejected.RejectionReason.ShouldBe("Try again later");
        await ErrorAsync(applicant, "/api/family-join-requests", new { RequestId = Guid.NewGuid(), Kind = 0, Secret = code.Secret }, HttpStatusCode.TooManyRequests);
        _host.Clock.Advance(TimeSpan.FromMinutes(2));
        var replacement = await _join.SubmitAsync(applicant, code);
        await ErrorAsync(applicant, $"/api/family-join-requests/{replacement.Id}/cancel", new FamilyMutationDto { ConcurrencyStamp = "stale" }, HttpStatusCode.Conflict);
        (await _join.CancelAsync(applicant, replacement)).Status.ShouldBe(FamilyJoinRequestStatus.Cancelled);
        (await _join.CancelAsync(applicant, replacement)).Status.ShouldBe(FamilyJoinRequestStatus.Cancelled);
        await ErrorAsync(owner, $"/api/families/{family.Id}/join-requests/{replacement.Id}/approve", new FamilyMutationDto { ConcurrencyStamp = replacement.ConcurrencyStamp }, HttpStatusCode.Conflict);
        await ErrorAsync(applicant, "/api/family-join-requests", new { RequestId = replacement.Id, Kind = 0, Secret = "00000-00000-00000-00000" }, HttpStatusCode.Conflict, FamilyJoinErrorCodes.RequestConflict);
    }

    [Fact]
    public async Task Former_admin_rejoins_as_member_with_the_same_retained_membership()
    {
        var owner = await Core.ParentAsync(); var applicant = await Core.ParentAsync(); var family = await Core.CreateAsync(owner);
        var code = await _join.IssueAsync(owner, family.Id);
        await _join.DecideAsync(owner, await _join.SubmitAsync(applicant, code), "approve");
        Guid memberId = default;
        await _host.InUnitAsync(async sp =>
        {
            var f = await sp.GetRequiredService<IRepository<Family, Guid>>().GetAsync(family.Id);
            memberId = f.RequireMember(applicant.ParentId).Id;
            f.PromoteToAdmin(owner.ParentId, applicant.ParentId, _host.Clock.Now);
            f.RemoveMember(owner.ParentId, applicant.ParentId, _host.Clock.Now);
        });
        await _join.DecideAsync(owner, await _join.SubmitAsync(applicant, code), "approve");
        (await Core.GetAsync(applicant, family.Id)).MyRole.ShouldBe(FamilyMemberRole.Member);
        await _host.InUnitAsync(async sp =>
        {
            var db = await DbAsync(sp);
            (await db.FamilyMembers.SingleAsync(x => x.FamilyId == family.Id && x.ParentId == applicant.ParentId)).Id.ShouldBe(memberId);
            (await db.FamilyMembershipHistory.CountAsync(x => x.MemberId == memberId)).ShouldBe(4);
        });
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("deleted")]
    public async Task Applicant_deactivation_prevents_atomic_approval(string change)
    {
        var owner = await Core.ParentAsync(); var applicant = await Core.ParentAsync(); var family = await Core.CreateAsync(owner);
        var pending = await _join.SubmitAsync(applicant, await _join.IssueAsync(owner, family.Id));
        await _host.InUnitAsync(async sp =>
        {
            if (change == "inactive") (await sp.GetRequiredService<IRepository<IdentityUser, Guid>>().GetAsync(applicant.UserId)).SetIsActive(false);
            else await sp.GetRequiredService<IRepository<Parent, Guid>>().DeleteAsync(applicant.ParentId);
        });
        await ErrorAsync(owner, $"/api/families/{family.Id}/join-requests/{pending.Id}/approve", new FamilyMutationDto { ConcurrencyStamp = pending.ConcurrencyStamp }, HttpStatusCode.Forbidden, FamilyErrorCodes.ParentNotEligible);
        await _host.InUnitAsync(async sp =>
        {
            var db = await DbAsync(sp);
            (await db.FamilyJoinRequests.SingleAsync(x => x.Id == pending.Id)).Status.ShouldBe(FamilyJoinRequestStatus.Pending);
            (await db.FamilyMembers.AnyAsync(x => x.FamilyId == family.Id && x.ParentId == applicant.ParentId)).ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Paged_reads_filter_counts_in_sql_without_materializing_aggregates_and_validate_limits()
    {
        var owner = await Core.ParentAsync(); var applicant = await Core.ParentAsync(); var outsider = await Core.ParentAsync();
        var family = await Core.CreateAsync(owner); var other = await Core.CreateAsync(outsider);
        var code = await _join.IssueAsync(owner, family.Id);
        for (var i = 0; i < 3; i++) await _join.CancelAsync(applicant, await _join.SubmitAsync(applicant, code));
        var pending = await _join.SubmitAsync(applicant, code);
        await _join.SubmitAsync(applicant, await _join.IssueAsync(outsider, other.Id));
        using (var probe = _host.FamilyReads.Observe())
        {
            var response = await Core.SendAsync(owner, HttpMethod.Get, $"/api/families/{family.Id}/join-requests?maxResultCount=1");
            var page = (await response.Content.ReadFromJsonAsync<PagedResultDto<FamilyJoinRequestDto>>())!;
            page.TotalCount.ShouldBe(1); page.Items.Single().Id.ShouldBe(pending.Id);
            var mine = await Core.SendAsync(applicant, HttpMethod.Get, "/api/me/family-join-requests?maxResultCount=2&skipCount=1");
            var ownPage = (await mine.Content.ReadFromJsonAsync<PagedResultDto<FamilyJoinRequestDto>>())!;
            ownPage.TotalCount.ShouldBe(5); ownPage.Items.Count.ShouldBe(2);
            var credentials = await Core.SendAsync(owner, HttpMethod.Get, $"/api/families/{family.Id}/join-credentials?maxResultCount=1");
            credentials.EnsureSuccessStatusCode();
            probe.MaterializedEntities.ShouldBeEmpty();
            probe.Commands.Count(x => x.Sql.Contains("OFFSET") && x.Sql.Contains("FETCH NEXT")).ShouldBe(3);
        }
        foreach (var path in new[] { "/api/me/family-join-requests?maxResultCount=101", $"/api/families/{family.Id}/join-requests?status=99", $"/api/families/{family.Id}/join-credentials?skipCount=-1" })
            (await Core.SendAsync(owner, HttpMethod.Get, path)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await _host.Client.GetAsync("/api/me/family-join-requests")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
