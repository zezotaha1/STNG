using System;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities.Auditing;

namespace STNG.Authentication;

[DisableAuditing]
public class EmailVerificationOtp : AuditedAggregateRoot<Guid>
{
    public const string ParentRegistration = "ParentRegistration";
    public Guid UserId { get; private set; }
    public string Purpose { get; private set; } = null!;
    public string CodeHash { get; private set; } = null!;
    public DateTime ExpiresAt { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; }
    public DateTime LastSentAt { get; private set; }
    public DateTime? UsedAt { get; private set; }
    public DateTime? InvalidatedAt { get; private set; }

    protected EmailVerificationOtp() { }

    public EmailVerificationOtp(Guid id, Guid userId, string purpose, string codeHash,
        DateTime now, TimeSpan lifetime, int maxAttempts) : base(id)
    {
        if (userId == Guid.Empty) throw new ArgumentException("Identity user is required.", nameof(userId));
        if (lifetime <= TimeSpan.Zero || maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(lifetime));
        UserId = userId;
        Purpose = Check.NotNullOrWhiteSpace(purpose, nameof(purpose), 64);
        CodeHash = Check.NotNullOrWhiteSpace(codeHash, nameof(codeHash), 128);
        LastSentAt = now;
        ExpiresAt = now.Add(lifetime);
        MaxAttempts = maxAttempts;
    }

    public bool IsActive(DateTime now) => UsedAt == null && InvalidatedAt == null &&
        now < ExpiresAt && AttemptCount < MaxAttempts;

    public bool TryVerify(bool hashMatches, DateTime now)
    {
        if (!IsActive(now)) return false;
        AttemptCount++;
        if (hashMatches) UsedAt = now;
        return hashMatches;
    }

    public bool CanResend(DateTime now, TimeSpan cooldown) => now >= LastSentAt.Add(cooldown);

    public void Invalidate(DateTime now)
    {
        if (UsedAt == null && InvalidatedAt == null) InvalidatedAt = now;
    }
}
