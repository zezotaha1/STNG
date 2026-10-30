using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Domain.Services;

namespace STNG.Families;

public class OwnershipTransferOptions
{
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(FamilyConsts.DefaultTransferLifetimeHours);
}

public class OwnershipTransferManager : DomainService
{
    private readonly FamilyManager _families;
    private readonly IOptions<OwnershipTransferOptions> _options;

    public OwnershipTransferManager(FamilyManager families, IOptions<OwnershipTransferOptions> options)
    {
        _families = families;
        _options = options;
    }

    public async Task<OwnershipTransfer> InitiateAsync(Family family, Guid actorParentId, Guid transferId,
        Guid recipientParentId, CancellationToken cancellationToken = default)
    {
        await _families.EnsureEligibleAsync(actorParentId, cancellationToken);
        family.RequireOwner(actorParentId);
        await _families.EnsureEligibleAsync(recipientParentId, cancellationToken);
        if (_options.Value.Lifetime <= TimeSpan.Zero) throw new InvalidOperationException("Ownership transfer lifetime must be positive.");
        return family.InitiateOwnershipTransfer(actorParentId, transferId, recipientParentId, Clock.Now,
            Clock.Now.Add(_options.Value.Lifetime));
    }

    public async Task AcceptAsync(Family family, Guid actorParentId, Guid transferId,
        CancellationToken cancellationToken = default)
    {
        var transfer = family.OwnershipTransfers.SingleOrDefault(x => x.Id == transferId)
            ?? throw new BusinessException(FamilyErrorCodes.TransferNotPending);
        if (transfer.ToParentId != actorParentId) throw new BusinessException(FamilyErrorCodes.InsufficientRole);
        await _families.EnsureEligibleAsync(actorParentId, cancellationToken);
        if (transfer.Status != OwnershipTransferStatus.Accepted)
            await _families.EnsureEligibleAsync(transfer.FromParentId, cancellationToken);
        family.CompleteOwnershipTransfer(actorParentId, transferId, Clock.Now);
    }
}
