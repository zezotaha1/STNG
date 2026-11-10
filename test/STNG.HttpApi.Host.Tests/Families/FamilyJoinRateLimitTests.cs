using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using STNG.Families;
using Xunit;

namespace STNG;

[Collection("Authentication host")]
public class FamilyJoinRateLimitTests
{
    private readonly AuthenticationHostFixture _host;
    public FamilyJoinRateLimitTests(AuthenticationHostFixture host) => _host = host;

    [Fact]
    public async Task Ip_limit_also_protects_anonymous_attempts_before_authentication()
    {
        using var factory = _host.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Family:Join:AttemptsPerIpPerMinute"] = "2" })));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(_host.Authority), AllowAutoRedirect = false });
        foreach (var expected in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests })
            (await client.PostAsJsonAsync("/api/family-join-requests", new { RequestId = Guid.NewGuid(), Kind = 0, Secret = "invalid" })).StatusCode.ShouldBe(expected);
    }

    [Fact]
    public async Task Authenticated_parent_limit_counts_invalid_secrets_and_does_not_block_other_parents()
    {
        var api = new FamilyTestClient(_host); var actor = await api.ParentAsync(); var other = await api.ParentAsync();
        using var factory = _host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<FamilyJoinOptions>(options => options.AttemptsPerParentPerMinute = 2)));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(_host.Authority), AllowAutoRedirect = false });
        async Task<HttpResponseMessage> Attempt(FamilyTestActor parent)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/family-join-requests");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", parent.Token);
            request.Content = JsonContent.Create(new { RequestId = Guid.NewGuid(), Kind = 0, Secret = "invalid" });
            return await client.SendAsync(request);
        }
        (await Attempt(actor)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Attempt(actor)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var limited = await Attempt(actor);
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await limited.Content.ReadAsStringAsync()).ShouldContain(FamilyJoinErrorCodes.AttemptsLimited);
        (await Attempt(other)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
