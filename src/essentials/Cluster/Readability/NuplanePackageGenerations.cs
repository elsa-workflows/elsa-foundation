using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Cluster.Core.Contracts;
using Elsa.Persistence.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nuplane.Loading;

namespace Elsa.Cluster.Readability;

/// <summary>
/// Which generations of this host's Nuplane packages an in-place upgrade has superseded (spec 183, FR-021, amended
/// 2026-09-29). Nuplane loads a host-integrated package graph into a load context it never unloads, so after an upgrade
/// the old generation's assemblies stay in <see cref="AssemblyLoadContext.All"/> for the life of the process; this is
/// what tells the readability report and the EF activation guard to stop reading them, and when.
/// </summary>
/// <remarks>
/// <para>
/// <b>Replaced for activation</b> starts with positive evidence from <see cref="IPackageAssemblyCatalog"/>:
/// an assembly in a Nuplane-created load context is replaced when the active package set names another assembly of the
/// same name. Successful catalog reads remember those exact identities weakly, so removing the last package does not
/// restore a proven replaced generation to EF discovery. Selecting an identity again clears its history, including on
/// rollback. Missing or unreadable catalog evidence yields no replacement evidence for that read; absence alone never
/// proves replacement. The default context and contexts Nuplane did not create are never excluded.
/// </para>
/// <para>
/// <b>Retired</b> still requires fresh replacement evidence from the current package catalog; activation history never
/// retires a removed package. It also requires that neither a shell generation nor the next shell's catalog can run that
/// exact assembly. A build participant establishes an unknown conservative pin before catalog access, then narrows it
/// to the assemblies of every feature in the exact selected snapshot, enabled or not. Each selected feature pins its
/// entire non-default load context, including sibling assemblies, but unrelated scanned assemblies are not pinned.
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>A build candidate or published shell.</b> The root-only <see cref="NuplanePackageGenerationBuildParticipant"/>
/// starts the candidate pin before the feature catalog is read. CShells supplies the exact detailed snapshot before
/// feature construction. Only upstream-confirmed pre-provider unwind or complete provider teardown releases that
/// build lease; a failed terminal notification keeps it. Lifecycle notifications may observe a drain failure but never
/// release lease-owned generations. A weak shell association prevents late notifications from recreating a released
/// lease as a legacy pin.
/// </description></item>
/// <item><description>
/// <b>A shell with no build lease.</b> The public lifecycle fallback establishes an unknown pin at the first lifecycle
/// notification and narrows it from that shell's descriptors. It releases only after a successful drain; an unknown or
/// failed drain stays conservative. This fallback cannot protect stock registry builds before catalog selection.
/// </description></item>
/// <item><description>
/// <b>The next shell generation.</b> CShells builds from the runtime feature catalog's current snapshot, which advances
/// according to the host's refresh policy. The snapshot's features and any replaced assembly it names are therefore
/// pinned too. An uninitialized or unreadable feature catalog
/// conservatively pins every current replacement because the first build may select any of them.
/// </description></item>
/// </list>
/// <para>
/// Whenever the complete retired set changes, including reintroduction of an assembly, a coalesced asynchronous loop
/// republishes the host's report. An unchanged set is quiet. Stock catalog commits queue reevaluation through a
/// root-owned subscription; catalogs without commit notifications use a conservative snapshot-generation watch while
/// replacements exist, including after retirement so rollback can be detected. Shutdown unsubscribes and stops the
/// watch. A catalog whose detailed read alone fails while its readable generation stays unchanged needs a later
/// generation change or another evaluation trigger to retry.
/// </para>
/// <para>
/// One nondisposable source instance serves the whole host and is bound to the host's own catalogs and membership.
/// The separate adapter is root-only and is excluded from copied shell providers. The instance is not a Nuplane observer,
/// and asynchronous publication does not promise persisted readability before an initializer runs.
/// </para>
/// </remarks>
public sealed class NuplanePackageGenerations : ISupersededAssemblySource, IShellLifecycleSubscriber
{
    /// <summary>
    /// The assembly that defines every load context Nuplane gives a package, a graph's or a single package's
    /// (<c>PackageAssemblyLoadContext</c>, <c>PackageGraphLoadContext</c> and <c>HostIntegratedPackageGraphLoadContext</c>).
    /// Named rather than referenced, so composing readability takes Nuplane's abstractions and not its runtime; a rename
    /// leaves every context counting, and a test over the pinned Nuplane.Loading fails first.
    /// </summary>
    public const string NuplaneLoadingAssembly = "Nuplane.Loading";

