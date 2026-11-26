using global::Nuplane.Abstractions;
using global::Nuplane.Admin;
using global::Nuplane.Loading;
using global::Nuplane.PackageArchives;

namespace Elsa.Studio.Hosting.Nuplane;

/// <summary>Studio content policy over Nuplane's verified package archives.</summary>
internal sealed class PackageContentReader(INuplaneAdminOperations admin, IPackageArchiveReader archives,
    IPackageAssemblyCatalog assemblies, ILogger<PackageContentReader> logger) : IDisposable
{
    public const int MaximumAssetBytes = 16 * 1024 * 1024;
    public const int MaximumMetadataBytes = 1024 * 1024;
    private readonly SemaphoreSlim _responses = new(4, 4);
    private readonly SemaphoreSlim _requests = new(32, 32);
    // Catalog queries in a host that uses a coordinated store share a non-waiting root lock.
    // Archive verification uses detached bytes and runs outside this gate.
    private readonly SemaphoreSlim _catalogQueries = new(1, 1);

    public async Task<ActivePackagesSnapshot> GetPackagesAsync(CancellationToken cancellationToken)
    {
        await _catalogQueries.WaitAsync(cancellationToken);
        try { return await admin.GetPackagesAsync(cancellationToken); }
        finally { _catalogQueries.Release(); }
    }

    public async Task<IAsyncDisposable?> TryAcquireResponseAsync(CancellationToken cancellationToken)
    {
        if (!await _requests.WaitAsync(0, cancellationToken)) return null;
        logger.LogDebug(new EventId(1002, "PackageContentRequestQueued"), "Admitted a bounded package-asset request.");
        try
        {
            await _responses.WaitAsync(cancellationToken);
            return new ResponseLease(_responses, _requests);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _requests.Release();
            logger.LogDebug(new EventId(1003, "PackageContentRequestCancelled"), "Released a cancelled queued package-asset request.");
            throw;
        }
        catch { _requests.Release(); throw; }
    }

    public async Task<PackageArchive?> OpenAssetAsync(string packageOrAssembly, CancellationToken cancellationToken)
    {
        var package = await ResolveAssetOwnerAsync(packageOrAssembly, cancellationToken);
        return package is null ? null : await OpenAsync(package, cancellationToken);
    }

    private async Task<ActivePackage?> ResolveAssetOwnerAsync(string packageOrAssembly, CancellationToken cancellationToken)
    {
        await _catalogQueries.WaitAsync(cancellationToken);
        try
        {
            // RCL URLs can name either a package ID or a loaded assembly.
            var owners = await AssemblyOwnersAsync(packageOrAssembly, cancellationToken);
            var packages = await admin.GetPackagesAsync(cancellationToken);
            if (owners.Any(owner => !packages.Packages.Any(package => SameOwner(package, owner))))
                return null;

            var candidates = packages.Packages.Where(package =>
                string.Equals(package.PackageId, packageOrAssembly, StringComparison.OrdinalIgnoreCase) ||
                owners.Any(owner => SameOwner(package, owner))).ToArray();
            // Direct package IDs and loaded assembly aliases have equal authority.
            return candidates.Length == 1 ? candidates[0] : null;
        }
        finally { _catalogQueries.Release(); }
    }

    private async Task<(string PackageId, string Version)[]> AssemblyOwnersAsync(string name, CancellationToken cancellationToken)
    {
        // Leave all reflection references in this short-lived query; never retain assemblies across generations.
        var loaded = await assemblies.GetPackagedAssembliesAsync(cancellationToken);
        return loaded.Where(package => package.Assemblies.Any(assembly =>
                string.Equals(assembly.GetName().Name, name, StringComparison.OrdinalIgnoreCase)))
            .Select(package => (package.PackageId, package.Version)).ToArray();
    }

    private static bool SameOwner(ActivePackage package, (string PackageId, string Version) owner) =>
        string.Equals(package.PackageId, owner.PackageId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(package.Version, owner.Version, StringComparison.OrdinalIgnoreCase);

    public ValueTask<PackageArchive> OpenAsync(ActivePackage package, CancellationToken cancellationToken) =>
        archives.OpenAsync(package, cancellationToken);

    public static byte[] Read(PackageArchiveEntry entry, int maximumBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (entry.Length > maximumBytes)
            throw new PackageContentSizeException(maximumBytes);

        var bytes = entry.ReadAllBytes();
        cancellationToken.ThrowIfCancellationRequested();
        return bytes;
    }

    public static string? NormalizeRelativePath(string relativePath)
    {
        var path = relativePath.Replace('\\', '/');
        return Path.IsPathRooted(path) || path.Split('/').Any(component => component is "" or "." or ".." || component.Contains(':') || component.Contains('\0')) ? null : path;
    }

    public void Dispose() { _responses.Dispose(); _requests.Dispose(); _catalogQueries.Dispose(); }

    private sealed class ResponseLease(SemaphoreSlim semaphore, SemaphoreSlim requests) : IAsyncDisposable
    {
        private int _disposed;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) { semaphore.Release(); requests.Release(); }
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class PackageContentSizeException(int maximumBytes) : Exception($"Package content exceeds Studio's supported {maximumBytes}-byte limit.");
