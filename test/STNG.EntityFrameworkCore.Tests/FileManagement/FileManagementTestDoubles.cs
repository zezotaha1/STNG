using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using STNG.Permissions;
using Volo.Abp.Authorization.Permissions;

namespace STNG.FileManagement;

public sealed class TestPermissionChecker : IPermissionChecker
{
    private readonly HashSet<string> _explicitGrants = new(StringComparer.Ordinal);

    public void Grant(string permission) => _explicitGrants.Add(permission);
    public void Reset() => _explicitGrants.Clear();

    public Task<bool> IsGrantedAsync(string name) => Task.FromResult(IsGranted(name));
    public Task<bool> IsGrantedAsync(ClaimsPrincipal? claimsPrincipal, string name) => Task.FromResult(IsGranted(name));

    public Task<MultiplePermissionGrantResult> IsGrantedAsync(string[] names) =>
        Task.FromResult(CreateResult(names));

    public Task<MultiplePermissionGrantResult> IsGrantedAsync(ClaimsPrincipal? claimsPrincipal, string[] names) =>
        Task.FromResult(CreateResult(names));

    private bool IsGranted(string name) =>
        name is not (STNGPermissions.Files.ManageAll or STNGPermissions.Files.Publish) || _explicitGrants.Contains(name);

    private MultiplePermissionGrantResult CreateResult(string[] names)
    {
        var result = new MultiplePermissionGrantResult();
        foreach (var name in names)
            result.Result[name] = IsGranted(name) ? PermissionGrantResult.Granted : PermissionGrantResult.Prohibited;
        return result;
    }
}

public sealed class TestFileAccessPolicy : IFileAccessPolicy
{
    private readonly HashSet<FileAccessOperation> _grants = [];
    public string ResourceType => "test-resource";

    public void Grant(params FileAccessOperation[] operations)
    {
        foreach (var operation in operations)
            _grants.Add(operation);
    }

    public void Reset() => _grants.Clear();

    public Task<bool> IsGrantedAsync(FileAccessPolicyContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(context.ResourceId != "missing" && _grants.Contains(context.Operation));
}

public sealed class RecordingFileStorage : IFileStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _objects = new(StringComparer.Ordinal);
    public bool FailNextSave { get; set; }
    public int Count => _objects.Count;
    public IReadOnlyCollection<string> Keys => _objects.Keys.ToArray();

    public async Task SaveAsync(string storageKey, Stream stream, CancellationToken cancellationToken = default)
    {
        if (FailNextSave)
        {
            FailNextSave = false;
            throw new IOException("Simulated storage failure.");
        }

        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        if (!_objects.TryAdd(storageKey, memory.ToArray()))
            throw new IOException("Storage key already exists.");
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        if (!_objects.TryGetValue(storageKey, out var bytes))
            throw new FileNotFoundException("The storage object does not exist.", storageKey);
        return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    public Task DeleteIfExistsAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        _objects.TryRemove(storageKey, out _);
        return Task.CompletedTask;
    }

    public void Reset()
    {
        _objects.Clear();
        FailNextSave = false;
    }
}
