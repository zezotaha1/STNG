using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using STNG.Parents;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Xunit;

namespace STNG.EntityFrameworkCore.Parents;

[Collection(STNGTestConsts.CollectionDefinitionName)]
public class ParentPersistenceTests : STNGEntityFrameworkCoreTestBase
{
    [Fact]
    public async Task A_user_can_have_only_one_parent_profile()
    {
        var userId = await WithUnitOfWorkAsync(async () =>
        {
            var users = GetRequiredService<IRepository<IdentityUser, Guid>>();
            var parents = GetRequiredService<IRepository<Parent, Guid>>();
            var user = await users.FirstAsync(x => x.UserName == "admin");
            await parents.InsertAsync(new Parent(Guid.NewGuid(), user.Id), autoSave: true);
            return user.Id;
        });
        await Should.ThrowAsync<DbUpdateException>(() => WithUnitOfWorkAsync(async () =>
        {
            await GetRequiredService<IRepository<Parent, Guid>>()
                .InsertAsync(new Parent(Guid.NewGuid(), userId), autoSave: true);
        }));
    }
}
