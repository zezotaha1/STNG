using System;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace STNG.Families;

// Complements the HTTP IP limiter after the authenticated Parent has been resolved.
// Like the host's existing limiter this is process-local; scale-out needs a shared policy.
public sealed class FamilyJoinAttemptLimiter : ISingletonDependency, IDisposable
{
    private readonly PartitionedRateLimiter<Guid> _limiter;

    public FamilyJoinAttemptLimiter(IOptions<FamilyJoinOptions> options) =>
        _limiter = PartitionedRateLimiter.Create<Guid, Guid>(parentId => RateLimitPartition.GetFixedWindowLimiter(parentId,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.Value.AttemptsPerParentPerMinute,
                Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
            }));

    public async Task CheckAsync(Guid parentId, CancellationToken cancellationToken)
    {
        using var lease = await _limiter.AcquireAsync(parentId, 1, cancellationToken);
        if (!lease.IsAcquired) throw new BusinessException(FamilyJoinErrorCodes.AttemptsLimited);
    }

    public void Dispose() => _limiter.Dispose();
}
