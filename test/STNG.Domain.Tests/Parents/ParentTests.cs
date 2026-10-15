using System;
using Shouldly;
using STNG.Parents;
using Volo.Abp;
using Xunit;

namespace STNG.Parents;

public class ParentTests
{
    private static readonly DateOnly Today = new(2026, 10, 14);

    private static Parent Create(DateOnly dateOfBirth, string country = "EG", string? job = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), dateOfBirth, country, Today, job);

    [Fact]
    public void Requires_identity_and_controls_profile_values()
    {
        Should.Throw<ArgumentException>(() =>
            new Parent(Guid.NewGuid(), Guid.Empty, new DateOnly(1990, 1, 1), "EG", Today));
        var userId = Guid.NewGuid();
        var parent = new Parent(Guid.NewGuid(), userId, new DateOnly(1990, 1, 1), " eg ", Today, " Engineer ");
        parent.UserId.ShouldBe(userId);
        parent.DateOfBirth.ShouldBe(new DateOnly(1990, 1, 1));
        parent.PhoneCountryIso2.ShouldBe("EG");
        parent.Job.ShouldBe("Engineer");
        parent.ChangeJob(" ");
        parent.Job.ShouldBeNull();
        Should.Throw<ArgumentException>(() => parent.ChangeJob(new string('x', Parent.MaxJobLength + 1)));
    }

    [Fact]
    public void Accepts_parent_on_eighteenth_birthday() =>
        Create(new DateOnly(2008, 10, 14)).DateOfBirth.ShouldBe(new DateOnly(2008, 10, 14));

    [Fact]
    public void Accepts_parent_older_than_eighteen() =>
        Create(new DateOnly(1980, 5, 20)).DateOfBirth.ShouldBe(new DateOnly(1980, 5, 20));

    [Fact]
    public void Rejects_parent_one_day_before_eighteenth_birthday()
    {
        var exception = Should.Throw<BusinessException>(() => Create(new DateOnly(2008, 10, 15)));
        exception.Code.ShouldBe(STNGDomainErrorCodes.ParentMinimumAgeNotMet);
    }

    [Fact]
    public void Rejects_future_date_of_birth()
    {
        var exception = Should.Throw<BusinessException>(() => Create(Today.AddDays(1)));
        exception.Code.ShouldBe(STNGDomainErrorCodes.ParentInvalidDateOfBirth);
    }

    [Fact]
    public void Rejects_missing_date_of_birth_at_the_domain_boundary()
    {
        var exception = Should.Throw<BusinessException>(() => Create(default));
        exception.Code.ShouldBe(STNGDomainErrorCodes.ParentDateOfBirthRequired);
    }

    [Fact]
    public void February_29_parent_becomes_eligible_on_March_1_in_a_non_leap_year()
    {
        var birthday = new DateOnly(2004, 2, 29);
        Should.Throw<BusinessException>(() =>
            new Parent(Guid.NewGuid(), Guid.NewGuid(), birthday, "EG", new DateOnly(2022, 2, 28)))
            .Code.ShouldBe(STNGDomainErrorCodes.ParentMinimumAgeNotMet);
        new Parent(Guid.NewGuid(), Guid.NewGuid(), birthday, "EG", new DateOnly(2022, 3, 1))
            .DateOfBirth.ShouldBe(birthday);
    }

    [Theory]
    [InlineData("")]
    [InlineData("E")]
    [InlineData("EGY")]
    [InlineData("1G")]
    public void Rejects_missing_or_malformed_phone_country(string country)
    {
        var exception = Should.Throw<BusinessException>(() => Create(new DateOnly(1990, 1, 1), country));
        exception.Code.ShouldBe(string.IsNullOrWhiteSpace(country)
            ? STNGDomainErrorCodes.ParentPhoneCountryRequired
            : STNGDomainErrorCodes.ParentInvalidPhoneCountry);
    }

    [Fact]
    public void Rejects_null_phone_country_at_the_domain_boundary()
    {
        var exception = Should.Throw<BusinessException>(() => Create(new DateOnly(1990, 1, 1), null!));
        exception.Code.ShouldBe(STNGDomainErrorCodes.ParentPhoneCountryRequired);
    }
}
