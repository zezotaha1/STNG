using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using STNG.Permissions;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;

namespace STNG.FileManagement;

public interface IFileAuthorizationService
{
    Task EnsureGrantedAsync(FileAsset asset, FileAccessOperation operation, CancellationToken cancellationToken = default);
    Task EnsureResourceGrantedAsync(FileAsset asset, FileReferenceInput reference, FileAccessOperation operation, CancellationToken cancellationToken = default);
    Task<bool> IsGrantedAsync(FileAsset asset, FileAccessOperation operation, CancellationToken cancellationToken = default);
}

public class FileAuthorizationService : IFileAuthorizationService, ITransientDependency
{
    private readonly IReadOnlyDictionary<string, IFileAccessPolicy> _policies;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentTenant _currentTenant;
    private readonly IPermissionChecker _permissionChecker;

    public FileAuthorizationService(
        IEnumerable<IFileAccessPolicy> policies,
        ICurrentUser currentUser,
        ICurrentTenant currentTenant,
        IPermissionChecker permissionChecker)
    {
        _policies = policies
            .GroupBy(x => x.ResourceType, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() == 1)
            .ToDictionary(x => x.Key, x => x.Single(), StringComparer.OrdinalIgnoreCase);
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _permissionChecker = permissionChecker;
    }

    public async Task EnsureGrantedAsync(FileAsset asset, FileAccessOperation operation, CancellationToken cancellationToken = default)
    {
        if (!await IsGrantedAsync(asset, operation, cancellationToken))
            throw new AbpAuthorizationException(FileManagementErrorCodes.AccessDenied);
    }

    public async Task EnsureResourceGrantedAsync(
        FileAsset asset,
        FileReferenceInput reference,
        FileAccessOperation operation,
        CancellationToken cancellationToken = default)
    {
        EnsureTenant(asset);
        var resourceType = reference.ResourceType?.Trim();
        var resourceId = reference.ResourceId?.Trim();
        var purpose = reference.Purpose?.Trim();
        if (string.IsNullOrWhiteSpace(resourceType) || string.IsNullOrWhiteSpace(resourceId) || string.IsNullOrWhiteSpace(purpose))
            throw new AbpAuthorizationException(FileManagementErrorCodes.AccessDenied);
        if (!_policies.TryGetValue(resourceType, out var policy))
            throw new AbpAuthorizationException(FileManagementErrorCodes.ResourcePolicyMissing);

        var context = new FileAccessPolicyContext(
            asset.Id,
            asset.TenantId,
            _currentUser.Id,
            resourceType,
            resourceId,
            purpose,
            operation);

        if (!await policy.IsGrantedAsync(context, cancellationToken))
            throw new AbpAuthorizationException(FileManagementErrorCodes.AccessDenied);
    }

    public async Task<bool> IsGrantedAsync(FileAsset asset, FileAccessOperation operation, CancellationToken cancellationToken = default)
    {
        if (asset.TenantId != _currentTenant.Id)
            return false;

        if (operation is FileAccessOperation.Read or FileAccessOperation.Discover)
        {
            if (asset.Status != FileAssetStatus.Ready)
                return false;
            if (operation == FileAccessOperation.Discover && !asset.IsDiscoverable)
                return false;
            if (asset.Visibility == FileVisibility.Public)
                return true;
        }

        if (!_currentUser.IsAuthenticated)
            return false;

        if ((operation is FileAccessOperation.Read or FileAccessOperation.Discover) &&
            await _permissionChecker.IsGrantedAsync(STNGPermissions.Files.ManageAll))
            return true;

        if (IsModification(operation))
        {
            var permission = GetOperationPermission(operation);
            if (permission != null && !await _permissionChecker.IsGrantedAsync(permission))
                return false;
            if (await _permissionChecker.IsGrantedAsync(STNGPermissions.Files.ManageAll))
                return true;
        }

        if (asset.Visibility == FileVisibility.OwnerOnly)
            return asset.OwnerUserId == _currentUser.Id;

        return await IsGrantedByAnyResourcePolicyAsync(asset, operation, cancellationToken);
    }

    private async Task<bool> IsGrantedByAnyResourcePolicyAsync(
        FileAsset asset,
        FileAccessOperation operation,
        CancellationToken cancellationToken)
    {
        foreach (var reference in asset.References)
        {
            if (!_policies.TryGetValue(reference.ResourceType, out var policy))
                continue;

            var context = new FileAccessPolicyContext(
                asset.Id,
                asset.TenantId,
                _currentUser.Id,
                reference.ResourceType,
                reference.ResourceId,
                reference.Purpose,
                operation);
            if (await policy.IsGrantedAsync(context, cancellationToken))
                return true;
        }
        return false;
    }

    private void EnsureTenant(FileAsset asset)
    {
        if (asset.TenantId != _currentTenant.Id)
            throw new AbpAuthorizationException(FileManagementErrorCodes.CrossTenantAccessDenied);
    }

    private static bool IsModification(FileAccessOperation operation) =>
        operation is FileAccessOperation.Attach or FileAccessOperation.Detach or FileAccessOperation.Replace or
            FileAccessOperation.Delete or FileAccessOperation.ChangeVisibility;

    private static string? GetOperationPermission(FileAccessOperation operation) => operation switch
    {
        FileAccessOperation.Attach => STNGPermissions.Files.Attach,
        FileAccessOperation.Detach => STNGPermissions.Files.Detach,
        FileAccessOperation.Replace => STNGPermissions.Files.Replace,
        FileAccessOperation.Delete => STNGPermissions.Files.Delete,
        FileAccessOperation.ChangeVisibility => STNGPermissions.Files.ChangeVisibility,
        _ => null
    };
}
