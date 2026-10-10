using System;
using Shouldly;
using STNG.Parents;
using Xunit;

namespace STNG.Parents;

public class ParentTests
{
    [Fact]
    public void Requires_identity_and_controls_optional_job()
    {
        Should.Throw<ArgumentException>(() => new Parent(Guid.NewGuid(), Guid.Empty));
        var userId = Guid.NewGuid();
        var parent = new Parent(Guid.NewGuid(), userId, " Engineer ");
        parent.UserId.ShouldBe(userId);
        parent.Job.ShouldBe("Engineer");
        parent.ChangeJob(" ");
        parent.Job.ShouldBeNull();
        Should.Throw<ArgumentException>(() => parent.ChangeJob(new string('x', Parent.MaxJobLength + 1)));
    }
}
