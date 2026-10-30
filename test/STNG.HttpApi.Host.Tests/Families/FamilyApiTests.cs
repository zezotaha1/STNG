using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using STNG.Families;
using STNG.Parents;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Xunit;

namespace STNG;

[Collection("Authentication host")]
public class FamilyApiTests
{
    private readonly AuthenticationHostFixture _host;
    private readonly FamilyTestClient _api;
    public FamilyApiTests(AuthenticationHostFixture host) { _host = host; _api = new(host); }

    private static async Task ErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).ShouldContain(code);
    }

    [Fact]
    public async Task Creates_multiple_families_with_an_owner_and_replays_creation_without_duplicates()
    {
        var owner = await _api.ParentAsync();
        var request = Guid.NewGuid();
        var first = await _api.CreateAsync(owner, request);
        var replay = await _api.CreateAsync(owner, request);
        replay.Id.ShouldBe(first.Id);
        var second = await _api.CreateAsync(owner);
        second.Id.ShouldNotBe(first.Id);
        first.Name.ShouldBe("Test Family");
        first.MyRole.ShouldBe(FamilyMemberRole.Owner);
        first.CreationTime.ShouldNotBe(default);
        var response = await _api.SendAsync(owner, HttpMethod.Get, "/api/families/mine?maxResultCount=100");
        response.EnsureSuccessStatusCode();
        var mine = (await response.Content.ReadFromJsonAsync<PagedResultDto<FamilyDto>>())!;
        mine.TotalCount.ShouldBe(2);
        mine.Items.ShouldAllBe(x => x.MyRole == FamilyMemberRole.Owner);
        var members = await _api.SendAsync(owner, HttpMethod.Get, $"/api/families/{first.Id}/members");
        members.EnsureSuccessStatusCode();
        (await members.Content.ReadFromJsonAsync<PagedResultDto<FamilyMemberDto>>())!.Items.Single().ParentId.ShouldBe(owner.ParentId);
    }

    [Fact]
    public async Task Validation_and_authentication_run_in_the_http_pipeline()
    {
        (await _host.Client.GetAsync("/api/families/mine")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var actor = await _api.ParentAsync();
        foreach (var name in new[] { "", "   ", new string('x', 121) })
        {
            var response = await _api.SendAsync(actor, HttpMethod.Post, "/api/families", new CreateFamilyDto { RequestId = Guid.NewGuid(), Name = name });
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
        (await _api.SendAsync(actor, HttpMethod.Post, "/api/families", new CreateFamilyDto { Name = "Test" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await _api.SendAsync(actor, HttpMethod.Get, "/api/families/mine?maxResultCount=101")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var family = await _api.CreateAsync(actor);
        (await _api.SendAsync(actor, HttpMethod.Put, $"/api/families/{family.Id}", new UpdateFamilyDto { Name = "Update" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Owner_role_in_another_family_grants_no_access_and_actor_identity_cannot_be_spoofed()
    {
        var a = await _api.ParentAsync();
        var b = await _api.ParentAsync();
        var family = await _api.CreateAsync(a);
        await _api.CreateAsync(b);
        foreach (var path in new[] { $"/api/families/{family.Id}", $"/api/families/{family.Id}/members", $"/api/families/{family.Id}/ownership-transfers" })
            await ErrorAsync(await _api.SendAsync(b, HttpMethod.Get, path), HttpStatusCode.NotFound, FamilyErrorCodes.NotFound);
        await ErrorAsync(await _api.SendAsync(b, HttpMethod.Put, $"/api/families/{family.Id}", new
        { name = "Spoofed", concurrencyStamp = family.ConcurrencyStamp, actorParentId = a.ParentId }), HttpStatusCode.NotFound, FamilyErrorCodes.NotFound);
        (await _api.GetAsync(a, family.Id)).Name.ShouldBe("Test Family");
    }

    [Fact]
    public async Task Role_management_enforces_family_roles_and_updates_the_root_stamp()
    {
        var owner = await _api.ParentAsync();
        var member = await _api.ParentAsync();
        var family = await _api.CreateAsync(owner);
        await _api.AddApprovedAsync(family.Id, owner, member);
        var current = await _api.GetAsync(owner, family.Id);
        current.ConcurrencyStamp.ShouldNotBe(family.ConcurrencyStamp);
        var memberView = await _api.GetAsync(member, family.Id);
        memberView.MyRole.ShouldBe(FamilyMemberRole.Member);
        await ErrorAsync(await _api.SendAsync(member, HttpMethod.Post, $"/api/families/{family.Id}/members/{member.ParentId}/promote",
            FamilyTestClient.Stamp(current)), HttpStatusCode.Forbidden, FamilyErrorCodes.InsufficientRole);
        await ErrorAsync(await _api.SendAsync(owner, HttpMethod.Post, $"/api/families/{family.Id}/leave", FamilyTestClient.Stamp(current)),
            HttpStatusCode.Conflict, FamilyErrorCodes.OwnerCannotLeave);
        var promoted = await _api.SendAsync(owner, HttpMethod.Post, $"/api/families/{family.Id}/members/{member.ParentId}/promote", FamilyTestClient.Stamp(current));
        promoted.EnsureSuccessStatusCode();
        (await _api.GetAsync(member, family.Id)).MyRole.ShouldBe(FamilyMemberRole.Admin);
        var stale = await _api.SendAsync(owner, HttpMethod.Put, $"/api/families/{family.Id}", new UpdateFamilyDto
        { Name = "Stale", ConcurrencyStamp = current.ConcurrencyStamp });
        await ErrorAsync(stale, HttpStatusCode.Conflict, FamilyErrorCodes.ConcurrencyConflict);
        var updated = await _api.GetAsync(member, family.Id);
        var rename = await _api.SendAsync(member, HttpMethod.Put, $"/api/families/{family.Id}", new UpdateFamilyDto
        { Name = "Renamed", ConcurrencyStamp = updated.ConcurrencyStamp });
        rename.EnsureSuccessStatusCode();
        current = await _api.GetAsync(member, family.Id);
        (await _api.SendAsync(member, HttpMethod.Post, $"/api/families/{family.Id}/leave", FamilyTestClient.Stamp(current))).EnsureSuccessStatusCode();
        await ErrorAsync(await _api.SendAsync(member, HttpMethod.Get, $"/api/families/{family.Id}"), HttpStatusCode.NotFound, FamilyErrorCodes.NotFound);
    }

    [Fact]
    public async Task Ownership_acceptance_is_recipient_only_atomic_and_replayable()
    {
        var owner = await _api.ParentAsync();
        var recipient = await _api.ParentAsync();
        var family = await _api.CreateAsync(owner);
        await _api.AddApprovedAsync(family.Id, owner, recipient);
        var transfer = await _api.InitiateAsync(owner, family, recipient);
        var input = new FamilyMutationDto { ConcurrencyStamp = transfer.FamilyConcurrencyStamp };
        await ErrorAsync(await _api.SendAsync(owner, HttpMethod.Post, $"/api/families/{family.Id}/ownership-transfers/{transfer.Id}/accept", input),
            HttpStatusCode.Forbidden, FamilyErrorCodes.InsufficientRole);
        var accepted = await _api.SendAsync(recipient, HttpMethod.Post, $"/api/families/{family.Id}/ownership-transfers/{transfer.Id}/accept", input);
        accepted.EnsureSuccessStatusCode();
        (await accepted.Content.ReadFromJsonAsync<FamilyDto>())!.MyRole.ShouldBe(FamilyMemberRole.Owner);
        (await _api.GetAsync(owner, family.Id)).MyRole.ShouldBe(FamilyMemberRole.Admin);
        var replay = await _api.SendAsync(recipient, HttpMethod.Post, $"/api/families/{family.Id}/ownership-transfers/{transfer.Id}/accept", input);
        replay.EnsureSuccessStatusCode();
        var transfers = await _api.SendAsync(recipient, HttpMethod.Get, $"/api/families/{family.Id}/ownership-transfers");
        transfers.EnsureSuccessStatusCode();
        (await transfers.Content.ReadFromJsonAsync<PagedResultDto<OwnershipTransferDto>>())!.Items.Single().Status.ShouldBe(OwnershipTransferStatus.Accepted);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("unverified")]
    [InlineData("deleted-parent")]
    [InlineData("deleted-user")]
    public async Task Eligibility_is_rechecked_for_existing_access_tokens(string change)
    {
        var actor = await _api.ParentAsync();
        var family = await _api.CreateAsync(actor);
        await _host.InUnitAsync(async sp =>
        {
            if (change == "deleted-parent") await sp.GetRequiredService<IRepository<Parent, Guid>>().DeleteAsync(actor.ParentId);
            else
            {
                var users = sp.GetRequiredService<IRepository<IdentityUser, Guid>>();
                var user = await users.GetAsync(actor.UserId);
                if (change == "inactive") user.SetIsActive(false);
                if (change == "unverified") user.SetEmailConfirmed(false);
                if (change == "deleted-user") await users.DeleteAsync(user);
            }
        });
        await ErrorAsync(await _api.SendAsync(actor, HttpMethod.Get, $"/api/families/{family.Id}"), HttpStatusCode.Forbidden, FamilyErrorCodes.ParentNotEligible);
        await ErrorAsync(await _api.SendAsync(actor, HttpMethod.Post, "/api/families", new CreateFamilyDto
        { Name = "Denied", RequestId = Guid.NewGuid() }), HttpStatusCode.Forbidden, FamilyErrorCodes.ParentNotEligible);
    }

    [Fact]
    public async Task A_member_cannot_probe_recipient_eligibility_by_initiating_transfers()
    {
        var owner = await _api.ParentAsync();
        var member = await _api.ParentAsync();
        var family = await _api.CreateAsync(owner);
        await _api.AddApprovedAsync(family.Id, owner, member);
        var current = await _api.GetAsync(member, family.Id);
        foreach (var target in new[] { owner.ParentId, Guid.NewGuid() })
        {
            var response = await _api.SendAsync(member, HttpMethod.Post, $"/api/families/{family.Id}/ownership-transfers",
                new InitiateOwnershipTransferDto { RequestId = Guid.NewGuid(), ToParentId = target, ConcurrencyStamp = current.ConcurrencyStamp });
            await ErrorAsync(response, HttpStatusCode.Forbidden, FamilyErrorCodes.InsufficientRole);
        }
    }

    [Fact]
    public async Task Disabled_recipient_cannot_receive_ownership_even_with_a_pending_transfer()
    {
        var owner = await _api.ParentAsync();
        var recipient = await _api.ParentAsync();
        var family = await _api.CreateAsync(owner);
        await _api.AddApprovedAsync(family.Id, owner, recipient);
        var transfer = await _api.InitiateAsync(owner, family, recipient);
        await _host.InUnitAsync(async sp =>
        {
            var user = await sp.GetRequiredService<IRepository<IdentityUser, Guid>>().GetAsync(recipient.UserId);
            user.SetIsActive(false);
        });
        await ErrorAsync(await _api.SendAsync(recipient, HttpMethod.Post, $"/api/families/{family.Id}/ownership-transfers/{transfer.Id}/accept",
            new FamilyMutationDto { ConcurrencyStamp = transfer.FamilyConcurrencyStamp }), HttpStatusCode.Forbidden, FamilyErrorCodes.ParentNotEligible);
        (await _api.GetAsync(owner, family.Id)).MyRole.ShouldBe(FamilyMemberRole.Owner);
    }
}
