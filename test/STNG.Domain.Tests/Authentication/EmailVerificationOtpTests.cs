using System;
using Shouldly;
using STNG.Authentication;
using Xunit;

namespace STNG.Authentication;

public class EmailVerificationOtpTests
{
    private readonly DateTime _now = new(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc);
    private EmailVerificationOtp Create() => new(Guid.NewGuid(), Guid.NewGuid(), EmailVerificationOtp.ParentRegistration,
        "hash", _now, TimeSpan.FromMinutes(10), 5);

    [Fact]
    public void Code_can_be_used_once()
    {
        var otp = Create();
        otp.TryVerify(true, _now).ShouldBeTrue();
        otp.UsedAt.ShouldBe(_now);
        otp.TryVerify(true, _now).ShouldBeFalse();
    }

    [Fact]
    public void Fifth_failed_attempt_exhausts_code()
    {
        var otp = Create();
        for (var i = 0; i < 5; i++) otp.TryVerify(false, _now).ShouldBeFalse();
        otp.AttemptCount.ShouldBe(5);
        otp.TryVerify(true, _now).ShouldBeFalse();
        otp.AttemptCount.ShouldBe(5);
    }

    [Fact]
    public void Expiration_and_cooldown_boundaries_are_enforced()
    {
        var otp = Create();
        otp.TryVerify(true, _now.AddMinutes(10)).ShouldBeFalse();
        otp.CanResend(_now.AddSeconds(59), TimeSpan.FromSeconds(60)).ShouldBeFalse();
        otp.CanResend(_now.AddSeconds(60), TimeSpan.FromSeconds(60)).ShouldBeTrue();
    }

    [Fact]
    public void Invalidated_code_cannot_be_used()
    {
        var otp = Create();
        otp.Invalidate(_now);
        otp.TryVerify(true, _now).ShouldBeFalse();
        otp.InvalidatedAt.ShouldBe(_now);
    }
}
