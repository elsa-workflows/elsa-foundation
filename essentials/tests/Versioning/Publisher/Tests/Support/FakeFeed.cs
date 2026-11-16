namespace Elsa.Versioning.Publisher.Tests.Support;

/// <summary>
/// An in-memory feed that answers as a NuGet feed does: a new version is taken (201), an existing one is rejected and
/// kept (409), and a version's package can be read back. Pushes can be made to fail, and listing to go unanswered.
/// </summary>
internal sealed class FakeFeed : IPackageFeed
{
    private readonly Dictionary<string, byte[]> packages = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every push the feed answered, in order.</summary>
    public List<(string PackageId, string Version, PushStatus Status)> Pushes { get; } = [];

    /// <summary>Package ids whose pushes fail, as a push that gets no answer does.</summary>
    public HashSet<string> FailingPushes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When true, the feed does not answer a request for a package's versions.</summary>
    public bool ListingFails { get; set; }

    /// <summary>The ids and versions the pushes took, in order.</summary>
    public IEnumerable<string> Taken => Pushes.Where(push => push.Status == PushStatus.Pushed).Select(push => $"{push.PackageId} {push.Version}");

    /// <summary>Puts a package on the feed as though something else had pushed it; a null fingerprint packs none.</summary>
    public void Seed(string packageId, string version, string? fingerprint = null) =>
        packages[Key(packageId, version)] = SyntheticPackages.Bytes(packageId, version, fingerprint, commit: new string('0', 40));

    /// <summary>The feed's copy of a version, or null.</summary>
    public byte[]? Package(string packageId, string version) => packages.GetValueOrDefault(Key(packageId, version));

    /// <summary>Every version the feed holds of a package id.</summary>
    public IReadOnlyList<string> Versions(string packageId) =>
        packages.Keys.Where(key => key.StartsWith(packageId.ToLowerInvariant() + "/", StringComparison.Ordinal)).Select(key => key[(key.IndexOf('/') + 1)..]).Order(StringComparer.Ordinal).ToArray();

    public Task<PushResult> PushAsync(string packagePath, CancellationToken cancellationToken)
    {
        var package = PackedPackage.Read(packagePath);
        var status = FailingPushes.Contains(package.PackageId) ? PushStatus.Failed
            : packages.TryAdd(Key(package.PackageId, package.Version), File.ReadAllBytes(packagePath)) ? PushStatus.Pushed
            : PushStatus.AlreadyExists;
        Pushes.Add((package.PackageId, package.Version, status));
        return Task.FromResult(new PushResult(status, status switch
        {
            PushStatus.Pushed => "201 Created",
            PushStatus.AlreadyExists => "409 Conflict",
            _ => "no answer: the connection was reset"
        }));
    }

    public Task<IReadOnlyList<string>> ListVersionsAsync(string packageId, CancellationToken cancellationToken) =>
        ListingFails ? throw new FeedException("503 Service Unavailable") : Task.FromResult(Versions(packageId));

    public Task<string> ReadFingerprintAsync(string packageId, string version, CancellationToken cancellationToken) =>
        ReadAsync(packageId, version, PackedPackage.ReadFingerprint);

    public Task<string> ReadSourceCommitAsync(string packageId, string version, CancellationToken cancellationToken) =>
        ReadAsync(packageId, version, PackedPackage.ReadSourceCommit);

    private Task<string> ReadAsync(string packageId, string version, Func<Stream, string, string> read)
    {
        var bytes = Package(packageId, version) ?? throw new FeedException($"{packageId} {version} answered 404 Not Found.");
        try
        {
            return Task.FromResult(read(new MemoryStream(bytes), $"{packageId} {version}"));
        }
        catch (InvalidOperationException exception)
        {
            throw new FeedException(exception.Message, exception);
        }
    }

    private static string Key(string packageId, string version) => $"{packageId.ToLowerInvariant()}/{version.ToLowerInvariant()}";
}
