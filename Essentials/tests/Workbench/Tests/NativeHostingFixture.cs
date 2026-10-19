using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Elsa.Workbench.Tests;

public enum NativeHostingProfile
{
    InProcess,
    Clustered
}

/// <summary>
/// Owns the shared durable state and Workbench processes used by native-hosting recovery tests.
/// </summary>
internal sealed class NativeHostingFixture : IAsyncDisposable
{
    private const string DistributedRuntimeFeature = "WorkflowsRuntimeDistributed";
    private const string DistributedPersistenceFeature = "WorkflowsRuntimeDistributedEntityFrameworkCorePersistence";
    private const string DistributedTransportFeature = "WorkflowsRuntimeDistributedCommandTransportEntityFrameworkCorePersistence";

    private readonly string _root;
    private readonly string _packagePath;
    private readonly bool _observeNativeHosting;
    private readonly List<WorkbenchProcess> _hosts = [];
    private readonly HashSet<WorkbenchProcess> _currentHosts = [];
    private readonly HashSet<string> _roles = new(StringComparer.Ordinal);
    private bool _disposed;

    private NativeHostingFixture(NativeHostingProfile profile, string root, string packagePath, string packageSha256,
        bool observeNativeHosting)
    {
        Profile = profile;
        _root = root;
        _packagePath = packagePath;
        _observeNativeHosting = observeNativeHosting;
        PackageSha256 = packageSha256;
        ScenarioId = $"native-hosting-{Guid.NewGuid():N}"[..29];
        JournalDirectory = Path.Combine(root, "journal");
        Directory.CreateDirectory(JournalDirectory);
        Directory.CreateDirectory(Path.Combine(root, "locks"));
    }

    internal NativeHostingProfile Profile { get; }
    internal string ScenarioId { get; }
    internal string JournalDirectory { get; }
    internal string PackagePath => _packagePath;
    internal string PackageSha256 { get; }
    internal string SharedRoot => _root;

