using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using STNG.Authentication;
using Volo.Abp.Identity;
using Xunit;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace STNG;

[Collection("Authentication host")]
public class ParentLoginTests
{
    private readonly AuthenticationHostFixture _host;
    private const string Password = "Strong-Password42!";
    public ParentLoginTests(AuthenticationHostFixture host) => _host = host;

    internal async Task<string> RegisterAsync(bool verify = true)
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        var response = await _host.Client.PostAsJsonAsync("/api/auth/parent/register", new RegisterParentDto
        {
            Email = email,
            FullName = "Parent Test",
            Password = Password,
            Job = "Engineer",
            DateOfBirth = new DateOnly(1990, 1, 1),
            PhoneCountryIso2 = "EG",
            PhoneNumber = "01012345678"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        if (verify)
        {
            var code = Regex.Match(_host.Email.Messages.Last(x => x.Email == email).Body, @"\b\d{6}\b").Value;
            var verification = await _host.Client.PostAsJsonAsync("/api/auth/parent/verify-email", new VerifyParentEmailDto { Email = email, Otp = code });
            verification.EnsureSuccessStatusCode();
            (await verification.Content.ReadFromJsonAsync<EmailVerificationResultDto>())!.Verified.ShouldBeTrue();
        }
        return email;
    }

    internal Task<HttpResponseMessage> LoginAsync(string email, string password = Password) => _host.Client.PostAsync("/connect/token",
        new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "password", ["client_id"] = ParentAuthenticationConsts.MobileClientId,
            ["username"] = email, ["password"] = password, ["scope"] = "openid profile email roles offline_access STNG" }));

    [Fact]
    public async Task Verified_parent_receives_tokens_calls_me_and_refreshes()
    {
        var email = await RegisterAsync();
        var login = await LoginAsync(email);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var tokens = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var access = tokens.RootElement.GetProperty("access_token").GetString();
        var refresh = tokens.RootElement.GetProperty("refresh_token").GetString();
        access.ShouldNotBeNullOrWhiteSpace();
        refresh.ShouldNotBeNullOrWhiteSpace();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var me = await _host.Client.SendAsync(request);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        var profile = (await me.Content.ReadFromJsonAsync<ParentCurrentUserDto>())!;
        profile.Email.ShouldBe(email);
        profile.ParentId.ShouldNotBe(Guid.Empty);
        var renewed = await _host.Client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["grant_type"] = "refresh_token", ["client_id"] = ParentAuthenticationConsts.MobileClientId, ["refresh_token"] = refresh! }));
        renewed.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var newTokens = JsonDocument.Parse(await renewed.Content.ReadAsStringAsync());
        newTokens.RootElement.GetProperty("access_token").GetString().ShouldNotBe(access);
    }

    [Theory]
    [InlineData("unverified")]
    [InlineData("wrong-password")]
    [InlineData("unknown")]
    [InlineData("locked")]
    [InlineData("inactive")]
    [InlineData("non-parent")]
    public async Task Ineligible_credentials_are_rejected_without_account_details(string scenario)
    {
        var email = scenario == "unknown" ? "unknown@example.com" : await RegisterAsync(scenario != "unverified");
        if (scenario is "locked" or "inactive" or "non-parent")
        {
            await _host.InUnitAsync(async sp =>
            {
                var users = sp.GetRequiredService<IdentityUserManager>();
                var user = (await users.FindByEmailAsync(email))!;
                if (scenario == "locked") (await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(10))).CheckErrors();
                if (scenario == "inactive") { user.SetIsActive(false); (await users.UpdateAsync(user)).CheckErrors(); }
                if (scenario == "non-parent") await sp.GetRequiredService<Volo.Abp.Domain.Repositories.IRepository<STNG.Parents.Parent, Guid>>().DeleteAsync(x => x.UserId == user.Id);
            });
        }
        var response = await LoginAsync(email, scenario == "wrong-password" ? "Wrong-Password42!" : Password);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        error.RootElement.GetProperty("error").GetString().ShouldBe("invalid_grant");
        error.RootElement.GetProperty("error_description").GetString().ShouldBe("Invalid credentials or account unavailable.");
    }

    [Fact]
    public async Task Me_requires_authentication() =>
        (await _host.Client.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
}
