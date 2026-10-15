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
    private readonly IInternationalPhoneNumberService _phoneNumbers;

    public ParentRegistrationAppService(IdentityUserManager users, IRepository<Parent, Guid> parents,
        IRepository<EmailVerificationOtp, Guid> otps, EmailOtpManager codes, VerificationEmailSender email,
        IUnitOfWorkManager units, IOptions<IdentityOptions> identityOptions,
        IInternationalPhoneNumberService phoneNumbers)
    {
        _users = users; _parents = parents; _otps = otps; _codes = codes;
        _email = email; _units = units; _identityOptions = identityOptions; _phoneNumbers = phoneNumbers;
    }

    [UnitOfWork(IsDisabled = true)]
    public virtual async Task RegisterAsync(RegisterParentDto input)
    {
        var email = input.Email.Trim();
        if (!input.DateOfBirth.HasValue)
        {
            throw new BusinessException(STNGDomainErrorCodes.ParentDateOfBirthRequired);
        }

        var phone = _phoneNumbers.ValidateAndNormalizeMobile(input.PhoneNumber, input.PhoneCountryIso2);
        var userId = GuidGenerator.Create();
        var parent = new Parent(GuidGenerator.Create(), userId, input.DateOfBirth.Value,
            phone.CountryIso2, DateOnly.FromDateTime(Clock.Now.ToUniversalTime()), input.Job);

        string code;
        using (var unit = _units.Begin(requiresNew: true, isTransactional: true))
        {
            await _identityOptions.SetAsync();
            if (await _users.FindByEmailAsync(email) != null)
                throw new BusinessException(STNGDomainErrorCodes.RegistrationUnavailable);
            var user = new IdentityUser(userId, GuidGenerator.Create().ToString("N"), email)
            { Name = input.FullName.Trim() };
            user.SetPhoneNumber(phone.E164, false);
            (await _users.CreateAsync(user, input.Password)).CheckErrors();
            await _parents.InsertAsync(parent);
            var issued = _codes.Create(user.Id, user.Email);
            code = issued.Code;
            await _otps.InsertAsync(issued.Otp);
            await unit.CompleteAsync();
        }
        await _email.SendAsync(email, code);
    }
}
