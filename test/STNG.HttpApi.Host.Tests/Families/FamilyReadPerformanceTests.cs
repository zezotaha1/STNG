using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
public class FamilyReadPerformanceTests
{
    private readonly AuthenticationHostFixture _host;
    private readonly FamilyTestClient _api;
    private string EvidenceDirectory => Path.GetFullPath(Path.Combine(
        _host.Services.GetRequiredService<IHostEnvironment>().ContentRootPath, "..", "..", "artifacts", "family-hardening", "queries"));

    public FamilyReadPerformanceTests(AuthenticationHostFixture host) { _host = host; _api = new(host); }

    private sealed record SeededTransfer(Guid Id, Guid From, Guid To, DateTime RequestedAt);
    private sealed record ReadData(FamilyTestActor Owner, FamilyTestActor Member, FamilyTestActor Other,
        Guid FamilyId, FamilyMemberDto[] Members, List<SeededTransfer> Transfers);

    private async Task<ReadData> SeedAsync(int extraMembers, int historyCount, bool changeOwner = false)
    {
        var originalOwner = await _api.ParentAsync();
        var member = await _api.ParentAsync();
        var other = await _api.ParentAsync();
        var family = await _api.CreateAsync(originalOwner);
        await _api.AddApprovedAsync(family.Id, originalOwner, member);
        await _api.AddApprovedAsync(family.Id, originalOwner, other);
        var transfers = new List<SeededTransfer>();
        FamilyMemberDto[] members = Array.Empty<FamilyMemberDto>();
        await _host.InUnitAsync(async sp =>
        {
            var aggregate = await sp.GetRequiredService<IRepository<Family, Guid>>().GetAsync(family.Id);
            var users = new List<IdentityUser>();
            var parents = new List<Parent>();
            for (var i = 0; i < extraMembers; i++)
            {
                var userId = Guid.NewGuid();
                var user = new IdentityUser(userId, userId.ToString("N"), $"{userId:N}@example.com");
                user.SetEmailConfirmed(true);
                users.Add(user);
                var parent = new Parent(Guid.NewGuid(), userId, new DateOnly(1990, 1, 1), "EG", DateOnly.FromDateTime(_host.Clock.Now));
                parents.Add(parent);
                aggregate.AddMember(originalOwner.ParentId, Guid.NewGuid(), parent.Id, _host.Clock.Now);
                if (i < 10) aggregate.RemoveMember(originalOwner.ParentId, parent.Id, _host.Clock.Now);
            }
            if (users.Count > 0)
            {
                await sp.GetRequiredService<IRepository<IdentityUser, Guid>>().InsertManyAsync(users);
                await sp.GetRequiredService<IRepository<Parent, Guid>>().InsertManyAsync(parents);
            }
            for (var i = 0; i < historyCount; i++)
            {
                if (changeOwner && i == historyCount / 2)
                {
                    var handover = aggregate.InitiateOwnershipTransfer(originalOwner.ParentId, Guid.NewGuid(), member.ParentId,
                        _host.Clock.Now, _host.Clock.Now.AddDays(1));
                    aggregate.CompleteOwnershipTransfer(member.ParentId, handover.Id, _host.Clock.Now);
                    aggregate.DemoteAdmin(member.ParentId, originalOwner.ParentId, _host.Clock.Now);
                    transfers.Add(new(handover.Id, handover.FromParentId, handover.ToParentId, handover.RequestedAt));
                }
                var switched = changeOwner && i >= historyCount / 2;
                var from = switched ? member.ParentId : originalOwner.ParentId;
                var to = i % 4 == 0 ? (switched ? originalOwner.ParentId : member.ParentId) : other.ParentId;
                var transfer = aggregate.InitiateOwnershipTransfer(from, Guid.NewGuid(), to, _host.Clock.Now, _host.Clock.Now.AddDays(1));
                aggregate.CancelOwnershipTransfer(from, transfer.Id, _host.Clock.Now);
                transfers.Add(new(transfer.Id, transfer.FromParentId, transfer.ToParentId, transfer.RequestedAt));
            }
            members = aggregate.Members.Select(x => new FamilyMemberDto
            { Id = x.Id, ParentId = x.ParentId, Status = x.Status, Role = x.Role, JoinedAt = x.JoinedAt }).ToArray();
        });
        return new ReadData(changeOwner ? member : originalOwner, changeOwner ? originalOwner : member,
            other, family.Id, members, transfers);
    }

