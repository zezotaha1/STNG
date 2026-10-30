using System;
using System.Linq;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace STNG.Families;

public class FamilyTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
    private static Family Create(Guid owner) => new(Guid.NewGuid(), new FamilyName("  My Family  "), owner,
        Guid.NewGuid(), Guid.NewGuid(), Now);
    private static Guid Add(Family family, Guid owner)
    {
        var id = Guid.NewGuid();
        family.AddMember(owner, Guid.NewGuid(), id, Now);
        return id;
    }
    private static void Error(string code, Action action) => Should.Throw<BusinessException>(action).Code.ShouldBe(code);
    private static void OneOwner(Family family) => family.Members.Count(x =>
        x.Role == FamilyMemberRole.Owner && x.Status == FamilyMemberStatus.Active).ShouldBe(1);

    [Fact]
    public void Creation_requires_an_owner_and_names_have_value_equality()
    {
        var owner = Guid.NewGuid();
        var family = Create(owner);
        family.Name.ShouldBe(new FamilyName("My Family"));
        family.Status.ShouldBe(FamilyStatus.Active);
        family.RequireMember(owner).Role.ShouldBe(FamilyMemberRole.Owner);
        family.MembershipHistory.Single().Change.ShouldBe(FamilyMembershipChange.Joined);
        OneOwner(family);
        Should.Throw<ArgumentException>(() => Create(Guid.Empty));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_missing_names(string? name) => Error(FamilyErrorCodes.InvalidName, () => new FamilyName(name!));

    [Fact]
    public void Trims_before_validating_name_length()
    {
        new FamilyName(" " + new string('x', 120) + " ").Value.Length.ShouldBe(120);
        Error(FamilyErrorCodes.InvalidName, () => new FamilyName(new string('x', 121)));
    }

    [Fact]
    public void Membership_and_roles_are_independent_between_families()
    {
        var owner = Guid.NewGuid();
        var a = Create(owner);
        var b = Create(owner);
        var parent = Guid.NewGuid();
        a.AddMember(owner, Guid.NewGuid(), parent, Now);
        b.AddMember(owner, Guid.NewGuid(), parent, Now);
        a.PromoteToAdmin(owner, parent, Now);
        a.RequireMember(parent).Role.ShouldBe(FamilyMemberRole.Admin);
        b.RequireMember(parent).Role.ShouldBe(FamilyMemberRole.Member);
        a.Leave(parent, Now);
        b.RequireMember(parent).Status.ShouldBe(FamilyMemberStatus.Active);
        OneOwner(a);
        OneOwner(b);
    }

    [Fact]
    public void Owner_cannot_leave_be_removed_or_be_directly_demoted()
    {
        var owner = Guid.NewGuid();
        var f = Create(owner);
        var admin = Add(f, owner);
        f.PromoteToAdmin(owner, admin, Now);
        Error(FamilyErrorCodes.OwnerCannotLeave, () => f.Leave(owner, Now));
        Error(FamilyErrorCodes.CannotRemoveOwner, () => f.RemoveMember(admin, owner, Now));
        Error(FamilyErrorCodes.InvalidRoleTransition, () => f.DemoteAdmin(owner, owner, Now));
        OneOwner(f);
    }

    [Fact]
    public void Only_owner_can_manage_admin_roles_and_remove_admins()
    {
        var owner = Guid.NewGuid();
        var f = Create(owner);
        var admin = Add(f, owner);
        var secondAdmin = Add(f, owner);
        var member = Add(f, owner);
        f.PromoteToAdmin(owner, admin, Now);
        f.PromoteToAdmin(owner, secondAdmin, Now);
        Error(FamilyErrorCodes.InsufficientRole, () => f.PromoteToAdmin(member, member, Now));
        Error(FamilyErrorCodes.InsufficientRole, () => f.PromoteToAdmin(admin, member, Now));
        Error(FamilyErrorCodes.InsufficientRole, () => f.DemoteAdmin(admin, secondAdmin, Now));
        Error(FamilyErrorCodes.InsufficientRole, () => f.RemoveMember(admin, secondAdmin, Now));
        f.RemoveMember(admin, member, Now);
        f.Members.Single(x => x.ParentId == member).RemovedByParentId.ShouldBe(admin);
        f.RemoveMember(owner, secondAdmin, Now);
        OneOwner(f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Former_members_reactivate_the_same_membership_as_members(bool removed)
    {
        var owner = Guid.NewGuid();
        var f = Create(owner);
        var member = Add(f, owner);
        f.PromoteToAdmin(owner, member, Now);
        var oldId = f.RequireMember(member).Id;
        Error(FamilyErrorCodes.AlreadyMember, () => f.AddMember(owner, Guid.NewGuid(), member, Now));
        if (removed) f.RemoveMember(owner, member, Now);
        else f.Leave(member, Now);
        Error(FamilyErrorCodes.AlreadyMember, () => f.AddMember(owner, Guid.NewGuid(), member, Now));
        f.ReactivateMember(owner, member, Now.AddDays(1));
        f.RequireMember(member).Id.ShouldBe(oldId);
        f.RequireMember(member).Role.ShouldBe(FamilyMemberRole.Member);
        f.RequireMember(member).LeftAt.ShouldBeNull();
        f.MembershipHistory.Last().Change.ShouldBe(FamilyMembershipChange.Reactivated);
        f.MembershipHistory.Count.ShouldBe(5);
    }

    [Fact]
    public void Editing_family_requires_a_current_administrative_membership()
    {
        var owner = Guid.NewGuid();
        var f = Create(owner);
        var member = Add(f, owner);
        Error(FamilyErrorCodes.InsufficientRole, () => f.Rename(member, new FamilyName("Other")));
        Error(FamilyErrorCodes.MembershipInactive, () => f.Rename(Guid.NewGuid(), new FamilyName("Other")));
        f.PromoteToAdmin(owner, member, Now);
        f.Rename(member, new FamilyName("Other"));
        f.Name.Value.ShouldBe("Other");
        f.Leave(member, Now);
        Error(FamilyErrorCodes.MembershipInactive, () => f.Rename(member, new FamilyName("Changed")));
    }

    [Fact]
    public void Ownership_requires_recipient_acceptance_and_is_idempotent()
    {
        var owner = Guid.NewGuid();
        var f = Create(owner);
        var recipient = Add(f, owner);
        var transfer = f.InitiateOwnershipTransfer(owner, Guid.NewGuid(), recipient, Now, Now.AddHours(48));
        f.RequireMember(owner).Role.ShouldBe(FamilyMemberRole.Owner);
        Error(FamilyErrorCodes.InsufficientRole, () => f.CompleteOwnershipTransfer(owner, transfer.Id, Now));
        f.CompleteOwnershipTransfer(recipient, transfer.Id, Now.AddHours(1));
        f.RequireMember(owner).Role.ShouldBe(FamilyMemberRole.Admin);
        f.RequireMember(recipient).Role.ShouldBe(FamilyMemberRole.Owner);
        transfer.Status.ShouldBe(OwnershipTransferStatus.Accepted);
        var version = f.Version;
        var history = f.MembershipHistory.Count;
        f.CompleteOwnershipTransfer(recipient, transfer.Id, Now.AddHours(2));
        f.Version.ShouldBe(version);
        f.MembershipHistory.Count.ShouldBe(history);
        f.Leave(owner, Now);
        OneOwner(f);
    }

    [Fact]
    public void Pending_transfer_is_unique_and_cannot_target_a_nonmember_or_self()
    {
        var owner = Guid.NewGuid();
        var f = Create(owner);
        var recipient = Add(f, owner);
        Error(FamilyErrorCodes.InvalidOwnershipRecipient, () => f.InitiateOwnershipTransfer(owner, Guid.NewGuid(), owner, Now, Now.AddDays(1)));
        Error(FamilyErrorCodes.InvalidOwnershipRecipient, () => f.InitiateOwnershipTransfer(owner, Guid.NewGuid(), Guid.NewGuid(), Now, Now.AddDays(1)));
        var transfer = f.InitiateOwnershipTransfer(owner, Guid.NewGuid(), recipient, Now, Now.AddDays(1));
        f.InitiateOwnershipTransfer(owner, transfer.Id, recipient, Now, Now.AddDays(1)).ShouldBeSameAs(transfer);
        Error(FamilyErrorCodes.TransferAlreadyPending, () => f.InitiateOwnershipTransfer(owner, Guid.NewGuid(), recipient, Now, Now.AddDays(1)));
        Error(FamilyErrorCodes.InsufficientRole, () => f.InitiateOwnershipTransfer(recipient, Guid.NewGuid(), owner, Now, Now.AddDays(1)));
    }

    [Fact]
    public void Expiration_is_exclusive_and_expired_pending_transfers_do_not_block_new_transfers()
    {
        var owner = Guid.NewGuid();
        var f = Create(owner);
        var recipient = Add(f, owner);
        var first = f.InitiateOwnershipTransfer(owner, Guid.NewGuid(), recipient, Now, Now.AddDays(1));
        Error(FamilyErrorCodes.TransferExpired, () => f.CompleteOwnershipTransfer(recipient, first.Id, first.ExpiresAt));
        var second = f.InitiateOwnershipTransfer(owner, Guid.NewGuid(), recipient, first.ExpiresAt, Now.AddDays(2));
        first.Status.ShouldBe(OwnershipTransferStatus.Expired);
        second.Status.ShouldBe(OwnershipTransferStatus.Pending);
        OneOwner(f);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Terminal_transfers_cannot_be_accepted(bool decline)
    {
        var owner = Guid.NewGuid();
        var f = Create(owner);
        var recipient = Add(f, owner);
        var t = f.InitiateOwnershipTransfer(owner, Guid.NewGuid(), recipient, Now, Now.AddDays(1));
        if (decline) f.DeclineOwnershipTransfer(recipient, t.Id, Now);
        else f.CancelOwnershipTransfer(owner, t.Id, Now);
        Error(FamilyErrorCodes.TransferNotPending, () => f.CompleteOwnershipTransfer(recipient, t.Id, Now));
        OneOwner(f);
    }

    [Fact]
    public void Leaving_invalidates_transfers_even_if_the_recipient_rejoins()
    {
        var owner = Guid.NewGuid();
        var f = Create(owner);
        var recipient = Add(f, owner);
        var t = f.InitiateOwnershipTransfer(owner, Guid.NewGuid(), recipient, Now, Now.AddDays(1));
        var version = f.Version;
        f.Leave(recipient, Now);
        f.Version.ShouldBeGreaterThan(version);
        f.ReactivateMember(owner, recipient, Now);
        Error(FamilyErrorCodes.TransferNotPending, () => f.CompleteOwnershipTransfer(recipient, t.Id, Now));
    }

    [Fact]
    public void Beginning_archive_stops_membership_and_ownership_changes_without_claiming_device_safety()
    {
        var owner = Guid.NewGuid();
        var f = Create(owner);
        var recipient = Add(f, owner);
        var t = f.InitiateOwnershipTransfer(owner, Guid.NewGuid(), recipient, Now, Now.AddDays(1));
        f.BeginArchiving(owner, Now);
        f.Status.ShouldBe(FamilyStatus.Archiving);
        t.Status.ShouldBe(OwnershipTransferStatus.Cancelled);
        Error(FamilyErrorCodes.NotActive, () => f.AddMember(owner, Guid.NewGuid(), Guid.NewGuid(), Now));
        Error(FamilyErrorCodes.NotActive, () => f.CompleteOwnershipTransfer(recipient, t.Id, Now));
        var version = f.Version;
        f.BeginArchiving(owner, Now);
        f.Version.ShouldBe(version);
    }
}
