using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using STNG.Parents;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Users;

namespace STNG.Authentication;

[Authorize, RemoteService(false)]
public class ParentCurrentUserAppService : STNGAppService, IParentCurrentUserAppService
{
    private readonly IRepository<Parent, Guid> _parents;
    private readonly IdentityUserManager _users;
    private readonly ParentAccessManager _access;
    public ParentCurrentUserAppService(IRepository<Parent, Guid> parents, IdentityUserManager users, ParentAccessManager access)
    {
        _parents = parents; _users = users; _access = access;
    }

    public virtual async Task<ParentCurrentUserDto> GetAsync()
    {
        var user = await _users.GetByIdAsync(CurrentUser.GetId());
        if (!await _access.IsEligibleAsync(user) || await _users.IsLockedOutAsync(user))
            throw new AbpAuthorizationException("Parent account unavailable.");
        var parent = await _parents.GetAsync(x => x.UserId == user.Id);
        return new() { Id = user.Id, ParentId = parent.Id, Email = user.Email, FullName = user.Name ?? "",
            Job = parent.Job, EmailConfirmed = user.EmailConfirmed };
    }
}
