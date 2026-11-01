using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Shouldly;
using STNG.Families;
using Volo.Abp;
using Xunit;

namespace STNG;

public class FamilyJoinInputTests
{
    [Fact]
    public async Task Parent_attempt_limits_are_atomic_and_partitioned_by_server_parent_identity()
    {
        using var limiter = new FamilyJoinAttemptLimiter(Options.Create(new FamilyJoinOptions { AttemptsPerParentPerMinute = 2 }));
        var parent = Guid.NewGuid();
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
        {
            try { await limiter.CheckAsync(parent, CancellationToken.None); return true; }
            catch (BusinessException exception) { exception.Code.ShouldBe(FamilyJoinErrorCodes.AttemptsLimited); return false; }
        }));
        results.Count(x => x).ShouldBe(2);
        await limiter.CheckAsync(Guid.NewGuid(), CancellationToken.None);
    }

    [Fact]
    public void Derived_review_and_page_dtos_do_not_bypass_the_existing_stamp_and_pagination_limits()
    {
        var review = new RejectFamilyJoinRequestValidator();
        review.Validate(new RejectFamilyJoinRequestDto { Reason = "Allowed length" }).IsValid.ShouldBeFalse();
        review.Validate(new RejectFamilyJoinRequestDto { ConcurrencyStamp = "stamp", Reason = new string('x', 257) }).IsValid.ShouldBeFalse();
        review.Validate(new RejectFamilyJoinRequestDto { ConcurrencyStamp = "stamp", Reason = new string('x', 256) }).IsValid.ShouldBeTrue();
        var pages = new FamilyJoinRequestPageValidator();
        pages.Validate(new FamilyJoinRequestPageDto { MaxResultCount = 101 }).IsValid.ShouldBeFalse();
        pages.Validate(new FamilyJoinRequestPageDto { Status = (FamilyJoinRequestStatus)99 }).IsValid.ShouldBeFalse();
        pages.Validate(new FamilyJoinRequestPageDto { Status = null }).IsValid.ShouldBeTrue();
    }
}