    /// <summary>How often a custom feature catalog's snapshot generation is read while replacements exist.</summary>
    public static readonly TimeSpan CatalogWatchInterval = TimeSpan.FromSeconds(1);

    /// <summary>A snapshot generation for a catalog that has not been initialized: CShells numbers its snapshots from 1.</summary>
    private const long Uninitialized = 0;

    /// <summary>Every shell generation that can still run code, until its container has finished disposing.</summary>
    private readonly ConcurrentDictionary<Generation, byte> _live = new();

    /// <summary>Build candidates and published shells protected by the upstream build lease.</summary>
    private readonly HashSet<BuildLease> _buildLeases = [];

    /// <summary>Strong identity mapping only while a lease is unresolved/live.</summary>
    private readonly Dictionary<ShellDescriptor, BuildLease> _leasesByDescriptor = [];

    private readonly Lock _pinGate = new();

    /// <summary>The generation each shell CShells has shown this host belongs to, so a lifecycle notification finds it.</summary>
    private readonly ConditionalWeakTable<IShell, Generation> _shells = new();

    /// <summary>Weak identity retained for late lifecycle notifications after the lease is released.</summary>
    private readonly ConditionalWeakTable<IShell, BuildLease> _buildOwnedShells = new();

    // Weak keys preserve positive activation evidence without retaining an assembly or its load context.
    private readonly ConditionalWeakTable<Assembly, object> _activationReplacements = new();
    private readonly SemaphoreSlim _replacementReads = new(1, 1);

    private readonly HostContainer _host = new();
    private ILogger _logger = NullLogger.Instance;

    /// <summary>1 while a republish has been asked for and its loop has not yet taken it up.</summary>
    private int _publishRequested;

    /// <summary>1 while the republish loop runs; there is never more than one.</summary>
    private int _publishing;

    /// <summary>What was retired when the republish loop last evaluated, which only that loop reads and writes.</summary>
    private IReadOnlySet<Assembly> _retiredWhenLastEvaluated = LoadedAssemblies.NoneSuperseded;

    /// <summary>1 while the feature catalog is watched for a refresh.</summary>
    private int _watchingCatalog;

    private readonly CancellationTokenSource _watchStop = new();
    private int _stopped;
    private int _commitNotificationsAvailable;

    private IServiceProvider? Host => _host.Root;

