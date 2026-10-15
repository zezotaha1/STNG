using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Shouldly;
using STNG.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Xunit;

namespace STNG.Authentication;

[Collection(STNGTestConsts.CollectionDefinitionName)]
public class ParentEmailVerificationTests : STNGEntityFrameworkCoreTestBase
{
    private async Task<(string Email, string Code)> RegisterAsync()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        await GetRequiredService<IParentRegistrationAppService>().RegisterAsync(new()
        {
            Email = email,
            FullName = "Parent",
            Password = "Strong-Password42!",
            DateOfBirth = new DateOnly(1990, 1, 1),
            PhoneCountryIso2 = "EG",
            PhoneNumber = "01012345678"
        });
        return (email, LastCode());
    }
    private string LastCode() => Regex.Match(GetRequiredService<RecordingEmailSender>().Messages.Last().Body, @"\b\d{6}\b").Value;
    private Task<EmailVerificationResultDto> Verify(string email, string code) => GetRequiredService<IParentEmailVerificationAppService>().VerifyAsync(new() { Email = email, Otp = code });

    [Fact]
    public async Task Valid_code_confirms_identity_and_cannot_be_reused()
    {
        var (email, code) = await RegisterAsync();
        (await Verify(email, code)).Verified.ShouldBeTrue();
        (await Verify(email, code)).Verified.ShouldBeFalse();
        await WithUnitOfWorkAsync(async () => (await GetRequiredService<IdentityUserManager>().FindByEmailAsync(email))!.EmailConfirmed.ShouldBeTrue());
    }

    [Fact]
    public async Task Failed_attempts_persist_and_exhaust_the_code()
    {
        var (email, code) = await RegisterAsync();
        var wrong = code == "000000" ? "000001" : "000000";
        for (var i = 0; i < 5; i++) (await Verify(email, wrong)).Verified.ShouldBeFalse();
        (await Verify(email, code)).Verified.ShouldBeFalse();
        await WithUnitOfWorkAsync(async () =>
        {
            var user = (await GetRequiredService<IdentityUserManager>().FindByEmailAsync(email))!;
            var otp = await GetRequiredService<IRepository<EmailVerificationOtp, Guid>>()
                .SingleAsync(x => x.UserId == user.Id);
            otp.AttemptCount.ShouldBe(5);
            otp.CreationTime.ShouldNotBe(default);
            otp.LastModificationTime.ShouldNotBeNull();
            user.EmailConfirmed.ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Unknown_email_and_cooldown_resend_are_no_ops()
    {
        var (email, _) = await RegisterAsync();
        var messages = GetRequiredService<RecordingEmailSender>().Messages;
        var count = messages.Count;
        var service = GetRequiredService<IParentEmailVerificationAppService>();
        await service.ResendAsync(new() { Email = email });
        await service.ResendAsync(new() { Email = "unknown@example.com" });
        messages.Count.ShouldBe(count);
        (await Verify("unknown@example.com", "123456")).Verified.ShouldBeFalse();
    }

    [Fact]
    public async Task Resend_invalidates_old_code_and_sends_new_code()
    {
        var (email, oldCode) = await RegisterAsync();
        GetRequiredService<TestClock>().Advance(TimeSpan.FromSeconds(60));
        await GetRequiredService<IParentEmailVerificationAppService>().ResendAsync(new() { Email = email });
        var newCode = LastCode();
        newCode.ShouldNotBe(oldCode);
        await WithUnitOfWorkAsync(async () =>
        {
            var user = (await GetRequiredService<IdentityUserManager>().FindByEmailAsync(email))!;
            var rows = await GetRequiredService<IRepository<EmailVerificationOtp, Guid>>().GetListAsync(x => x.UserId == user.Id);
            rows.Count.ShouldBe(2);
            rows.Count(x => x.InvalidatedAt != null).ShouldBe(1);
        });
        (await Verify(email, oldCode)).Verified.ShouldBeFalse();
        (await Verify(email, newCode)).Verified.ShouldBeTrue();
    }

    [Fact]
    public async Task Expired_code_is_rejected()
    {
        var (email, code) = await RegisterAsync();
        GetRequiredService<TestClock>().Advance(TimeSpan.FromMinutes(10));
        (await Verify(email, code)).Verified.ShouldBeFalse();
    }

    [Fact]
    public async Task Invalidated_code_is_rejected()
    {
        var (email, code) = await RegisterAsync();
        await WithUnitOfWorkAsync(async () =>
        {
            var user = (await GetRequiredService<IdentityUserManager>().FindByEmailAsync(email))!;
            var repo = GetRequiredService<IRepository<EmailVerificationOtp, Guid>>();
            var otp = await repo.SingleAsync(x => x.UserId == user.Id);
            otp.Invalidate(GetRequiredService<TestClock>().Now);
            await repo.UpdateAsync(otp);
        });
        (await Verify(email, code)).Verified.ShouldBeFalse();
    }
}
