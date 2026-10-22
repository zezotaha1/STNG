using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Shouldly;
using STNG.EntityFrameworkCore;
using STNG.Permissions;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.Linq;
using Xunit;

namespace STNG.FileManagement;

[Collection(STNGTestConsts.CollectionDefinitionName)]
public class FileManagementAppServiceTests : STNGEntityFrameworkCoreTestBase
{
    private static readonly Guid OwnerId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");
    private readonly IFileManagementAppService _service;
    private readonly TestPermissionChecker _permissions;
    private readonly TestFileAccessPolicy _policy;
    private readonly RecordingFileStorage _storage;
    private readonly FileManagementOptions _options;

    public FileManagementAppServiceTests()
    {
        _service = GetRequiredService<IFileManagementAppService>();
        _permissions = GetRequiredService<TestPermissionChecker>();
        _policy = GetRequiredService<TestFileAccessPolicy>();
        _storage = GetRequiredService<RecordingFileStorage>();
        _options = GetRequiredService<IOptions<FileManagementOptions>>().Value;
        _permissions.Reset();
        _policy.Reset();
        _storage.Reset();
        _options.RequireSecurityScan = false;
    }

    [Fact]
    public async Task Public_ready_file_can_be_read_anonymously()
    {
        _permissions.Grant(STNGPermissions.Files.Publish);
        var asset = await UploadPdfAsync();
        await _service.ChangeVisibilityAsync(asset.Id, new ChangeFileVisibilityInput
        {
            Visibility = FileVisibility.Public,
            IsDiscoverable = true
        });

        using (ChangeUser(null))
        {
            var metadata = await _service.GetAsync(asset.Id);
            metadata.Id.ShouldBe(asset.Id);
            var download = await _service.DownloadAsync(asset.Id);
            await using var stream = download.GetStream();
            stream.Length.ShouldBeGreaterThan(0);
        }
    }

    [Fact]
    public async Task Public_file_with_discovery_disabled_is_not_listed()
    {
        _permissions.Grant(STNGPermissions.Files.Publish);
        var asset = await UploadPdfAsync();
        await _service.ChangeVisibilityAsync(asset.Id, new ChangeFileVisibilityInput
        {
            Visibility = FileVisibility.Public,
            IsDiscoverable = false
        });

        using (ChangeUser(null))
        {
            var result = await _service.GetListAsync(new GetFileListInput { MaxResultCount = 20 });
            result.Items.ShouldNotContain(x => x.Id == asset.Id);
        }
    }

