using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Shouldly;
using STNG.EntityFrameworkCore;
using STNG.Parents;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Validation;
using Xunit;

namespace STNG.Authentication;

[Collection(STNGTestConsts.CollectionDefinitionName)]
public class ParentRegistrationTests : STNGEntityFrameworkCoreTestBase
{
    private RegisterParentDto Input() => new() { Email = $"{Guid.NewGuid():N}@example.com", FullName = "Ahmed Taha", Password = "Strong-Password42!", Job = "Engineer" };

    [Fact]
    public async Task Registers_identity_profile_and_hashed_otp_and_sends_email()
    {
        var input = Input();
        await GetRequiredService<IParentRegistrationAppService>().RegisterAsync(input);
        var message = GetRequiredService<RecordingEmailSender>().Messages.Single(x => x.Email == input.Email);
        var code = Regex.Match(message.Body, @"\b\d{6}\b").Value;
        code.Length.ShouldBe(6);
        await WithUnitOfWorkAsync(async () =>
        {
            var user = await GetRequiredService<IdentityUserManager>().FindByEmailAsync(input.Email);
            user.ShouldNotBeNull();
            user.EmailConfirmed.ShouldBeFalse();
            user.Name.ShouldBe(input.FullName);
            var parent = await GetRequiredService<IRepository<Parent, Guid>>().SingleAsync(x => x.UserId == user.Id);
            parent.Job.ShouldBe(input.Job);
            var otp = await GetRequiredService<IRepository<EmailVerificationOtp, Guid>>().SingleAsync(x => x.UserId == user.Id);
            otp.CodeHash.ShouldNotBe(code);
            otp.CodeHash.Length.ShouldBe(64);
            GetRequiredService<EmailOtpManager>().Matches(otp, code).ShouldBeTrue();
        });
    }

    [Fact]
    public async Task Rejects_duplicate_email()
    {
        var input = Input();
        var service = GetRequiredService<IParentRegistrationAppService>();
        await service.RegisterAsync(input);
        input.Email = input.Email.ToUpperInvariant();
        await Should.ThrowAsync<BusinessException>(() => service.RegisterAsync(input));
    }

    [Theory]
    [InlineData("invalid", "Name", "strong42!")]
    [InlineData("valid@example.com", " ", "strong42!")]
    [InlineData("valid@example.com", "Name", "")]
    public async Task Validates_through_application_pipeline(string email, string name, string password)
    {
        await Should.ThrowAsync<AbpValidationException>(() => GetRequiredService<IParentRegistrationAppService>()
            .RegisterAsync(new RegisterParentDto { Email = email, FullName = name, Password = password }));
    }

    [Fact]
    public async Task Identity_rejects_weak_password_without_creating_profile()
    {
        var input = Input();
        input.Password = "x";
        await Should.ThrowAsync<AbpIdentityResultException>(() => GetRequiredService<IParentRegistrationAppService>().RegisterAsync(input));
        await WithUnitOfWorkAsync(async () => (await GetRequiredService<IdentityUserManager>().FindByEmailAsync(input.Email)).ShouldBeNull());
    }

    [Fact]
    public async Task Rolls_back_identity_and_profile_when_otp_creation_fails()
    {
        var input = Input();
        GetRequiredService<IOptions<EmailOtpOptions>>().Value.HashKey = "";
        await Should.ThrowAsync<InvalidOperationException>(() => GetRequiredService<IParentRegistrationAppService>().RegisterAsync(input));
        await WithUnitOfWorkAsync(async () => (await GetRequiredService<IdentityUserManager>().FindByEmailAsync(input.Email)).ShouldBeNull());
    }

    [Fact]
    public async Task Email_delivery_failure_does_not_rollback_registration()
    {
        var input = Input();
        GetRequiredService<RecordingEmailSender>().FailDelivery = true;
        await GetRequiredService<IParentRegistrationAppService>().RegisterAsync(input);
        await WithUnitOfWorkAsync(async () => (await GetRequiredService<IdentityUserManager>().FindByEmailAsync(input.Email)).ShouldNotBeNull());
    }
}
