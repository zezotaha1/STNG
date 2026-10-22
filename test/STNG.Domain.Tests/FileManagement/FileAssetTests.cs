using System;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace STNG.FileManagement;

public class FileAssetTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly string Hash = new('a', FileManagementConsts.ContentHashLength);

    [Fact]
    public void New_assets_are_private_pending_and_distinguish_owner_from_uploader()
    {
        var uploaderId = Guid.NewGuid();
        var asset = NewPending(uploaderId);

        asset.Visibility.ShouldBe(FileVisibility.OwnerOnly);
        asset.IsDiscoverable.ShouldBeFalse();
        asset.Status.ShouldBe(FileAssetStatus.Pending);
        asset.OwnerUserId.ShouldBe(OwnerId);
        asset.UploadedByUserId.ShouldBe(uploaderId);
    }

    [Fact]
    public void Valid_lifecycle_reaches_ready_and_invalid_transitions_are_rejected()
    {
        var asset = NewPending(OwnerId);
        Should.Throw<BusinessException>(() => asset.MarkReady(Hash)).Code.ShouldBe(FileManagementErrorCodes.InvalidStateTransition);

        asset.BeginValidation();
        asset.BeginProcessing("file.pdf", "application/pdf", ".pdf", 10);
        asset.MarkReady(Hash);

        asset.Status.ShouldBe(FileAssetStatus.Ready);
        asset.ContentHash.ShouldBe(Hash);
        Should.Throw<BusinessException>(() => asset.BeginValidation()).Code.ShouldBe(FileManagementErrorCodes.InvalidStateTransition);
    }

    [Fact]
    public void References_are_unique_and_prevent_deletion()
    {
        var asset = NewReady();
        asset.AttachReference(Guid.NewGuid(), "lesson", "42", "attachment");

        Should.Throw<BusinessException>(() =>
            asset.AttachReference(Guid.NewGuid(), "LESSON", "42", "ATTACHMENT"))
            .Code.ShouldBe(FileManagementErrorCodes.DuplicateReference);
        Should.Throw<BusinessException>(() => asset.MarkDeleted())
            .Code.ShouldBe(FileManagementErrorCodes.FileStillReferenced);
    }

    [Fact]
    public void Restricted_files_cannot_lose_their_last_reference()
    {
        var asset = NewReady();
        var reference = asset.AttachReference(Guid.NewGuid(), "lesson", "42", "attachment");
        asset.ChangeVisibility(FileVisibility.Restricted, true);

        Should.Throw<BusinessException>(() => asset.DetachReference(reference.Id))
            .Code.ShouldBe(FileManagementErrorCodes.RestrictedFileRequiresReference);
    }

    [Fact]
    public void Ready_content_can_be_replaced_without_losing_references()
    {
        var asset = NewReady();
        var reference = asset.AttachReference(Guid.NewGuid(), "lesson", "42", "attachment");

        asset.ReplaceReadyContent("new.pdf", "new-key", "application/pdf", ".pdf", 20, new string('b', 64), OwnerId);

        asset.OriginalFileName.ShouldBe("new.pdf");
        asset.References.ShouldContain(x => x.Id == reference.Id);
        asset.Status.ShouldBe(FileAssetStatus.Ready);
    }

    private static FileAsset NewReady()
    {
        var asset = NewPending(OwnerId);
        asset.BeginValidation();
        asset.BeginProcessing("file.pdf", "application/pdf", ".pdf", 10);
        asset.MarkReady(Hash);
        return asset;
    }

    private static FileAsset NewPending(Guid uploaderId) => new(
        Guid.NewGuid(), null, "file.pdf", Guid.NewGuid().ToString("N"), FileAssetBlobContainer.Name,
        "application/pdf", ".pdf", 10, OwnerId, uploaderId);
}
