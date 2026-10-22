# File management core

The file-management core is a business-module-neutral subsystem. It stores file metadata and resource references in SQL Server and file bytes through ABP BLOB Storing. The HTTP API is rooted at `/api/files`; no Family, Parent, Child, Lesson, or other business-specific routes are defined.

## Domain and lifecycle

`FileAsset` is a tenant-aware, full-audited aggregate root with optimistic concurrency and soft deletion. It owns audited `FileReference` child entities. The explicit owner and uploader are recorded independently. Internal storage keys and physical paths are never included in application DTOs.

New assets are `OwnerOnly`, non-discoverable, and move through:

`Pending -> Validating -> Processing -> Ready`

Validation failures become `Rejected`; infrastructure failures and expired incomplete work become `Failed`; authorized deletion moves the aggregate to `Deleted` before ABP soft deletion. Only `Ready` assets can be read, listed, attached, or changed. A file with references cannot be deleted, and a restricted file cannot lose its final reference.

Replacement stages and validates a new object, stores it under a new key, atomically swaps metadata while retaining references, and then removes the prior object. If the metadata transaction fails, the new object is removed. If a post-commit storage cleanup fails, the inaccessible object is logged for maintenance rather than exposing inconsistent metadata.

## Authorization extension point

Every business module that associates files with one of its resources must implement `IFileAccessPolicy` and register it in dependency injection. `ResourceType` is the stable discriminator used to select the policy. The policy receives the tenant, current user, resource identifier, purpose, file identifier, and requested operation. It must both validate that the resource exists and decide whether the caller may perform the operation.

```csharp
public sealed class ExampleDocumentFilePolicy : IFileAccessPolicy, ITransientDependency
{
    public string ResourceType => "example-document";

    public async Task<bool> IsGrantedAsync(
        FileAccessPolicyContext context,
        CancellationToken cancellationToken = default)
    {
        // Load the resource within context.TenantId, verify it exists, and apply
        // module-specific Read/Discover/Attach/Detach/Replace/Delete rules.
        return await CheckResourcePermissionAsync(context, cancellationToken);
    }
}
```

Unknown or duplicate policy registrations deny access. The core never interprets business resource IDs. `OwnerOnly` access is limited to the explicit owner unless `STNG.Files.ManageAll` is granted. `Restricted` access is granted only by a referenced resource policy (or the privileged manage-all permission). Public read is anonymous, but publishing requires the separate `STNG.Files.Publish` permission. Unauthorized read requests return not-found semantics to avoid revealing file existence. Discoverability is an independent flag and never changes read authorization.

Permissions are separate for upload, attach, detach, replace, delete, visibility changes, publishing, privileged management, and maintenance.

## Storage and configuration

The production host configures the named `file-assets` container with `Volo.Abp.BlobStoring.FileSystem`. `FileManagement:StorageRoot` must be an absolute path outside the application content/deployment directory. If omitted, the host uses the operating system's common application-data directory under `STNG/files`. Set the value at deployment time, for example with `FileManagement__StorageRoot`; grant the process identity read/write/create/delete access only to that directory.

```json
{
  "FileManagement": {
    "StorageRoot": "D:\\STNGData\\files",
    "MaximumFileSizeBytes": 26214400,
    "AllowedExtensions": [ ".pdf", ".png", ".jpg", ".jpeg" ],
    "AllowedContentTypes": [ "application/pdf", "image/png", "image/jpeg" ],
    "RequireKnownContentSignature": true,
    "RequireSecurityScan": false,
    "MaximumDiscoveryScanCount": 1000,
    "IncompleteUploadRetention": "1.00:00:00"
  }
}
```

Uploads are streamed to a staging file while enforcing the actual byte limit and calculating SHA-256; they are not buffered wholly in memory. The built-in validator verifies configured extension/MIME pairs and PDF, PNG, and JPEG signatures. Setting `RequireKnownContentSignature` to `false` weakens this protection and should only be done when a registered processing handler performs equivalent content verification.

The host also caps multipart parsing at the configured file limit plus 1 MiB of form overhead. When increasing the application limit, align the reverse-proxy, IIS, or Kestrel request-body limit as an explicit deployment change.

## Processing and malware scanning

Register ordered `IFileProcessingHandler` implementations for additional validation or processing. Register one or more `IFileSecurityScanner` implementations to integrate real malware scanners. Every registered scanner is executed. When `RequireSecurityScan` is true and no scanner is registered, upload is rejected; the subsystem never labels a file as scanned merely because scanning was requested.

Handlers and scanners receive a fresh read-only stream for the staged object. Throwing prevents publication, and files remain inaccessible until the full pipeline and BLOB write complete.

## Operations and maintenance

The generic API supports upload, anonymous-authorized read/metadata, authorized discovery, replacement, attach/detach, visibility changes, deletion, and incomplete-upload cleanup. Upload and replacement use separate database/storage steps with compensating deletion to handle the lack of a distributed transaction.

`POST /api/files/maintenance/cleanup-incomplete` requires `STNG.Files.Maintenance`. It marks stale `Pending`, `Validating`, or `Processing` rows as failed, removes their known storage keys, and deletes expired local staging files. Operators should monitor cleanup warnings. Enumerating provider-level objects that have no metadata row is provider-specific and is intentionally not implemented in this core version.

Discovery authorizes every candidate before returning metadata. To bound work, it scans at most `MaximumDiscoveryScanCount` matching rows; the reported total is the authorized total within that bounded candidate set. Modules with very large restricted collections can add a policy-aware indexed discovery projection in a future iteration.
