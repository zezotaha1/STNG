using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using STNG.Authentication;
using STNG.Families;
using Volo.Abp.Domain.Repositories;

namespace STNG;

internal sealed record FamilyTestActor(Guid ParentId, Guid UserId, string Token);

internal sealed class FamilyTestClient
{
    private readonly AuthenticationHostFixture _host;
    public FamilyTestClient(AuthenticationHostFixture host) => _host = host;

    public async Task<FamilyTestActor> ParentAsync()
    {
        var auth = new ParentLoginTests(_host);
        var email = await auth.RegisterAsync();
        var login = await auth.LoginAsync(email);
        login.EnsureSuccessStatusCode();
        using var tokens = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = tokens.RootElement.GetProperty("access_token").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _host.Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var profile = (await response.Content.ReadFromJsonAsync<ParentCurrentUserDto>())!;
        return new FamilyTestActor(profile.ParentId, profile.Id, token);
    }

    public async Task<HttpResponseMessage> SendAsync(FamilyTestActor actor, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", actor.Token);
        if (body != null) request.Content = JsonContent.Create(body);
        return await _host.Client.SendAsync(request);
    }

    public async Task<FamilyDto> CreateAsync(FamilyTestActor actor, Guid? requestId = null)
    {
        var response = await SendAsync(actor, HttpMethod.Post, "/api/families",
            new CreateFamilyDto { RequestId = requestId ?? Guid.NewGuid(), Name = "  Test Family  " });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FamilyDto>())!;
    }

    public async Task<FamilyDto> GetAsync(FamilyTestActor actor, Guid id)
    {
        var response = await SendAsync(actor, HttpMethod.Get, $"/api/families/{id}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FamilyDto>())!;
    }

    public async Task AddApprovedAsync(Guid familyId, FamilyTestActor owner, FamilyTestActor member)
    {
        await _host.InUnitAsync(async sp =>
        {
            var family = await sp.GetRequiredService<IRepository<Family, Guid>>().GetAsync(familyId);
            await sp.GetRequiredService<FamilyManager>().AddApprovedMemberAsync(family, owner.ParentId, member.ParentId);
        });
    }

    public async Task<OwnershipTransferDto> InitiateAsync(FamilyTestActor owner, FamilyDto family, FamilyTestActor recipient)
    {
        var current = await GetAsync(owner, family.Id);
        var response = await SendAsync(owner, HttpMethod.Post, $"/api/families/{family.Id}/ownership-transfers",
            new InitiateOwnershipTransferDto { RequestId = Guid.NewGuid(), ToParentId = recipient.ParentId, ConcurrencyStamp = current.ConcurrencyStamp });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OwnershipTransferDto>())!;
    }

    public static FamilyMutationDto Stamp(FamilyDto family) => new() { ConcurrencyStamp = family.ConcurrencyStamp };
}
