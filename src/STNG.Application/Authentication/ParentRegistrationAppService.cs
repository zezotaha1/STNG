using System;
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
public class ParentRegistrationAppService : STNGAppService, IParentRegistrationAppService
{
    private readonly IdentityUserManager _users;
    private readonly IRepository<Parent, Guid> _parents;
    private readonly IRepository<EmailVerificationOtp, Guid> _otps;
    private readonly EmailOtpManager _codes;
    private readonly VerificationEmailSender _email;
    private readonly IUnitOfWorkManager _units;
    private readonly IOptions<IdentityOptions> _identityOptions;

    public ParentRegistrationAppService(IdentityUserManager users, IRepository<Parent, Guid> parents,
        IRepository<EmailVerificationOtp, Guid> otps, EmailOtpManager codes, VerificationEmailSender email,
        IUnitOfWorkManager units, IOptions<IdentityOptions> identityOptions)
    {
        _users = users; _parents = parents; _otps = otps; _codes = codes;
        _email = email; _units = units; _identityOptions = identityOptions;
    }

    [UnitOfWork(IsDisabled = true)]
    public virtual async Task RegisterAsync(RegisterParentDto input)
    {
        var email = input.Email.Trim();
        string code;
        using (var unit = _units.Begin(requiresNew: true, isTransactional: true))
        {
            await _identityOptions.SetAsync();
            if (await _users.FindByEmailAsync(email) != null)
                throw new BusinessException("STNG:RegistrationUnavailable");
            var user = new IdentityUser(GuidGenerator.Create(), GuidGenerator.Create().ToString("N"), email)
            { Name = input.FullName.Trim() };
            (await _users.CreateAsync(user, input.Password)).CheckErrors();
            await _parents.InsertAsync(new Parent(GuidGenerator.Create(), user.Id, input.Job));
            var issued = _codes.Create(user.Id, user.Email);
            code = issued.Code;
            await _otps.InsertAsync(issued.Otp);
            await unit.CompleteAsync();
        }
        await _email.SendAsync(email, code);
    }
}