    /// <summary>
    /// Binds this instance to the host's own container and returns it as the lifecycle subscriber CShells' registry
    /// subscribes. CShells resolves its subscribers from the host container alone, before it builds any shell, and never
    /// copies them into one, so the first binding is the host's; any later one, from whatever container, changes nothing.
    /// </summary>
    public IShellLifecycleSubscriber BindTo(IServiceProvider host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (_host.Bind(host))
            _logger = host.GetService<ILoggerFactory>()?.CreateLogger<NuplanePackageGenerations>() ?? NullLogger<NuplanePackageGenerations>.Instance;
        return this;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlySet<Assembly>> GetReplacedAsync(CancellationToken cancellationToken = default) =>
        ReadReplacedAsync(includeActivationHistory: true, cancellationToken);

    private async ValueTask<IReadOnlySet<Assembly>> ReadReplacedAsync(bool includeActivationHistory, CancellationToken cancellationToken)
    {
        if (Host?.GetService<IPackageAssemblyCatalog>() is not { } catalog)
            return LoadedAssemblies.NoneSuperseded;

        // Serialize the read as well as the update, so an older snapshot cannot reset newer history.
        await _replacementReads.WaitAsync(cancellationToken);
        try
        {
            HashSet<Assembly> current;
            try
            {
                current = [.. (await catalog.GetPackagedAssembliesAsync(cancellationToken)).SelectMany(package => package.Assemblies)];
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // No evidence is no replacement: every generation keeps counting, which only delays finalization, rather than
                // failing the publish every module activation waits on.
                _logger.LogWarning(exception, "Nuplane's package catalog could not be read, so no package generation is treated as superseded.");
                return LoadedAssemblies.NoneSuperseded;
            }

            var currentNames = current.Select(NameOf).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var loaded = AssemblyLoadContext.All
                .Where(context => context != AssemblyLoadContext.Default && context.GetType().Assembly.GetName().Name == NuplaneLoadingAssembly)
                .SelectMany(context => context.Assemblies)
                .ToArray();
            var replaced = loaded
                .Where(assembly => !current.Contains(assembly) && currentNames.Contains(NameOf(assembly)))
                .ToHashSet();

            // A rollback may select an identity previously replaced. It stays eligible even after later removal.
            foreach (var assembly in current)
                _activationReplacements.Remove(assembly);
            foreach (var assembly in replaced)
                _activationReplacements.GetValue(assembly, static _ => new object());

            if (!includeActivationHistory)
                return replaced;

            return loaded.Where(assembly => !current.Contains(assembly) &&
                _activationReplacements.TryGetValue(assembly, out _)).ToHashSet();
        }
        finally
        {
            _replacementReads.Release();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Custom catalogs without commit notifications remain watched while replacements exist, so both retirement and
    /// reintroduction can be detected. Live pins are copied only after the asynchronous catalog reads finish.
    /// </remarks>
    public async ValueTask<IReadOnlySet<Assembly>> GetRetiredAsync(CancellationToken cancellationToken = default)
    {
        // Retirement still requires current replacement evidence; removal alone never credits readability.
        var replaced = await ReadReplacedAsync(includeActivationHistory: false, cancellationToken);
        if (replaced.Count == 0)
            return replaced;

        // Read before the snapshot the pins come from, so a refresh that commits in between shows as a move.
        var catalogGeneration = FeatureCatalogGeneration();
        var byCatalog = await PinnedByFeatureCatalogAsync(replaced, cancellationToken);
        IReadOnlySet<Assembly>?[] pins;
        lock (_pinGate)
        {
            pins = _live.Keys.Select(generation => generation.Features)
                .Concat(_buildLeases.Select(lease => lease.Features))
                .ToArray();
        }
        var byGenerations = new HashSet<AssemblyLoadContext>();
        foreach (var pin in pins)
            byGenerations.UnionWith(PackageContexts(pin ?? replaced));

        if (Volatile.Read(ref _commitNotificationsAvailable) == 0 && Host?.GetService<IRuntimeFeatureCatalog>() is not null && replaced.Count > 0)
            WatchFeatureCatalog(catalogGeneration ?? Uninitialized);
        return replaced.Where(assembly => !byCatalog.Contains(ContextOf(assembly)) && !byGenerations.Contains(ContextOf(assembly))).ToHashSet();
    }

    /// <summary>
    /// Attaches the drain for an unleased legacy shell, which is pinned from its first lifecycle notification until that
    /// drain completes. Lease-owned generations never use lifecycle callbacks as a release authority.
    /// </summary>
    public Task OnStateChangedAsync(
        IShell shell,
        ShellLifecycleState previous,
        ShellLifecycleState current,
        CancellationToken cancellationToken = default)
    {
        BuildLease? buildLease;
        lock (_pinGate)
        {
            if (!_buildOwnedShells.TryGetValue(shell, out buildLease) && _leasesByDescriptor.TryGetValue(shell.Descriptor, out buildLease))
                _buildOwnedShells.Add(shell, buildLease);
        }

        if (buildLease is not null)
        {
            if (current is ShellLifecycleState.Draining or ShellLifecycleState.Drained && shell.Drain is { } buildDrain && buildLease.ObserveDrain())
                _ = ObserveDrainFailureAsync(buildLease, buildDrain);
            return Task.CompletedTask;
        }

        if (!_shells.TryGetValue(shell, out var generation))
        {
            if (current == ShellLifecycleState.Disposed)
                return Task.CompletedTask;
            _logger.LogWarning(
                "Shell generation {Shell} did not construct this host's generation tracking before its initializers ran, so it is tracked " +
                "from {State} on, and every package generation it composes from keeps counting for this host's readability report until " +
                "a drain of it completes.",
                shell.Descriptor,
                current);
            generation = Track(shell, shell.ServiceProvider);
        }

        // Drained is awaited by the drain before it disposes the shell; Draining is not, and Drained is skipped when the host's
        // shutdown disposes a shell whose drain overran, so both attach.
        if (current is ShellLifecycleState.Draining or ShellLifecycleState.Drained && shell.Drain is { } drain && generation.Attach(drain))
            _ = ReleaseWhenDrainedAsync(generation, drain);
        return Task.CompletedTask;
    }

    /// <summary>Starts conservative accounting before the runtime feature catalog is read.</summary>
    internal IShellGenerationBuildLease Begin(ShellGenerationBuildContext context)
    {
        var lease = new BuildLease(this, context.Descriptor);
        lock (_pinGate)
        {
            _buildLeases.Add(lease);
            _leasesByDescriptor.Add(context.Descriptor, lease);
        }
        RequestPublish();
        return lease;
    }

    /// <summary>Stops the fallback catalog watcher at root lifetime end.</summary>
    internal void StopCatalogTracking()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 0)
            _watchStop.Cancel();
    }

    internal void EnableCommitNotifications() => Volatile.Write(ref _commitNotificationsAvailable, 1);

    internal void CatalogCommitted()
    {
        if (Volatile.Read(ref _stopped) == 0)
            RequestPublish();
    }

    internal void CommitSubscriptionFailed(Exception exception) =>
        _logger.LogWarning(exception, "Runtime feature catalog commit notifications could not be attached; readability will use the conservative catalog watch.");

    internal void CommitUnsubscriptionFailed(Exception exception) =>
        _logger.LogWarning(exception, "Runtime feature catalog commit notifications could not be detached cleanly; host readability tracking has stopped.");

    private Generation Track(IShell shell, IServiceProvider container)
    {
        var generation = new Generation(shell, features: null);
        lock (_pinGate)
        {
            if (_shells.TryGetValue(shell, out var existing))
                return existing;
            _shells.Add(shell, generation);
            _live.TryAdd(generation, 0);
        }
        RequestPublish();

        // Establish the conservative pin before resolving a shell's copied feature descriptors.
        var features = FeatureAssemblies(container, shell);
        lock (_pinGate)
        {
            if (_live.ContainsKey(generation))
                generation.SetFeatures(features);
        }
        RequestPublish();
        return generation;
    }

    /// <summary>
    /// The assemblies the features in <paramref name="container"/> come from, or <see langword="null"/> when they cannot be
    /// read: CShells gives every shell container the feature descriptors of the catalog snapshot it was built from.
    /// </summary>
    private IReadOnlySet<Assembly>? FeatureAssemblies(IServiceProvider container, IShell? shell)
    {
        try
        {
            if (container.GetService<IReadOnlyCollection<ShellFeatureDescriptor>>() is { } descriptors)
                return descriptors.Select(descriptor => descriptor.StartupType?.Assembly).OfType<Assembly>().ToHashSet();
            _logger.LogWarning(
                "Shell generation {Shell} names no features, so every superseded package generation keeps counting for this host's " +
                "readability report until it is disposed.",
                shell?.Descriptor);
        }
        catch (Exception exception) when (!IsCritical(exception))
        {
            _logger.LogWarning(
                exception,
                "The features of shell generation {Shell} could not be read, so every superseded package generation keeps counting for " +
                "this host's readability report until it is disposed.",
                shell?.Descriptor);
        }

        return null;
    }

    /// <summary>
    /// The load contexts the next shell generation may compose from, as the host's CShells runtime feature catalog names
    /// them: every load context of a feature its current snapshot names, and of a replaced assembly it names. Before the
    /// catalog is initialized, or when it cannot be read, that is every load context a replaced assembly is in. A host
    /// without CShells builds no shell generation, so it pins nothing. A refresh that lifts the last of these pins is seen
    /// by committed notifications, or the custom-catalog watch <see cref="GetRetiredAsync"/> starts.
    /// </summary>
    private async ValueTask<HashSet<AssemblyLoadContext>> PinnedByFeatureCatalogAsync(IReadOnlySet<Assembly> replaced, CancellationToken cancellationToken)
    {
        if (Host?.GetService<IRuntimeFeatureCatalog>() is not { } catalog)
            return [];

        try
        {
            // A catalog that has never been refreshed is refreshed by the first build, from whatever Nuplane lists at that
            // moment, and one may be under way already; asking for its detailed snapshot now would refresh it here instead.
            if (!IsInitialized(catalog))
                return PackageContexts(replaced);

            var snapshot = await catalog.GetSnapshotAsync(cancellationToken);
            return PackageContexts(snapshot.FeatureDescriptors
                .Select(descriptor => descriptor.StartupType?.Assembly)
                .Concat(snapshot.Assemblies.Where(replaced.Contains)));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "CShells' runtime feature catalog could not be read, so every superseded package generation keeps counting for this host's " +
                "readability report: the next shell generation may be built from any of them.");
            return PackageContexts(replaced);
        }
    }

