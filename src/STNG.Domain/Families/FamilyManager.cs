using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using STNG.Parents;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Identity;

namespace STNG.Families;

public class FamilyManager : DomainService
{
    private readonly IRepository<Parent, Guid> _parents;
    private readonly IRepository<IdentityUser, Guid> _users;
    private readonly ParentAccessManager _parentAccess;
    private readonly IdentityUserManager _userManager;

    public FamilyManager(IRepository<Parent, Guid> parents, IRepository<IdentityUser, Guid> users,
        ParentAccessManager parentAccess, IdentityUserManager userManager)
    {
        _parents = parents;
        _users = users;
        _parentAccess = parentAccess;
        _userManager = userManager;
    }

    public async Task<Parent> GetParentByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var parent = await _parents.FindAsync(x => x.UserId == userId, cancellationToken: cancellationToken);
        if (parent == null) throw new BusinessException(FamilyErrorCodes.ParentNotEligible);
        await EnsureEligibleAsync(parent.Id, cancellationToken);
        return parent;
    }

    public async Task EnsureEligibleAsync(Guid parentId, CancellationToken cancellationToken = default)
    {
        var parent = await _parents.FindAsync(parentId, cancellationToken: cancellationToken);
        // Identity may already be tracked by authentication or an earlier eligibility check.
        // Committed account revocations must take precedence over that older snapshot.
        using var tracking = _users.DisableTracking();
        var user = parent == null ? null : await _users.FindAsync(parent.UserId, cancellationToken: cancellationToken);
        if (!await _parentAccess.IsEligibleAsync(user, cancellationToken) || await _userManager.IsLockedOutAsync(user!))
            throw new BusinessException(FamilyErrorCodes.ParentNotEligible);
    }

    public async Task<Family> CreateAsync(Guid parentId, FamilyName name, Guid requestId,
        CancellationToken cancellationToken = default)
    {
        await EnsureEligibleAsync(parentId, cancellationToken);
        return new Family(GuidGenerator.Create(), name, parentId, GuidGenerator.Create(), requestId, Clock.Now);
    }

    // Used by approval workflows; deliberately not exposed as an unrestricted add-member API.
    public async Task AddApprovedMemberAsync(Family family, Guid actorParentId, Guid parentId,
        CancellationToken cancellationToken = default)
    {
        await EnsureEligibleAsync(actorParentId, cancellationToken);
        family.RequireAdministrator(actorParentId);
        await EnsureEligibleAsync(parentId, cancellationToken);
        var existing = family.Members.SingleOrDefault(x => x.ParentId == parentId);
        if (existing == null) family.AddMember(actorParentId, GuidGenerator.Create(), parentId, Clock.Now);
        else family.ReactivateMember(actorParentId, parentId, Clock.Now);
    }
}