    internal static NativeHostingFixture Create(NativeHostingProfile profile, bool observeNativeHosting = false)
    {
        if (!Enum.IsDefined(profile))
            throw new ArgumentOutOfRangeException(nameof(profile), profile, "Select an explicit native hosting profile.");
        if (observeNativeHosting && profile != NativeHostingProfile.Clustered)
            throw new ArgumentException("The transport observation requires the clustered profile.", nameof(profile));
        var root = Directory.CreateTempSubdirectory("elsa-native-hosting-").FullName;

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "packages"));
            var packagePath = NativeHostingScenario.CreatePackage(root);
            var packageSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(packagePath)));
            return new NativeHostingFixture(profile, root, packagePath, packageSha256, observeNativeHosting);
        }
        catch
        {
            Directory.Delete(root, recursive: true);
            throw;
        }
    }

    /// <summary>Starts one host and returns only after its startup, including persistence migrations, completes.</summary>
    internal async Task<WorkbenchProcess> StartAsync(string role, bool runWorkers = false)
    {
        ThrowIfDisposed();
        ValidateRole(role);
        if (!_roles.Add(role))
            throw new InvalidOperationException($"A host with role '{role}' has already been started in this scenario.");

        var shell = NativeHostingScenario.WitnessShell();
        var settings = new Dictionary<string, string>(shell.Settings, StringComparer.Ordinal)
        {
            ["ConnectionStrings:Elsa"] = $"Data Source={Path.Combine(_root, "elsa.db")};Pooling=False",
            ["CShells:Shells:default:Features:FileSystemDistributedLocking:LocksFolderPath"] = Path.Combine(_root, "locks"),
            ["Elsa:DataProtection:EntityFrameworkCore:Enabled"] = "true",
            ["Elsa:DataProtection:EntityFrameworkCore:Provider"] = "Sqlite",
            ["Elsa:DataProtection:EntityFrameworkCore:ConnectionString"] = $"Data Source={Path.Combine(_root, "keys.db")};Pooling=False"
        };

        if (Profile == NativeHostingProfile.Clustered)
        {
            settings["Elsa:Cluster:Membership:HostId"] = $"{ScenarioId}-{role}";
            settings["Elsa:Cluster:Membership:EntityFrameworkCore:Enabled"] = "true";
            settings["Elsa:Cluster:Membership:EntityFrameworkCore:Provider"] = "Sqlite";
            settings["Elsa:Cluster:Membership:EntityFrameworkCore:ConnectionString"] = $"Data Source={Path.Combine(_root, "membership.db")};Pooling=False";
        }

        try
        {
            var host = await WorkbenchProcess.StartAsync(
                shell with { Settings = settings },
                directory => ConfigureContentRoot(directory, role, runWorkers));
            _hosts.Add(host);
            _currentHosts.Add(host);
            return host;
        }
        catch
        {
            _roles.Remove(role);
            throw;
        }
    }

    /// <summary>Starts hosts one at a time so the first host finishes startup migrations before the next starts.</summary>
    internal async Task<IReadOnlyList<WorkbenchProcess>> SerialStartAsync(params (string Role, bool RunWorkers)[] hosts)
    {
        var started = new List<WorkbenchProcess>(hosts.Length);
        foreach (var (role, runWorkers) in hosts)
            started.Add(await StartAsync(role, runWorkers));
        return started;
    }

    /// <summary>Restarts a fixture-owned host while retaining both process wrappers until fixture cleanup.</summary>
    internal async Task<WorkbenchProcess> RestartAsync(WorkbenchProcess host, Action<string>? prepareRestart = null)
    {
        ThrowIfDisposed();
        if (!_currentHosts.Remove(host))
            throw new ArgumentException("The process is not a current host owned by this fixture.", nameof(host));

        var replacement = await host.CrashAndRestartAsync(prepareRestart);
        _hosts.Add(replacement);
        _currentHosts.Add(replacement);
        return replacement;
    }

    internal void HoldWorkerClaims(string role)
    {
        ThrowIfDisposed();
        ValidateRole(role);
        File.WriteAllText(Path.Combine(JournalDirectory, $"hold-workers-{role}"), role);
    }

    internal void ReleaseWorkerClaims(string role)
    {
        ThrowIfDisposed();
        ValidateRole(role);
        File.Delete(Path.Combine(JournalDirectory, $"hold-workers-{role}"));
    }

    private void ConfigureContentRoot(string directory, string role, bool runWorkers)
    {
        NativeHostingScenario.ConfigureContentRoot(directory);

        var targetPackagePath = Path.Combine(directory, "packages", Path.GetFileName(_packagePath));
        File.Copy(_packagePath, targetPackagePath);
        var copiedSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(targetPackagePath)));
        if (!StringComparer.Ordinal.Equals(PackageSha256, copiedSha256))
            throw new IOException("The private-feed package copy does not match the scenario's pinned package bytes.");

        var shellsPath = Path.Combine(directory, "shells.json");
        var shells = JsonNode.Parse(File.ReadAllText(shellsPath)) as JsonObject
            ?? throw new InvalidDataException($"Could not parse {shellsPath} as a JSON object.");
        var features = shells["CShells"]?["Shells"]?["default"]?["Features"]?.AsObject()
            ?? throw new InvalidDataException($"The default shell features were missing from {shellsPath}.");

        features["BackgroundActivities"]!["RunWorkers"] = runWorkers;
        features["BackgroundActivityWitness"] = new JsonObject
        {
            ["JournalDirectory"] = JournalDirectory,
            ["Role"] = role,
            ["NativeHostingObservation"] = _observeNativeHosting
        };

        if (Profile == NativeHostingProfile.InProcess)
        {
            features.Remove(DistributedRuntimeFeature);
            features.Remove(DistributedPersistenceFeature);
            features.Remove(DistributedTransportFeature);
        }
        else
        {
            features[DistributedRuntimeFeature] = _observeNativeHosting
                // Departure must reclaim a still-live routing lease; waiting for its expiry would prove less.
                ? new JsonObject { ["LeaseDurationSeconds"] = 120 }
                : new JsonObject();
            features[DistributedPersistenceFeature] = new JsonObject();
            features[DistributedTransportFeature] = new JsonObject();
        }

        File.WriteAllText(shellsPath, shells.ToJsonString());
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        var failures = new List<Exception>();
        for (var index = _hosts.Count - 1; index >= 0; index--)
        {
            var host = _hosts[index];
            try
            {
                await host.DisposeAsync();
            }
            catch (Exception exception)
            {
                failures.Add(new InvalidOperationException(
                    $"Could not clean up fixture-owned Workbench content root '{host.ContentRoot}'. The shared scenario root '{_root}' has been preserved.",
                    exception));
            }
        }

        foreach (var contentRoot in _hosts.Select(host => host.ContentRoot).Distinct(StringComparer.Ordinal))
        {
            if (Directory.Exists(contentRoot))
                failures.Add(new IOException($"Fixture-owned Workbench content root '{contentRoot}' remains after host cleanup."));
        }

        if (failures.Count > 0)
            throw new AggregateException("One or more fixture-owned Workbench hosts failed to stop; the shared scenario root was preserved.", failures);

        Directory.Delete(_root, recursive: true);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static void ValidateRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role) || role.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new ArgumentException("Host roles may contain only ASCII letters, digits, and hyphens.", nameof(role));
    }
}
