using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Identity;

namespace STNG.Parents;

public class ParentAccessManager : DomainService
{
    private readonly IRepository<Parent, Guid> _parents;
    public ParentAccessManager(IRepository<Parent, Guid> parents) => _parents = parents;

    public Task<bool> IsEligibleAsync(IdentityUser? user) => IsEligibleAsync(user, CancellationToken.None);

    public async Task<bool> IsEligibleAsync(IdentityUser? user, CancellationToken cancellationToken) =>
        user != null && user.TenantId == null && user.IsActive && user.EmailConfirmed &&
        await _parents.AnyAsync(x => x.UserId == user.Id, cancellationToken);
}
