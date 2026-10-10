using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using STNG.Parents;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.OpenIddict.Controllers;

namespace STNG.Authentication;

[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(TokenController))]
[DisableAuditing, RemoteService(false)]
public class ParentTokenController : TokenController
{
    private readonly ParentAccessManager _parents;
    private readonly IdentityUserManager _users;
    public ParentTokenController(ParentAccessManager parents, IdentityUserManager users)
    {
        _parents = parents;
        _users = users;
    }

    protected override async Task<IActionResult> HandlePasswordAsync(OpenIddictRequest request)
    {
        if (request.ClientId != ParentAuthenticationConsts.MobileClientId) return InvalidCredentials();

        var user = await _users.FindByEmailAsync(request.Username?.Trim() ?? "");
        if (!await _parents.IsEligibleAsync(user)) return InvalidCredentials();
        request.Username = user!.UserName;
        // ABP applies password policy, lockout, active-user and two-factor checks.
        var result = await base.HandlePasswordAsync(request);
        return result is SignInResult ? result : InvalidCredentials();
    }

    protected override async Task<IActionResult> HandleRefreshTokenAsync(OpenIddictRequest request)
    {
        if (request.ClientId == ParentAuthenticationConsts.MobileClientId)
        {
            var principal = (await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;
            var user = principal == null ? null : await SignInManager.ValidateSecurityStampAsync(principal);
            if (!await _parents.IsEligibleAsync(user)) return InvalidCredentials();
        }
        return await base.HandleRefreshTokenAsync(request);
    }

    protected override async Task<bool> PreSignInCheckAsync(IdentityUser user) =>
        (HttpContext.GetOpenIddictServerRequest()?.ClientId != ParentAuthenticationConsts.MobileClientId ||
         await _parents.IsEligibleAsync(user)) && await base.PreSignInCheckAsync(user);

    private ForbidResult InvalidCredentials() => Forbid(new AuthenticationProperties(new Dictionary<string, string?>
    {
        [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Invalid credentials or account unavailable."
    }), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}
