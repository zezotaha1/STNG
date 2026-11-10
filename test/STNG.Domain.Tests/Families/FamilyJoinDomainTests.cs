using System;
using System.Linq;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace STNG.Families;

public class FamilyJoinDomainTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
    private readonly FamilyJoinSecrets _secrets = new();
    private static Family Family(Guid owner) => new(Guid.NewGuid(), new FamilyName("Join tests"), owner,
        Guid.NewGuid(), Guid.NewGuid(), Now);
    private static FamilyJoinRequest Request(Guid? parent = null) => new(Guid.NewGuid(), Guid.NewGuid(),
        parent ?? Guid.NewGuid(), Guid.NewGuid(), Now);
    private static void Error(string code, Action action) => Should.Throw<BusinessException>(action).Code.ShouldBe(code);

    private FamilyJoinCredential Credential(FamilyJoinCredentialKind kind = FamilyJoinCredentialKind.ManualCode)
    {
        var secret = _secrets.Generate(kind);
        _secrets.TryHash(kind, secret, out var hash).ShouldBeTrue();
        return new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), kind, hash, Now, Now.AddHours(1));
    }

    [Theory]
    [InlineData(FamilyJoinCredentialKind.ManualCode)]
    [InlineData(FamilyJoinCredentialKind.QRToken)]
    public void Generated_capabilities_verify_without_persisting_their_plaintext(FamilyJoinCredentialKind kind)
    {
        var secret = _secrets.Generate(kind);
        _secrets.TryHash(kind, secret, out var hash).ShouldBeTrue();
        hash.Length.ShouldBe(FamilyJoinConsts.HashLength);
        (hash == secret).ShouldBeFalse();
        var credential = new FamilyJoinCredential(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), kind, hash, Now, Now.AddHours(1));
        _secrets.Matches(credential, kind, secret).ShouldBeTrue();
        _secrets.Matches(credential, kind, "invalid").ShouldBeFalse();
        _secrets.Matches(credential, kind == FamilyJoinCredentialKind.ManualCode ? FamilyJoinCredentialKind.QRToken : FamilyJoinCredentialKind.ManualCode,
            secret).ShouldBeFalse();
    }

    [Fact]
    public void Manual_codes_accept_case_and_separator_normalization()
    {
        const string code = "01234-56789-ABCDE-FGHJK";
        _secrets.TryHash(FamilyJoinCredentialKind.ManualCode, code, out var hash).ShouldBeTrue();
        _secrets.TryHash(FamilyJoinCredentialKind.ManualCode, " 01234 56789 abcde fghjk ", out var normalized).ShouldBeTrue();
        normalized.ShouldBe(hash);
        _secrets.TryHash(FamilyJoinCredentialKind.ManualCode, code + "X", out _).ShouldBeFalse();
        _secrets.TryHash((FamilyJoinCredentialKind)99, code, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-a-code")]
    public void Malformed_secrets_do_not_hash_as_valid_codes(string? input) =>
        _secrets.TryHash(FamilyJoinCredentialKind.ManualCode, input, out _).ShouldBeFalse();

    [Fact]
    public void Expiration_is_exclusive_and_revocation_is_final()
    {
        var credential = Credential();
        credential.CanSubmit(Now).ShouldBeTrue();
        credential.CanSubmit(credential.ExpiresAt).ShouldBeFalse();
        Error(FamilyJoinErrorCodes.InvalidTransition, () => credential.Expire(Now));
        credential.Expire(credential.ExpiresAt);
        credential.Status.ShouldBe(FamilyJoinCredentialStatus.Expired);
        credential.Revoke(Guid.NewGuid(), credential.ExpiresAt);
        credential.Status.ShouldBe(FamilyJoinCredentialStatus.Revoked);
        Error(FamilyJoinErrorCodes.InvalidTransition, () => credential.Expire(credential.ExpiresAt));
    }

    [Fact]
    public void Expired_or_malformed_credential_construction_is_rejected()
    {
        Error(FamilyJoinErrorCodes.InvalidInput, () => new FamilyJoinCredential(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            FamilyJoinCredentialKind.ManualCode, "bad", Now, Now.AddDays(1)));
        Error(FamilyJoinErrorCodes.InvalidInput, () => new FamilyJoinCredential(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            FamilyJoinCredentialKind.ManualCode, new string('A', 64), Now, Now));
    }

    [Fact]
    public void Approval_is_terminal_and_duplicate_approval_does_not_emit_another_decision()
    {
        var request = Request();
        var reviewer = Guid.NewGuid();
        request.Approve(reviewer, Now);
        request.Status.ShouldBe(FamilyJoinRequestStatus.Approved);
        request.ReviewedByParentId.ShouldBe(reviewer);
        var events = request.GetLocalEvents().Count();
        request.Approve(Guid.NewGuid(), Now.AddMinutes(1));
        request.GetLocalEvents().Count().ShouldBe(events);
        request.ReviewedByParentId.ShouldBe(reviewer);
        Error(FamilyJoinErrorCodes.InvalidTransition, () => request.Cancel(request.ParentId, Now));
        Error(FamilyJoinErrorCodes.InvalidTransition, () => request.Reject(reviewer, null, Now));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Requester_cannot_review_their_own_request(bool approve)
    {
        var request = Request();
        Error(FamilyJoinErrorCodes.CannotReviewOwnRequest, () =>
        {
            if (approve) request.Approve(request.ParentId, Now);
            else request.Reject(request.ParentId, null, Now);
        });
        request.Status.ShouldBe(FamilyJoinRequestStatus.Pending);
    }

    [Fact]
    public void Rejection_records_a_bounded_reason_and_cannot_be_reopened()
    {
        var request = Request();
        var reviewer = Guid.NewGuid();
        Error(FamilyJoinErrorCodes.InvalidInput, () => request.Reject(reviewer, new string('x', 257), Now));
        request.Status.ShouldBe(FamilyJoinRequestStatus.Pending);
        request.Reject(reviewer, "  Not approved  ", Now);
        request.RejectionReason.ShouldBe("Not approved");
        request.Reject(reviewer, "Not approved", Now);
        Error(FamilyJoinErrorCodes.InvalidTransition, () => request.Approve(reviewer, Now));
        Error(FamilyJoinErrorCodes.InvalidTransition, () => request.Reject(reviewer, "Different decision", Now));
    }

    [Fact]
    public void Only_the_requester_can_withdraw_and_withdrawal_is_idempotent()
    {
        var request = Request();
        Error(FamilyErrorCodes.NotFound, () => request.Cancel(Guid.NewGuid(), Now));
        request.Cancel(request.ParentId, Now);
        request.Cancel(request.ParentId, Now.AddMinutes(1));
        request.Status.ShouldBe(FamilyJoinRequestStatus.Cancelled);
        request.ReviewedByParentId.ShouldBeNull();
        Error(FamilyJoinErrorCodes.InvalidTransition, () => request.Approve(Guid.NewGuid(), Now));
    }

    [Fact]
    public void A_submission_coordinates_the_root_without_activating_membership()
    {
        var owner = Guid.NewGuid();
        var family = Family(owner);
        var version = family.Version;
        family.RecordJoinRequestSubmission(Guid.NewGuid());
        family.Version.ShouldBeGreaterThan(version);
        family.Members.Count.ShouldBe(1);
        Error(FamilyErrorCodes.AlreadyMember, () => family.RecordJoinRequestSubmission(owner));
        Error(FamilyErrorCodes.MembershipInactive, () => family.RecordJoinCredentialChange(Guid.NewGuid()));
        family.BeginArchiving(owner, Now);
        Error(FamilyErrorCodes.NotActive, () => family.RecordJoinRequestSubmission(Guid.NewGuid()));
        family.RecordJoinRequestCancellation();
        family.Status.ShouldBe(FamilyStatus.Archiving);
    }

    [Fact]
    public void Join_options_require_secure_links_bounded_lifetimes_and_positive_limits()
    {
        var options = new FamilyJoinOptions();
        options.IsValid().ShouldBeTrue();
        options.QRJoinBaseUri = "http://example.com/join";
        options.IsValid().ShouldBeFalse();
        options.QRJoinBaseUri = "https://example.com/join?token=bad";
        options.IsValid().ShouldBeFalse();
        options.QRJoinBaseUri = "https://example.com/join";
        options.ManualCodeLifetime = TimeSpan.FromDays(31);
        options.IsValid().ShouldBeFalse();
        options.ManualCodeLifetime = TimeSpan.FromHours(1);
        options.AttemptsPerParentPerMinute = 0;
        options.IsValid().ShouldBeFalse();
    }
}
