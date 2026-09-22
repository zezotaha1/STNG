using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using STNG.Parents;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Uow;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace STNG.Authentication;

[AllowAnonymous, DisableAuditing, RemoteService(false)]
public class ParentEmailVerificationAppService : STNGAppService, IParentEmailVerificationAppService
{
    private readonly IdentityUserManager _users;
    private readonly IRepository<Parent, Guid> _parents;
    private readonly IRepository<EmailVerificationOtp, Guid> _otps;
    private readonly EmailOtpManager _codes;
    private readonly VerificationEmailSender _email;
    private readonly IUnitOfWorkManager _units;
    private readonly IOptions<EmailOtpOptions> _options;

    public ParentEmailVerificationAppService(IdentityUserManager users, IRepository<Parent, Guid> parents,
        IRepository<EmailVerificationOtp, Guid> otps, EmailOtpManager codes, VerificationEmailSender email,
        IUnitOfWorkManager units, IOptions<EmailOtpOptions> options)
    {
        _users = users; _parents = parents; _otps = otps; _codes = codes;
        _email = email; _units = units; _options = options;
    }

    [UnitOfWork(IsDisabled = true)]
    public virtual async Task<EmailVerificationResultDto> VerifyAsync(VerifyParentEmailDto input)
    {
        using var unit = _units.Begin(new AbpUnitOfWorkOptions { IsTransactional = true, IsolationLevel = IsolationLevel.Serializable }, requiresNew: true);
        var user = await FindParentUserAsync(input.Email);
        if (user == null || user.EmailConfirmed) return new();
        var otp = await FindLatestAsync(user.Id);
        if (otp == null || !otp.IsActive(Clock.Now)) return new();
        var verified = otp.TryVerify(_codes.Matches(otp, input.Otp), Clock.Now);
        await _otps.UpdateAsync(otp);
        if (verified)
        {
            var identityToken = await _users.GenerateEmailConfirmationTokenAsync(user);
            (await _users.ConfirmEmailAsync(user, identityToken)).CheckErrors();
        }
        // Commit failed attempts too: throwing here would roll back the attempt counter.
        await unit.CompleteAsync();
        return new() { Verified = verified };
    }

    [UnitOfWork(IsDisabled = true)]
    public virtual async Task ResendAsync(ResendVerificationCodeDto input)
    {
        string code;
        string email;
        using (var unit = _units.Begin(new AbpUnitOfWorkOptions { IsTransactional = true, IsolationLevel = IsolationLevel.Serializable }, requiresNew: true))
        {
            var user = await FindParentUserAsync(input.Email);
            if (user == null || user.EmailConfirmed) return;
            var previous = await FindLatestAsync(user.Id);
            if (previous != null)
            {
                if (!previous.CanResend(Clock.Now, TimeSpan.FromSeconds(_options.Value.ResendCooldownSeconds))) return;
                previous.Invalidate(Clock.Now);
                await _otps.UpdateAsync(previous, autoSave: true);
            }
            var issued = _codes.Create(user.Id);
            while (previous != null && _codes.Matches(previous, issued.Code))
                issued = _codes.Create(user.Id);
            code = issued.Code;
            email = user.Email;
            await _otps.InsertAsync(issued.Otp);
            await unit.CompleteAsync();
        }
        await _email.SendAsync(email, code);
    }

    private async Task<IdentityUser?> FindParentUserAsync(string email)
    {
        var user = await _users.FindByEmailAsync(email.Trim());
        return user != null && await _parents.AnyAsync(x => x.UserId == user.Id) ? user : null;
    }

    private async Task<EmailVerificationOtp?> FindLatestAsync(Guid userId) =>
        await AsyncExecuter.FirstOrDefaultAsync((await _otps.GetQueryableAsync())
            .Where(x => x.UserId == userId && x.Purpose == EmailVerificationOtp.ParentRegistration)
            .OrderByDescending(x => x.LastSentAt));
}
