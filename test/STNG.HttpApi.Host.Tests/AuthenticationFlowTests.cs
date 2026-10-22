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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Shouldly;
using STNG.Authentication;
using STNG.EntityFrameworkCore;
using STNG.Parents;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Data;
using Xunit;

namespace STNG;

[Collection("Authentication host")]
public class AuthenticationFlowTests
{
    private readonly AuthenticationHostFixture _host;
    private readonly ParentLoginTests _flow;
    public AuthenticationFlowTests(AuthenticationHostFixture host) { _host = host; _flow = new(host); }
    private string Code(string email) => Regex.Match(_host.Email.Messages.Last(x => x.Email == email).Body, @"\b\d{6}\b").Value;
    private Task<HttpResponseMessage> Verify(string email, string code) => _host.Client.PostAsJsonAsync("/api/auth/parent/verify-email", new VerifyParentEmailDto { Email = email, Otp = code });
    private Task<HttpResponseMessage> Refresh(string refresh) => _host.Client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["grant_type"] = "refresh_token", ["client_id"] = ParentAuthenticationConsts.MobileClientId, ["refresh_token"] = refresh }));

    [Fact]
    public async Task Malformed_date_of_birth_is_rejected_before_registration_side_effects()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        var messageCount = _host.Email.Messages.Count;
        var response = await _host.Client.PostAsJsonAsync("/api/auth/parent/register", new
        {
            email,
            fullName = "Invalid Date",
            password = "Strong-Password42!",
            dateOfBirth = "2008-02-30",
            phoneCountryIso2 = "EG",
            phoneNumber = "01012345678"
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        _host.Email.Messages.Count.ShouldBe(messageCount);
        await _host.InUnitAsync(async sp =>
            (await sp.GetRequiredService<IdentityUserManager>().FindByEmailAsync(email)).ShouldBeNull());
    }

    [Fact]
    public async Task Concurrent_verification_consumes_code_only_once()
    {
        var email = await _flow.RegisterAsync(false);
        var code = Code(email);
        var responses = await Task.WhenAll(Verify(email, code), Verify(email, code));
        var successes = 0;
        foreach (var response in responses)
        {
            response.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.Conflict);
            if (response.IsSuccessStatusCode && (await response.Content.ReadFromJsonAsync<EmailVerificationResultDto>())!.Verified) successes++;
        }
        successes.ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_registration_cannot_create_duplicate_identity_or_profile()
    {
        var input = new RegisterParentDto
        {
            Email = $"{Guid.NewGuid():N}@example.com",
            FullName = "Race Test",
            Password = "Strong-Password42!",
            DateOfBirth = new DateOnly(1990, 1, 1),
            PhoneCountryIso2 = "EG",
            PhoneNumber = "01012345678"
        };
        var responses = await Task.WhenAll(_host.Client.PostAsJsonAsync("/api/auth/parent/register", input), _host.Client.PostAsJsonAsync("/api/auth/parent/register", input));
        responses.Count(x => x.IsSuccessStatusCode).ShouldBe(1);
        foreach (var response in responses) response.StatusCode.ShouldBeOneOf(HttpStatusCode.NoContent, HttpStatusCode.Forbidden, HttpStatusCode.Conflict);
        await _host.InUnitAsync(async sp =>
        {
            var db = await sp.GetRequiredService<IDbContextProvider<STNGDbContext>>().GetDbContextAsync();
            var users = await db.Users.Where(x => x.NormalizedEmail == input.Email.ToUpperInvariant()).ToListAsync();
            users.Count.ShouldBe(1);
            (await db.Parents.CountAsync(x => x.UserId == users[0].Id)).ShouldBe(1);
            (await db.EmailVerificationOtps.CountAsync(x => x.UserId == users[0].Id)).ShouldBe(1);
        });
    }

    [Fact]
    public async Task SqlServer_rejects_duplicate_parent_and_orphan_profile()
    {
        var email = await _flow.RegisterAsync();
        Guid userId = default;
        await _host.InUnitAsync(async sp => userId = (await sp.GetRequiredService<IdentityUserManager>().FindByEmailAsync(email))!.Id);
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _host.InUnitAsync(async sp =>
            await sp.GetRequiredService<IRepository<Parent, Guid>>().InsertAsync(
                new Parent(Guid.NewGuid(), userId, new DateOnly(1990, 1, 1), "EG", new DateOnly(2026, 10, 14)), autoSave: true)));
        await Should.ThrowAsync<DbUpdateException>(() => _host.InUnitAsync(async sp =>
            await sp.GetRequiredService<IRepository<Parent, Guid>>().InsertAsync(
                new Parent(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(1990, 1, 1), "EG", new DateOnly(2026, 10, 14)), autoSave: true)));
    }

    [Fact]
    public async Task SqlServer_rejects_two_active_challenges_for_one_user()
    {
        var email = await _flow.RegisterAsync(false);
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _host.InUnitAsync(async sp =>
        {
            var user = (await sp.GetRequiredService<IdentityUserManager>().FindByEmailAsync(email))!;
            var issued = sp.GetRequiredService<EmailOtpManager>().Create(user.Id, user.Email);
            await sp.GetRequiredService<IRepository<EmailVerificationOtp, Guid>>().InsertAsync(issued.Otp, autoSave: true);
        }));
    }

    [Fact]
    public async Task Concurrent_resend_leaves_one_active_challenge_and_rejects_old_code()
    {
        var email = await _flow.RegisterAsync(false);
        var oldCode = Code(email);
        _host.Clock.Advance(TimeSpan.FromSeconds(60));
        var input = new ResendVerificationCodeDto { Email = email };
        var responses = await Task.WhenAll(_host.Client.PostAsJsonAsync("/api/auth/parent/resend-verification-code", input),
            _host.Client.PostAsJsonAsync("/api/auth/parent/resend-verification-code", input));
        foreach (var response in responses) response.StatusCode.ShouldBeOneOf(HttpStatusCode.NoContent, HttpStatusCode.Conflict);
        var oldResponse = await Verify(email, oldCode);
        (await oldResponse.Content.ReadFromJsonAsync<EmailVerificationResultDto>())!.Verified.ShouldBeFalse();
        var responseNew = await Verify(email, Code(email));
        (await responseNew.Content.ReadFromJsonAsync<EmailVerificationResultDto>())!.Verified.ShouldBeTrue();
        _host.Email.Messages.Count(x => x.Email == email).ShouldBe(2);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("locked")]
    [InlineData("unconfirmed")]
    [InlineData("non-parent")]
    [InlineData("security-stamp")]
    public async Task Refresh_rechecks_current_account_state(string change)
    {
        var email = await _flow.RegisterAsync();
        var login = await _flow.LoginAsync(email);
        login.EnsureSuccessStatusCode();
        using var tokens = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var refresh = tokens.RootElement.GetProperty("refresh_token").GetString()!;
        await _host.InUnitAsync(async sp =>
        {
            var users = sp.GetRequiredService<IdentityUserManager>();
            var user = (await users.FindByEmailAsync(email))!;
            if (change == "inactive") { user.SetIsActive(false); (await users.UpdateAsync(user)).CheckErrors(); }
            if (change == "locked") (await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(10))).CheckErrors();
            if (change == "unconfirmed") { user.SetEmailConfirmed(false); (await users.UpdateAsync(user)).CheckErrors(); }
            if (change == "security-stamp") (await users.UpdateSecurityStampAsync(user)).CheckErrors();
            if (change == "non-parent") await sp.GetRequiredService<IRepository<Parent, Guid>>().DeleteAsync(x => x.UserId == user.Id);
        });
        (await Refresh(refresh)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Soft_deleted_parent_cannot_use_an_existing_access_token()
    {
        var email = await _flow.RegisterAsync();
        var login = await _flow.LoginAsync(email);
        login.EnsureSuccessStatusCode();
        using var tokens = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var accessToken = tokens.RootElement.GetProperty("access_token").GetString();
        accessToken.ShouldNotBeNullOrWhiteSpace();

        await _host.InUnitAsync(async sp =>
        {
            var user = (await sp.GetRequiredService<IdentityUserManager>().FindByEmailAsync(email))!;
            await sp.GetRequiredService<IRepository<Parent, Guid>>()
                .DeleteAsync(x => x.UserId == user.Id);
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await _host.Client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Identity_lockout_counts_wrong_passwords()
    {
        var email = await _flow.RegisterAsync();
        for (var i = 0; i < 5; i++) (await _flow.LoginAsync(email, "Wrong-Password42!")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await _host.InUnitAsync(async sp =>
        {
            var users = sp.GetRequiredService<IdentityUserManager>();
            (await users.IsLockedOutAsync((await users.FindByEmailAsync(email))!)).ShouldBeTrue();
        });
        (await _flow.LoginAsync(email)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Code_for_previous_email_cannot_confirm_changed_address()
    {
        var email = await _flow.RegisterAsync(false);
        var code = Code(email);
        var newEmail = $"{Guid.NewGuid():N}@example.com";
        await _host.InUnitAsync(async sp =>
        {
            var users = sp.GetRequiredService<IdentityUserManager>();
            (await users.SetEmailAsync((await users.FindByEmailAsync(email))!, newEmail)).CheckErrors();
        });
        var response = await Verify(newEmail, code);
        (await response.Content.ReadFromJsonAsync<EmailVerificationResultDto>())!.Verified.ShouldBeFalse();
    }

    [Fact]
    public async Task Mobile_client_is_public_and_has_only_required_grants()
    {
        await _host.InUnitAsync(async sp =>
        {
            var manager = sp.GetRequiredService<IOpenIddictApplicationManager>();
            var client = (await manager.FindByClientIdAsync(ParentAuthenticationConsts.MobileClientId))!;
            (await manager.GetClientTypeAsync(client)).ShouldBe(OpenIddictConstants.ClientTypes.Public);
            var descriptor = new OpenIddictApplicationDescriptor();
            await manager.PopulateAsync(descriptor, client);
            descriptor.ClientSecret.ShouldBeNullOrEmpty();
            descriptor.Permissions.ShouldContain(OpenIddictConstants.Permissions.GrantTypes.Password);
            descriptor.Permissions.ShouldContain(OpenIddictConstants.Permissions.GrantTypes.RefreshToken);
            descriptor.Permissions.ShouldNotContain(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials);
        });
    }

    [Fact]
    public async Task Authentication_routes_enforce_request_throttling()
    {
        // A separate source IP isolates this counter from the rest of the fixture.
        var source = IPAddress.Parse("192.0.2.123");
        for (var i = 0; i < 1000; i++)
        {
            var result = await _host.Server.SendAsync(context =>
            {
                context.Connection.RemoteIpAddress = source;
                context.Request.Scheme = "https";
                context.Request.Path = "/api/auth/me";
            });
            result.Response.StatusCode.ShouldBe(401);
        }
        var rejected = await _host.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = source;
            context.Request.Scheme = "https";
            context.Request.Path = "/api/auth/me";
        });
        rejected.Response.StatusCode.ShouldBe(429);
    }
}
