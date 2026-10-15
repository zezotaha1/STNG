using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace STNG.Parents;

public class Parent : FullAuditedAggregateRoot<Guid>
{
    public const int MinimumAge = 18;
    public const int MaxJobLength = 128;
    public const int PhoneCountryIso2Length = 2;

    public Guid UserId { get; private set; }
    public string? Job { get; private set; }
    public DateOnly DateOfBirth { get; private set; }
    public string PhoneCountryIso2 { get; private set; }

    protected Parent()
    {
        PhoneCountryIso2 = null!;
    }

    public Parent(Guid id, Guid userId, DateOnly dateOfBirth, string phoneCountryIso2,
        DateOnly currentUtcDate, string? job = null) : base(id)
    {
        if (userId == Guid.Empty) throw new ArgumentException("A parent must have an identity user.", nameof(userId));

        EnsureEligible(dateOfBirth, currentUtcDate);

        UserId = userId;
        DateOfBirth = dateOfBirth;
        PhoneCountryIso2 = NormalizePhoneCountry(phoneCountryIso2);
        ChangeJob(job);
    }

    public void ChangeJob(string? job)
    {
        Job = Check.Length(job?.Trim(), nameof(job), MaxJobLength);
        if (string.IsNullOrEmpty(Job)) Job = null;
    }

    private static void EnsureEligible(DateOnly dateOfBirth, DateOnly currentUtcDate)
    {
        if (dateOfBirth == default)
        {
            throw new BusinessException(STNGDomainErrorCodes.ParentDateOfBirthRequired);
        }

        if (dateOfBirth > currentUtcDate)
        {
            throw new BusinessException(STNGDomainErrorCodes.ParentInvalidDateOfBirth);
        }

        var eligibilityYear = dateOfBirth.Year + MinimumAge;
        var eligibleOn = dateOfBirth.Month == 2 && dateOfBirth.Day == 29 && !DateTime.IsLeapYear(eligibilityYear)
            ? new DateOnly(eligibilityYear, 3, 1)
            : dateOfBirth.AddYears(MinimumAge);

        if (currentUtcDate < eligibleOn)
        {
            throw new BusinessException(STNGDomainErrorCodes.ParentMinimumAgeNotMet);
        }
    }

    private static string NormalizePhoneCountry(string phoneCountryIso2)
    {
        if (string.IsNullOrWhiteSpace(phoneCountryIso2))
        {
            throw new BusinessException(STNGDomainErrorCodes.ParentPhoneCountryRequired);
        }

        var normalized = phoneCountryIso2.Trim().ToUpperInvariant();
        if (normalized.Length != PhoneCountryIso2Length ||
            normalized[0] is < 'A' or > 'Z' || normalized[1] is < 'A' or > 'Z')
        {
            throw new BusinessException(STNGDomainErrorCodes.ParentInvalidPhoneCountry);
        }

        return normalized;
    }
}