    private static bool IsInitialized(IRuntimeFeatureCatalog catalog) => SnapshotGeneration(catalog) != Uninitialized;

    /// <summary>The generation of the host's feature catalog's current snapshot, or <see langword="null"/> when there is no catalog, or it cannot be read.</summary>
    private long? FeatureCatalogGeneration()
    {
        try
        {
            return Host?.GetService<IRuntimeFeatureCatalog>() is { } catalog ? SnapshotGeneration(catalog) : null;
        }
        catch (Exception exception) when (exception is not ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>The generation of <paramref name="catalog"/>'s current snapshot, or <see cref="Uninitialized"/>, read without initializing it.</summary>
    private static long SnapshotGeneration(IRuntimeFeatureCatalog catalog)
    {
        try
        {
            return catalog.CurrentSnapshot.Generation;
        }
        catch (InvalidOperationException)
        {
            return Uninitialized;
        }
    }

    /// <summary>
    /// Watches the feature catalog, unless it is watched already, until its snapshot has moved from <paramref name="seen"/>,
    /// then has the republish loop evaluate, which watches it again while replacements still exist.
    /// </summary>
    private void WatchFeatureCatalog(long seen)
    {
        if (Volatile.Read(ref _stopped) == 0 && Volatile.Read(ref _commitNotificationsAvailable) == 0 && Interlocked.CompareExchange(ref _watchingCatalog, 1, 0) == 0)
            _ = Task.Run(() => WatchFeatureCatalogAsync(seen, _watchStop.Token));
    }

    private async Task WatchFeatureCatalogAsync(long seen, CancellationToken cancellationToken)
    {
        try
        {
            // A catalog that turns unreadable is watched until it can be read again, or the host is disposed.
            while (FeatureCatalogGeneration() is not { } current || current == seen)
                await Task.Delay(CatalogWatchInterval, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            // The host is gone, and its report with it.
            return;
        }
        finally
        {
            Volatile.Write(ref _watchingCatalog, 0);
        }

        RequestPublish();
    }

    private async Task ReleaseWhenDrainedAsync(Generation generation, IDrainOperation drain)
    {
        try
        {
            await drain.WaitAsync();
        }
        catch (Exception exception) when (!IsCritical(exception))
        {
            _logger.LogWarning(
                exception,
                "The drain of unleased shell generation {Shell} failed, so every package generation it composed from keeps counting for this host's readability report.",
                generation.Name);
            return;
        }

        Release(generation);
    }

    /// <summary>
    /// Stops a legacy <paramref name="generation"/> pinning after successful drain completion and queues reevaluation.
    /// </summary>
    private void Release(Generation generation)
    {
        var released = false;
        lock (_pinGate)
            released = _live.TryRemove(generation, out _);
        if (released)
            RequestPublish();
    }

    private void Select(BuildLease lease, RuntimeFeatureCatalogSnapshot snapshot)
    {
        IReadOnlySet<Assembly>? assemblies;
        try
        {
            assemblies = snapshot.FeatureDescriptors
                .Select(descriptor => descriptor.StartupType?.Assembly)
                .OfType<Assembly>()
                .ToHashSet();
        }
        catch (Exception exception) when (!IsCritical(exception))
        {
            _logger.LogWarning(exception, "The selected features of shell generation {Shell} could not be read, so every superseded package generation keeps counting until its build lease is released.", lease.Descriptor);
            assemblies = null;
        }
        lock (_pinGate)
        {
            if (!_buildLeases.Contains(lease))
                return;
            lease.Features = assemblies;
        }
        RequestPublish();
    }

    private async Task ObserveDrainFailureAsync(BuildLease lease, IDrainOperation drain)
    {
        try
        {
            await drain.WaitAsync();
        }
        catch (Exception exception) when (!IsCritical(exception))
        {
            // Diagnostic only: a failed drain never grants authority to release the build lease.
            _logger.LogWarning(exception, "The drain of shell generation {Shell} failed; its package generations remain governed by the confirmed build-lease teardown boundary.", lease.Descriptor);
        }
    }

    private void ProviderDisposed(BuildLease lease)
    {
        lock (_pinGate)
        {
            if (!_buildLeases.Remove(lease))
                return;
            if (_leasesByDescriptor.TryGetValue(lease.Descriptor, out var current) && ReferenceEquals(current, lease))
                _leasesByDescriptor.Remove(lease.Descriptor);
        }
        RequestPublish();
    }

    /// <summary>
    /// Asks the republish loop to evaluate, starting it unless it runs already: a request made while it publishes is taken
    /// up by its next evaluation, however many were made.
    /// </summary>
    private void RequestPublish()
    {
        if (Volatile.Read(ref _stopped) != 0)
            return;
        Volatile.Write(ref _publishRequested, 1);
        if (Volatile.Read(ref _stopped) == 0 && Interlocked.CompareExchange(ref _publishing, 1, 0) == 0)
            _ = Task.Run(PublishWhileRequestedAsync);
    }

    private async Task PublishWhileRequestedAsync()
    {
        do
        {
            while (Volatile.Read(ref _stopped) == 0 && Interlocked.Exchange(ref _publishRequested, 0) == 1)
                await PublishIfRetiredAsync();
            Volatile.Write(ref _publishing, 0);
        }
        // A request made after the last evaluation and before the loop stopped would otherwise wait for the next one.
        while (Volatile.Read(ref _stopped) == 0 && Volatile.Read(ref _publishRequested) == 1 && Interlocked.CompareExchange(ref _publishing, 1, 0) == 0);
    }

    /// <summary>
    /// Publishes after any retired-set change, restoring constraints on reintroduction as well as lifting retired ones.
    /// Noncritical failures are isolated; a later normal membership publish carries the current source evidence.
    /// </summary>
    private async Task PublishIfRetiredAsync()
    {
        try
        {
            var retired = await GetRetiredAsync();
            if (retired.SetEquals(_retiredWhenLastEvaluated))
                return;

            var newly = retired.Except(_retiredWhenLastEvaluated).ToArray();
            var reintroduced = _retiredWhenLastEvaluated.Except(retired).ToArray();
            _retiredWhenLastEvaluated = retired;
            if (Host?.GetService<IClusterMembership>() is not { } membership)
                return;

            if (newly.Length > 0)
                _logger.LogInformation(
                    "Nothing on this host can run {Assemblies} any more, so they no longer count for its readability report, which is published again.",
                    string.Join(", ", newly.Select(assembly => assembly.GetName().ToString())));
            if (reintroduced.Length > 0)
                _logger.LogInformation(
                    "The host's catalog again names {Assemblies}, so the readability constraints are restored in its report.",
                    string.Join(", ", reintroduced.Select(assembly => assembly.GetName().ToString())));
            await membership.PublishReportAsync();
        }
        catch (ObjectDisposedException)
        {
            // The host is gone, and its report with it.
        }
        catch (Exception exception) when (!IsCritical(exception))
        {
            // The next publish - an activation, a changed write version, a heartbeat - carries the same report.
            _logger.LogWarning(exception, "The readability report could not be published again after a superseded package generation retired.");
        }
    }

    /// <summary>
    /// Whether <paramref name="exception"/> leaves the process unfit to go on. Every other failure is caught where it happens
    /// and settled conservatively: a release it concerns keeps counting, because a failure is never evidence that nothing can
    /// run that release, and a report it could not publish goes out with the next publish.
    /// </summary>
    private static bool IsCritical(Exception exception) =>
        exception is OutOfMemoryException or StackOverflowException or AccessViolationException or InvalidProgramException;

    private static AssemblyLoadContext ContextOf(Assembly assembly) => AssemblyLoadContext.GetLoadContext(assembly) ?? AssemblyLoadContext.Default;

    /// <summary>The load contexts of <paramref name="assemblies"/> other than the default one, which no package runtime replaces.</summary>
    private static HashSet<AssemblyLoadContext> PackageContexts(IEnumerable<Assembly?> assemblies) =>
        assemblies.OfType<Assembly>().Select(ContextOf).Where(context => context != AssemblyLoadContext.Default).ToHashSet();

    private static string NameOf(Assembly assembly) => assembly.GetName().Name ?? assembly.FullName ?? "";

    private sealed class BuildLease(NuplanePackageGenerations owner, ShellDescriptor descriptor) : IShellGenerationBuildLease
    {
        private int _observingDrain;

        public ShellDescriptor Descriptor { get; } = descriptor;

        public IReadOnlySet<Assembly>? Features { get; set; }

        public bool ObserveDrain() => Interlocked.Exchange(ref _observingDrain, 1) == 0;

        public ValueTask OnSnapshotSelectedAsync(RuntimeFeatureCatalogSnapshot snapshot, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            cancellationToken.ThrowIfCancellationRequested();
            owner.Select(this, snapshot);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            owner.ProviderDisposed(this);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// A legacy shell generation, pinned from its first lifecycle notification until successful drain completion.
    /// </summary>
    private sealed class Generation(IShell shell, IReadOnlySet<Assembly>? features)
    {
        private readonly Lock _gate = new();
        private IDrainOperation? _drain;

        /// <summary>
        /// The assemblies its features come from, whose every load context but the default one it pins, or
        /// <see langword="null"/> when they could not be read, which pins every load context a replaced assembly is in.
        /// </summary>
        public IReadOnlySet<Assembly>? Features { get; private set; } = features;

        public void SetFeatures(IReadOnlySet<Assembly>? value) => Features = value;

        public string Name => shell.Descriptor.ToString();

        /// <summary>Attaches the first drain that can release this legacy pin on successful completion.</summary>
        public bool Attach(IDrainOperation drain)
        {
            lock (_gate)
            {
                if (_drain is not null)
                    return false;
                _drain = drain;
                return true;
            }
        }

    }
}