    [Fact]
    public async Task Restricted_access_is_controlled_by_resource_policy()
    {
        _policy.Grant(FileAccessOperation.Attach, FileAccessOperation.ChangeVisibility, FileAccessOperation.Read);
        var asset = await CreateRestrictedAssetAsync();

        using (ChangeUser(Guid.NewGuid()))
            (await _service.GetAsync(asset.Id)).Id.ShouldBe(asset.Id);

        _policy.Reset();
        using (ChangeUser(Guid.NewGuid()))
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(asset.Id));
    }

    [Fact]
    public async Task Missing_resource_policy_denies_attachment()
    {
        var asset = await UploadPdfAsync();
        var exception = await Should.ThrowAsync<AbpAuthorizationException>(() => _service.AttachAsync(asset.Id, new FileReferenceInput
        {
            ResourceType = "unknown-resource",
            ResourceId = "42",
            Purpose = "attachment"
        }));
        exception.Message.ShouldContain(FileManagementErrorCodes.ResourcePolicyMissing);
    }

    [Fact]
    public async Task Owner_only_file_allows_owner_and_denies_other_users()
    {
        var asset = await UploadPdfAsync();
        (await _service.GetAsync(asset.Id)).Id.ShouldBe(asset.Id);

        using (ChangeUser(Guid.NewGuid()))
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(asset.Id));
    }

    [Fact]
    public async Task Other_users_cannot_modify_or_publish_owner_only_files()
    {
        var asset = await UploadPdfAsync();

        using (ChangeUser(Guid.NewGuid()))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.ReplaceAsync(asset.Id,
                new ReplaceFileContentInput { Content = Pdf("other.pdf") }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.DeleteAsync(asset.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.ChangeVisibilityAsync(asset.Id,
                new ChangeFileVisibilityInput { Visibility = FileVisibility.Public, IsDiscoverable = true }));
        }
    }

    [Fact]
    public async Task Owner_cannot_make_a_file_public_without_publish_permission()
    {
        var asset = await UploadPdfAsync();
        var exception = await Should.ThrowAsync<AbpAuthorizationException>(() => _service.ChangeVisibilityAsync(asset.Id,
            new ChangeFileVisibilityInput { Visibility = FileVisibility.Public, IsDiscoverable = true }));
        exception.Message.ShouldContain(FileManagementErrorCodes.PublicVisibilityRequiresPermission);
    }

    [Fact]
    public async Task Tenant_context_is_enforced_even_when_an_id_is_known()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        FileAssetDto asset;
        using (GetRequiredService<ICurrentTenant>().Change(tenantA))
            asset = await UploadPdfAsync();

        using (GetRequiredService<ICurrentTenant>().Change(tenantB))
        {
            var exception = await Record.ExceptionAsync(() => _service.GetAsync(asset.Id));
            exception.ShouldNotBeNull();
            (exception is EntityNotFoundException or AbpAuthorizationException).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Unauthorized_discovery_does_not_leak_owner_only_metadata()
    {
        var asset = await UploadPdfAsync();
        await _service.ChangeVisibilityAsync(asset.Id, new ChangeFileVisibilityInput
        {
            Visibility = FileVisibility.OwnerOnly,
            IsDiscoverable = true
        });

        using (ChangeUser(Guid.NewGuid()))
        {
            var result = await _service.GetListAsync(new GetFileListInput { Filter = asset.OriginalFileName, MaxResultCount = 20 });
            result.Items.ShouldNotContain(x => x.Id == asset.Id);
        }
    }

    [Theory]
    [InlineData("unsafe.exe", "application/pdf", 20)]
    [InlineData("file.pdf", "text/plain", 20)]
    [InlineData("file.pdf", "application/pdf", 26214401)]
    public async Task Invalid_upload_metadata_is_rejected(string fileName, string contentType, long declaredLength)
    {
        var input = new UploadFileInput
        {
            Content = new RemoteStreamContent(new MemoryStream(ValidPdfBytes()), fileName, contentType, declaredLength, true)
        };
        await Should.ThrowAsync<BusinessException>(() => _service.UploadAsync(input));
        _storage.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Required_scanning_without_a_scanner_rejects_the_upload()
    {
        _options.RequireSecurityScan = true;
        var exception = await Should.ThrowAsync<BusinessException>(() => UploadPdfAsync());
        exception.Code.ShouldBe(FileManagementErrorCodes.ScannerRequired);
        _storage.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Storage_failure_is_recorded_and_leaves_no_object()
    {
        _storage.FailNextSave = true;
        await Should.ThrowAsync<IOException>(() => UploadPdfAsync());
        _storage.Count.ShouldBe(0);

        await WithUnitOfWorkAsync(async () =>
        {
            var failed = (await GetRequiredService<IRepository<FileAsset, Guid>>().GetListAsync())
                .OrderByDescending(x => x.CreationTime).First();
            failed.Status.ShouldBe(FileAssetStatus.Failed);
        });
    }

    [Fact]
    public async Task Replacement_preserves_references_and_removes_old_storage_object()
    {
        _policy.Grant(FileAccessOperation.Attach);
        var asset = await UploadPdfAsync();
        await _service.AttachAsync(asset.Id, Reference());
        var oldKey = _storage.Keys.Single();

        var replaced = await _service.ReplaceAsync(asset.Id,
            new ReplaceFileContentInput { Content = Pdf("replacement.pdf", "%PDF-1.7 replacement") });

        replaced.ContentHash.ShouldNotBe(asset.ContentHash);
        _storage.Count.ShouldBe(1);
        _storage.Keys.ShouldNotContain(oldKey);
        await WithUnitOfWorkAsync(async () =>
        {
            var repository = GetRequiredService<IRepository<FileAsset, Guid>>();
            var query = await repository.WithDetailsAsync(x => x.References);
            var persisted = await GetRequiredService<IAsyncQueryableExecuter>().FirstAsync(query.Where(x => x.Id == asset.Id));
            persisted.References.Count.ShouldBe(1);
        });
    }

    [Fact]
    public async Task Deletion_is_blocked_while_references_exist()
    {
        _policy.Grant(FileAccessOperation.Attach);
        var asset = await UploadPdfAsync();
        await _service.AttachAsync(asset.Id, Reference());

        var exception = await Should.ThrowAsync<BusinessException>(() => _service.DeleteAsync(asset.Id));
        exception.Code.ShouldBe(FileManagementErrorCodes.FileStillReferenced);
        _storage.Count.ShouldBe(1);
    }

    private async Task<FileAssetDto> CreateRestrictedAssetAsync()
    {
        var asset = await UploadPdfAsync();
        await _service.AttachAsync(asset.Id, Reference());
        return await _service.ChangeVisibilityAsync(asset.Id, new ChangeFileVisibilityInput
        {
            Visibility = FileVisibility.Restricted,
            IsDiscoverable = true
        });
    }

    private Task<FileAssetDto> UploadPdfAsync() =>
        _service.UploadAsync(new UploadFileInput { Content = Pdf("document.pdf") });

    private static RemoteStreamContent Pdf(string fileName, string text = "%PDF-1.7 test document")
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        return new RemoteStreamContent(new MemoryStream(bytes), fileName, "application/pdf", bytes.Length, true);
    }

    private static byte[] ValidPdfBytes() => Encoding.ASCII.GetBytes("%PDF-1.7 test document");

    private static FileReferenceInput Reference() => new()
    {
        ResourceType = "test-resource",
        ResourceId = "42",
        Purpose = "attachment"
    };

    private IDisposable ChangeUser(Guid? userId)
    {
        var identity = userId.HasValue
            ? new ClaimsIdentity(new[]
            {
                new Claim(AbpClaimTypes.UserId, userId.Value.ToString()),
                new Claim(AbpClaimTypes.UserName, "file-test-user")
            }, "Test")
            : new ClaimsIdentity();
        return GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(identity));
    }
}
