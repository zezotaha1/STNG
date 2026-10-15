using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using STNG.Authentication;
using STNG.Parents;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;
using Xunit;

namespace STNG.EntityFrameworkCore.Parents;

[Collection(STNGTestConsts.CollectionDefinitionName)]
public class ParentPersistenceTests : STNGEntityFrameworkCoreTestBase
{
    private static readonly Guid TestUserId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");

    private static Parent NewParent(Guid userId) => new(Guid.NewGuid(), userId,
        new DateOnly(1990, 1, 1), "EG", new DateOnly(2026, 10, 14));

    [Fact]
    public async Task A_user_can_have_only_one_parent_profile()
    {
        var userId = await WithUnitOfWorkAsync(async () =>
        {
            var users = GetRequiredService<IRepository<IdentityUser, Guid>>();
            var parents = GetRequiredService<IRepository<Parent, Guid>>();
            var user = await users.FirstAsync(x => x.UserName == "admin");
            await parents.InsertAsync(NewParent(user.Id), autoSave: true);
            return user.Id;
        });
        await Should.ThrowAsync<DbUpdateException>(() => WithUnitOfWorkAsync(async () =>
        {
            await GetRequiredService<IRepository<Parent, Guid>>()
                .InsertAsync(NewParent(userId), autoSave: true);
        }));
    }

    [Fact]
    public async Task Model_configures_registration_fields_and_auditing()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var db = await GetRequiredService<IDbContextProvider<STNGDbContext>>().GetDbContextAsync();
            var parent = db.Model.FindEntityType(typeof(Parent));
            parent.ShouldNotBeNull();

            var dateOfBirth = parent.FindProperty(nameof(Parent.DateOfBirth));
            dateOfBirth.ShouldNotBeNull();
            dateOfBirth.IsNullable.ShouldBeFalse();
            dateOfBirth.GetColumnType().ShouldBe("date");

            var phoneCountry = parent.FindProperty(nameof(Parent.PhoneCountryIso2));
            phoneCountry.ShouldNotBeNull();
            phoneCountry.IsNullable.ShouldBeFalse();
            phoneCountry.GetMaxLength().ShouldBe(Parent.PhoneCountryIso2Length);

            var creationTime = parent.FindProperty(nameof(Parent.CreationTime));
            creationTime.ShouldNotBeNull();
            creationTime.IsNullable.ShouldBeFalse();
            parent.FindProperty(nameof(Parent.CreatorId)).ShouldNotBeNull();
            parent.FindProperty(nameof(Parent.LastModificationTime)).ShouldNotBeNull();
            parent.FindProperty(nameof(Parent.LastModifierId)).ShouldNotBeNull();
            var isDeleted = parent.FindProperty(nameof(Parent.IsDeleted));
            isDeleted.ShouldNotBeNull();
            isDeleted.IsNullable.ShouldBeFalse();
            isDeleted.ClrType.ShouldBe(typeof(bool));
            isDeleted.GetDefaultValue().ShouldBe(false);
            parent.FindProperty(nameof(Parent.DeletionTime)).ShouldNotBeNull();
            parent.FindProperty(nameof(Parent.DeleterId)).ShouldNotBeNull();

            var otp = db.Model.FindEntityType(typeof(EmailVerificationOtp));
            otp.ShouldNotBeNull();
            otp.FindProperty(nameof(EmailVerificationOtp.CreationTime)).ShouldNotBeNull();
            otp.FindProperty(nameof(EmailVerificationOtp.CreatorId)).ShouldNotBeNull();
            otp.FindProperty(nameof(EmailVerificationOtp.LastModificationTime)).ShouldNotBeNull();
            otp.FindProperty(nameof(EmailVerificationOtp.LastModifierId)).ShouldNotBeNull();
            otp.FindProperty(nameof(ISoftDelete.IsDeleted)).ShouldBeNull();
        });
    }

    [Fact]
    public async Task Parent_audit_fields_are_populated_and_soft_delete_preserves_the_identity()
    {
        var (parentId, userId) = await WithUnitOfWorkAsync(async () =>
        {
            var users = GetRequiredService<IRepository<IdentityUser, Guid>>();
            var parents = GetRequiredService<IRepository<Parent, Guid>>();
            var name = $"audit-{Guid.NewGuid():N}";
            var user = new IdentityUser(Guid.NewGuid(), name, $"{name}@example.com");
            await users.InsertAsync(user, autoSave: true);

            var parent = NewParent(user.Id);
            await parents.InsertAsync(parent, autoSave: true);
            parent.CreationTime.ShouldNotBe(default);
            parent.CreatorId.ShouldBe(TestUserId);
            parent.LastModificationTime.ShouldBeNull();

            parent.ChangeJob("Updated job");
            await parents.UpdateAsync(parent, autoSave: true);
            parent.LastModificationTime.ShouldNotBeNull();
            parent.LastModifierId.ShouldBe(TestUserId);

            await parents.DeleteAsync(parent, autoSave: true);
            return (parent.Id, user.Id);
        });

        await WithUnitOfWorkAsync(async () =>
        {
            (await GetRequiredService<IRepository<Parent, Guid>>().FindAsync(parentId)).ShouldBeNull();
            (await GetRequiredService<IRepository<IdentityUser, Guid>>().FindAsync(userId)).ShouldNotBeNull();
        });

        await WithUnitOfWorkAsync(async () =>
        {
            using (GetRequiredService<IDataFilter<ISoftDelete>>().Disable())
            {
                var deletedParent = await GetRequiredService<IRepository<Parent, Guid>>().GetAsync(parentId);
                deletedParent.IsDeleted.ShouldBeTrue();
                deletedParent.DeletionTime.ShouldNotBeNull();
                deletedParent.DeleterId.ShouldBe(TestUserId);
            }
        });
    }
}
