using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace STNG.Parents;

public class Parent : CreationAuditedAggregateRoot<Guid>
{
    public const int MaxJobLength = 128;
    public Guid UserId { get; private set; }
    public string? Job { get; private set; }

    protected Parent() { }

    public Parent(Guid id, Guid userId, string? job = null) : base(id)
    {
        if (userId == Guid.Empty) throw new ArgumentException("A parent must have an identity user.", nameof(userId));
        UserId = userId;
        ChangeJob(job);
    }

    public void ChangeJob(string? job)
    {
        Job = Check.Length(job?.Trim(), nameof(job), MaxJobLength);
        if (string.IsNullOrEmpty(Job)) Job = null;
    }
}
