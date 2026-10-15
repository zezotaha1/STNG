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
    private RegisterParentDto Input() => new()
    {
        Email = $"{Guid.NewGuid():N}@example.com",
        FullName = "Ahmed Taha",
        Password = "Strong-Password42!",
        Job = "Engineer",
        DateOfBirth = new DateOnly(1990, 6, 15),
        PhoneCountryIso2 = "eg",
        PhoneNumber = " 01012345678 "
    };

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
            user.PhoneNumber.ShouldBe("+201012345678");
            user.PhoneNumberConfirmed.ShouldBeFalse();
            user.Name.ShouldBe(input.FullName);
            var parent = await GetRequiredService<IRepository<Parent, Guid>>().SingleAsync(x => x.UserId == user.Id);
            parent.Job.ShouldBe(input.Job);
            parent.DateOfBirth.ShouldBe(input.DateOfBirth.GetValueOrDefault());
            parent.PhoneCountryIso2.ShouldBe("EG");
            var otp = await GetRequiredService<IRepository<EmailVerificationOtp, Guid>>().SingleAsync(x => x.UserId == user.Id);
            otp.CodeHash.ShouldNotBe(code);
            otp.CodeHash.Length.ShouldBe(64);
            GetRequiredService<EmailOtpManager>().Matches(otp, code, user.Email).ShouldBeTrue();
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
        var input = Input();
        input.Email = email;
        input.FullName = name;
        input.Password = password;
        await Should.ThrowAsync<AbpValidationException>(() =>
            GetRequiredService<IParentRegistrationAppService>().RegisterAsync(input));
    }

    [Fact]
    public async Task Requires_date_of_birth_phone_and_country_through_application_pipeline()
    {
        var input = Input();
        input.DateOfBirth = null;
        input.PhoneNumber = "";
        input.PhoneCountryIso2 = "";

        var exception = await Should.ThrowAsync<AbpValidationException>(() =>
            GetRequiredService<IParentRegistrationAppService>().RegisterAsync(input));

        var members = exception.ValidationErrors.SelectMany(x => x.MemberNames).ToList();
        members.ShouldContain(nameof(RegisterParentDto.DateOfBirth));
        members.ShouldContain(nameof(RegisterParentDto.PhoneNumber));
        members.ShouldContain(nameof(RegisterParentDto.PhoneCountryIso2));
    }

    [Fact]
    public async Task Underage_registration_creates_no_records_and_sends_no_email()
    {
        GetRequiredService<TestClock>().Set(new DateTime(2026, 10, 14, 23, 59, 59, DateTimeKind.Utc));
        var input = Input();
        input.DateOfBirth = new DateOnly(2008, 10, 15);
        var messageCount = GetRequiredService<RecordingEmailSender>().Messages.Count;

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            GetRequiredService<IParentRegistrationAppService>().RegisterAsync(input));

        exception.Code.ShouldBe(STNGDomainErrorCodes.ParentMinimumAgeNotMet);
        GetRequiredService<RecordingEmailSender>().Messages.Count.ShouldBe(messageCount);
        await WithUnitOfWorkAsync(async () =>
        {
            (await GetRequiredService<IdentityUserManager>().FindByEmailAsync(input.Email)).ShouldBeNull();
            (await GetRequiredService<IRepository<Parent, Guid>>().AnyAsync(x => x.DateOfBirth == input.DateOfBirth)).ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Registration_uses_the_current_utc_calendar_date_for_the_age_boundary()
    {
        GetRequiredService<TestClock>().Set(new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc));
        var input = Input();
        input.DateOfBirth = new DateOnly(2008, 10, 14);

        await GetRequiredService<IParentRegistrationAppService>().RegisterAsync(input);

        await WithUnitOfWorkAsync(async () =>
            (await GetRequiredService<IdentityUserManager>().FindByEmailAsync(input.Email)).ShouldNotBeNull());
    }

    [Fact]
    public async Task Invalid_mobile_registration_creates_no_records_and_sends_no_email()
    {
        var input = Input();
        input.PhoneNumber = "123";
        var messageCount = GetRequiredService<RecordingEmailSender>().Messages.Count;

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            GetRequiredService<IParentRegistrationAppService>().RegisterAsync(input));

        exception.Code.ShouldBe(STNGDomainErrorCodes.ParentInvalidMobileNumber);
        GetRequiredService<RecordingEmailSender>().Messages.Count.ShouldBe(messageCount);
        await WithUnitOfWorkAsync(async () =>
            (await GetRequiredService<IdentityUserManager>().FindByEmailAsync(input.Email)).ShouldBeNull());
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
