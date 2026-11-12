using global::Nuplane;
using global::Nuplane.Abstractions;
using global::Nuplane.Abstractions.PackageStoreProtection;
using global::Nuplane.Admin;
using global::Nuplane.Loading;

namespace Elsa.Studio.Hosting.Nuplane;

/// <summary>HTTP content policy over Nuplane's admitted guarded reader; published extracted installs are immutable.</summary>
internal sealed class PackageContentReader(INuplaneAdminOperations admin, IPackageStoreAdmission admission,
    IPackageAssemblyCatalog assemblies, ILogger<PackageContentReader> logger) : IDisposable
{
    public const int MaximumAssetBytes = 16 * 1024 * 1024;
    public const int MaximumMetadataBytes = 1024 * 1024;
    private readonly SemaphoreSlim _responses = new(4, 4);
    private readonly SemaphoreSlim _requests = new(32, 32);
    // Our catalog lookups and content admissions must not race each other for the non-waiting native root lock.
    private readonly SemaphoreSlim _reads = new(1, 1);

    public async Task<ActivePackagesSnapshot> GetPackagesAsync(CancellationToken cancellationToken)
    {
        await _reads.WaitAsync(cancellationToken);
        try { return await admin.GetPackagesAsync(cancellationToken); }
        finally { _reads.Release(); }
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

    public async Task<PackageReadScope?> OpenAssetAsync(string packageOrAssembly, CancellationToken cancellationToken)
    {
        await _reads.WaitAsync(cancellationToken);
        try
        {
            // RCL URLs name an assembly, which need not have its owning NuGet package's ID.
            // Both catalog queries precede admission: another query under root ownership would refuse the busy root.
            var owners = await AssemblyOwnersAsync(packageOrAssembly, cancellationToken);
            var packages = await admin.GetPackagesAsync(cancellationToken);
            if (owners.Any(owner => !packages.Packages.Any(package => SameOwner(package, owner))))
            {
                _reads.Release();
                return null;
            }
            var candidates = packages.Packages.Where(package =>
                string.Equals(package.PackageId, packageOrAssembly, StringComparison.OrdinalIgnoreCase) ||
                owners.Any(owner => SameOwner(package, owner))).ToArray();
            // Direct package IDs and loaded assembly aliases have equal authority. A collision must not
            // choose another package's bytes, including when its ID happens to equal this assembly's name.
            if (candidates.Length != 1) { _reads.Release(); return null; }
            return await AdmitAsync(candidates[0], cancellationToken);
        }
        catch { _reads.Release(); throw; }
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

    public async Task<PackageReadScope> OpenAsync(ActivePackage package, CancellationToken cancellationToken)
    {
        await _reads.WaitAsync(cancellationToken);
        try { return await AdmitAsync(package, cancellationToken); }
        catch { _reads.Release(); throw; }
    }

    private async Task<PackageReadScope> AdmitAsync(ActivePackage package, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(package.InstallPath))
            throw new PackageContentProfileException("Protected Studio content requires a stock extracted package install; archive/custom install shapes are unsupported.");
        var owner = await admission.AcquireForInstallPathsAsync([package.InstallPath], PackageStoreAdmissionKind.Loading, cancellationToken);
        try { return new(package, owner, owner.BorrowFor(package.InstallPath), _reads, logger); }
        catch { await owner.DisposeAsync(); throw; }
    }

    public void Dispose() { _responses.Dispose(); _requests.Dispose(); _reads.Dispose(); }

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

internal sealed class PackageReadScope(
    ActivePackage package,
    PackageStorePathAdmission owner,
    PackageStoreOperationBorrow borrow,
    SemaphoreSlim semaphore,
    ILogger logger) : IAsyncDisposable
{
    private int _disposed;
    public ActivePackage Package { get; } = package;

    public PackageFileContent? Read(string relativePath, int maximumBytes, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        var path = NormalizeRelativePath(relativePath);
        if (path is null) return null;
        var components = path.Split('/');
        borrow.ValidateForInstallPath(Package.InstallPath);
        var file = new FileInfo(Path.Combine(Package.InstallPath, Path.Combine(components)));
        file.Refresh();
        if (!file.Exists) return null;
        if (file.Length > maximumBytes)
            throw new PackageContentSizeException(maximumBytes);
        // No supported publisher/reconciler/maintenance writer can resize a published file under this admission.
        // Native traversal/identity/link checks still belong to PackageContent; the stat is not a byte reader.
        logger.LogDebug(new EventId(1001, "PackageContentPreflight"),
            "Reading protected package content {PackageId} {RelativePath}: {Bytes} bytes after size preflight.", Package.PackageId, path, file.Length);
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = PackageContent.TryReadFile(Package.InstallPath, path, borrow);
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes is null) return null;
        if (bytes.Length > maximumBytes)
            throw new PackageContentProfileException("Package content changed outside the supported immutable publication protocol.");
        return new(bytes, file.LastWriteTimeUtc);
    }

    public static string? NormalizeRelativePath(string relativePath)
    {
        var path = relativePath.Replace('\\', '/');
        return Path.IsPathRooted(path) || path.Split('/').Any(component => component is "" or "." or ".." || component.Contains(':') || component.Contains('\0')) ? null : path;
    }

    public string? FindRootFile(string extension, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        borrow.ValidateForInstallPath(Package.InstallPath);
        var names = Directory.EnumerateFileSystemEntries(Package.InstallPath).Take(16_385).Select(Path.GetFileName).ToArray();
        if (names.Length > 16_384)
            throw new PackageContentProfileException("The package root exceeds Studio's supported metadata inventory bound.");
        return names.FirstOrDefault(name => string.Equals(Path.GetExtension(name), extension, StringComparison.OrdinalIgnoreCase));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { borrow.Dispose(); }
        finally
        {
            try { await owner.DisposeAsync(); }
            finally { semaphore.Release(); }
        }
    }
}

internal sealed record PackageFileContent(byte[] Bytes, DateTimeOffset LastModified);
internal sealed class PackageContentProfileException(string message) : Exception(message);
internal sealed class PackageContentSizeException(int maximumBytes) : Exception($"Package content exceeds Studio's supported {maximumBytes}-byte limit.");