    private async Task<(T Result, FamilyReadProbe.Observation Trace)> GetAsync<T>(FamilyTestActor actor, string path,
        string evidenceName, int expectedCommands)
    {
        using var trace = _host.FamilyReads.Observe();
        using var response = await _api.SendAsync(actor, HttpMethod.Get, path);
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<T>())!;
        trace.MaterializedEntities.ShouldBeEmpty();
        trace.Commands.Count.ShouldBe(expectedCommands);
        trace.WriteSql(EvidenceDirectory, evidenceName);
        return (result, trace);
    }

    private static FamilyReadCommand AssertPaged(FamilyReadProbe.Observation trace, Guid familyId)
    {
        var count = trace.Commands.Single(x => x.Sql.Contains("COUNT_BIG("));
        count.Sql.ShouldNotContain("ORDER BY");
        count.Sql.ShouldNotContain("[Name]");
        var page = trace.Commands.Single(x => x.Sql.Contains("OFFSET"));
        page.Sql.ShouldContain("FETCH NEXT");
        page.Sql.IndexOf("WHERE", StringComparison.Ordinal).ShouldBeLessThan(page.Sql.IndexOf("OFFSET", StringComparison.Ordinal));
        foreach (var command in new[] { count, page })
            command.Parameters.Any(x => x.Value is Guid id && id == familyId).ShouldBeTrue();
        return page;
    }

    private async Task SavePlanAsync(FamilyReadCommand query, string name)
    {
        await _host.InUnitAsync(async sp =>
        {
            var db = await sp.GetRequiredService<IDbContextProvider<STNGDbContext>>().GetDbContextAsync();
            await using var connection = new SqlConnection(db.Database.GetConnectionString());
            await connection.OpenAsync();
            await using var setting = connection.CreateCommand();
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SET STATISTICS XML ON;\n" + query.Sql + ";\nSET STATISTICS XML OFF;";
                command.Parameters.AddRange(query.Parameters.Select(x => (SqlParameter)((ICloneable)x).Clone()).ToArray());
                await using var reader = await command.ExecuteReaderAsync();
                string? plan = null;
                do
                {
                    while (await reader.ReadAsync())
                    {
                        if (reader.FieldCount != 1) continue;
                        var value = reader.GetValue(0) switch { string text => text, SqlXml sqlXml => sqlXml.Value, _ => null };
                        if (value?.Contains("ShowPlanXML", StringComparison.Ordinal) == true) plan = value;
                    }
                } while (await reader.NextResultAsync());
                plan.ShouldNotBeNull();
                var xml = XDocument.Parse(plan);
                xml.Descendants().Where(x => x.Name.LocalName == "Object").Any(x => x.Attribute("Index") != null).ShouldBeTrue();
                xml.Descendants().Where(x => x.Name.LocalName == "RunTimeCountersPerThread")
                    .Any(x => x.Attribute("ActualRows") != null).ShouldBeTrue();
                Directory.CreateDirectory(EvidenceDirectory);
                await File.WriteAllTextAsync(Path.Combine(EvidenceDirectory, name + ".sqlplan"), plan);
            }
            finally
            {
                setting.CommandText = "SET STATISTICS XML OFF";
                await setting.ExecuteNonQueryAsync();
            }
        });
    }

    [Fact]
    public async Task Member_pages_are_deterministic_SQL_pages_without_aggregate_materialization()
    {
        var data = await SeedAsync(extraMembers: 128, historyCount: 512);
        var expected = data.Members.OrderBy(x => x.JoinedAt).ThenBy(x => new SqlGuid(x.Id)).ToArray();
        var first = await GetAsync<PagedResultDto<FamilyMemberDto>>(data.Owner,
            $"/api/families/{data.FamilyId}/members?skipCount=17&maxResultCount=13", "members-page", 3);
        first.Result.TotalCount.ShouldBe(131);
        first.Result.Items.Select(x => x.Id).ShouldBe(expected.Skip(17).Take(13).Select(x => x.Id));
        var page = AssertPaged(first.Trace, data.FamilyId);
        first.Trace.Commands.ShouldAllBe(x => !x.Sql.Contains("[AppOwnershipTransfers]") && !x.Sql.Contains("[AppFamilyMembershipHistory]"));
        var repeat = await GetAsync<PagedResultDto<FamilyMemberDto>>(data.Member,
            $"/api/families/{data.FamilyId}/members?skipCount=17&maxResultCount=13", "members-repeat", 3);
        repeat.Result.Items.Select(x => x.Id).ShouldBe(first.Result.Items.Select(x => x.Id));
        var removedIndex = Array.FindIndex(expected, x => x.Status == FamilyMemberStatus.Removed);
        var retained = await GetAsync<PagedResultDto<FamilyMemberDto>>(data.Owner,
            $"/api/families/{data.FamilyId}/members?skipCount={removedIndex}&maxResultCount=1", "members-retained", 3);
        retained.Result.Items.ShouldContain(x => x.Status == FamilyMemberStatus.Removed);
        await SavePlanAsync(page, "members-page");
    }

    [Fact]
    public async Task Transfer_pages_apply_live_role_visibility_before_count_and_paging_over_large_history()
    {
        var data = await SeedAsync(extraMembers: 0, historyCount: 1024, changeOwner: true);
        var expected = data.Transfers.OrderByDescending(x => x.RequestedAt).ThenBy(x => new SqlGuid(x.Id)).ToArray();
        var owner = await GetAsync<PagedResultDto<OwnershipTransferDto>>(data.Owner,
            $"/api/families/{data.FamilyId}/ownership-transfers?skipCount=19&maxResultCount=11", "transfers-owner", 3);
        owner.Result.TotalCount.ShouldBe(1025);
        owner.Result.Items.Select(x => x.Id).ShouldBe(expected.Skip(19).Take(11).Select(x => x.Id));
        var page = AssertPaged(owner.Trace, data.FamilyId);
        page.Sql.ShouldContain("[Role]");
        page.Sql.ShouldContain("[FromParentId]");
        page.Sql.ShouldContain("[ToParentId]");
        var visible = expected.Where(x => x.From == data.Member.ParentId || x.To == data.Member.ParentId).ToArray();
        visible.Length.ShouldBeLessThan(expected.Length);
        visible.ShouldContain(x => x.From == data.Member.ParentId);
        visible.ShouldContain(x => x.To == data.Member.ParentId);
        var member = await GetAsync<PagedResultDto<OwnershipTransferDto>>(data.Member,
            $"/api/families/{data.FamilyId}/ownership-transfers?skipCount=7&maxResultCount=9", "transfers-member", 3);
        member.Result.TotalCount.ShouldBe(visible.Length);
        member.Result.Items.Select(x => x.Id).ShouldBe(visible.Skip(7).Take(9).Select(x => x.Id));
        AssertPaged(member.Trace, data.FamilyId);
        member.Trace.Commands.Where(x => x.Sql.Contains("COUNT_BIG(")).Single().Sql.ShouldContain("[Role]");
        var repeat = await GetAsync<PagedResultDto<OwnershipTransferDto>>(data.Member,
            $"/api/families/{data.FamilyId}/ownership-transfers?skipCount=7&maxResultCount=9", "transfers-repeat", 3);
        repeat.Result.Items.Select(x => x.Id).ShouldBe(member.Result.Items.Select(x => x.Id));
        await SavePlanAsync(page, "transfers-owner");
        await SavePlanAsync(member.Trace.Commands.Single(x => x.Sql.Contains("OFFSET")), "transfers-member");
    }

    [Fact]
    public async Task Details_and_my_families_use_scalar_projections_without_loading_retained_collections()
    {
        var data = await SeedAsync(extraMembers: 16, historyCount: 512);
        var second = await _api.CreateAsync(data.Owner);
        await _api.CreateAsync(data.Other);
        var details = await GetAsync<FamilyDto>(data.Member, $"/api/families/{data.FamilyId}", "details", 1);
        details.Result.Id.ShouldBe(data.FamilyId);
        details.Result.Name.ShouldBe("Test Family");
        details.Result.MyRole.ShouldBe(FamilyMemberRole.Member);
        details.Trace.Commands.Single().Sql.ShouldContain("TOP(2)");
        details.Trace.Commands.Single().Sql.ShouldNotContain("[AppOwnershipTransfers]");
        details.Trace.Commands.Single().Sql.ShouldNotContain("[AppFamilyMembershipHistory]");
        var expected = new[] { data.FamilyId, second.Id }.OrderBy(x => new SqlGuid(x)).ToArray();
        var first = await GetAsync<PagedResultDto<FamilyDto>>(data.Owner, "/api/families/mine?maxResultCount=1", "mine-first", 2);
        var next = await GetAsync<PagedResultDto<FamilyDto>>(data.Owner, "/api/families/mine?skipCount=1&maxResultCount=1", "mine-next", 2);
        first.Result.TotalCount.ShouldBe(2);
        next.Result.TotalCount.ShouldBe(2);
        first.Result.Items.Concat(next.Result.Items).Select(x => x.Id).ShouldBe(expected);
        first.Result.Items.Single().MyRole.ShouldBe(FamilyMemberRole.Owner);
        foreach (var command in first.Trace.Commands)
        {
            command.Sql.ShouldNotContain("[AppOwnershipTransfers]");
            command.Sql.ShouldNotContain("[AppFamilyMembershipHistory]");
            command.Parameters.Any(x => x.Value is Guid id && id == data.Owner.ParentId).ShouldBeTrue();
        }
        await SavePlanAsync(details.Trace.Commands.Single(), "details");
        await SavePlanAsync(first.Trace.Commands.Single(x => x.Sql.Contains("OFFSET")), "mine-first");
    }

    [Fact]
    public async Task Scoped_reads_return_the_same_not_found_for_outsiders_former_members_and_unknown_families()
    {
        var data = await SeedAsync(extraMembers: 0, historyCount: 8);
        var outsider = await _api.ParentAsync();
        await _api.CreateAsync(outsider);
        var current = await _api.GetAsync(data.Member, data.FamilyId);
        (await _api.SendAsync(data.Member, HttpMethod.Post, $"/api/families/{data.FamilyId}/leave", FamilyTestClient.Stamp(current)))
            .EnsureSuccessStatusCode();
        foreach (var actor in new[] { outsider, data.Member })
        foreach (var familyId in new[] { data.FamilyId, Guid.NewGuid() })
        foreach (var suffix in new[] { "", "/members", "/ownership-transfers" })
        {
            using var trace = _host.FamilyReads.Observe();
            using var response = await _api.SendAsync(actor, HttpMethod.Get, $"/api/families/{familyId}{suffix}");
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await response.Content.ReadAsStringAsync()).ShouldContain(FamilyErrorCodes.NotFound);
            trace.MaterializedEntities.ShouldBeEmpty();
            trace.Commands.Count.ShouldBe(1);
            trace.Commands.Single().Sql.ShouldNotContain("[AppOwnershipTransfers]");
        }
    }

    [Fact]
    public async Task Inactive_family_read_errors_are_preserved_without_loading_collections()
    {
        var data = await SeedAsync(extraMembers: 0, historyCount: 8);
        await _host.InUnitAsync(async sp =>
        {
            var family = await sp.GetRequiredService<IRepository<Family, Guid>>().GetAsync(data.FamilyId);
            family.BeginArchiving(data.Owner.ParentId, _host.Clock.Now);
        });
        foreach (var suffix in new[] { "", "/members", "/ownership-transfers" })
        {
            using var trace = _host.FamilyReads.Observe();
            using var response = await _api.SendAsync(data.Member, HttpMethod.Get, $"/api/families/{data.FamilyId}{suffix}");
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await response.Content.ReadAsStringAsync()).ShouldContain(FamilyErrorCodes.NotActive);
            trace.MaterializedEntities.ShouldBeEmpty();
            trace.Commands.Count.ShouldBe(1);
        }
    }
}
