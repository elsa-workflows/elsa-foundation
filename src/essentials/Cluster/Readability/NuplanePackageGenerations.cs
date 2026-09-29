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
/// <b>Replaced</b> is Nuplane's positive evidence, read afresh from <see cref="IPackageAssemblyCatalog"/> on every call:
/// an assembly in a load context Nuplane created is replaced when the catalog lists a loaded assembly of the same name
/// for the active package set and does not list this one. Nothing else is: not the default context, not a context
/// Nuplane did not create, not an assembly whose name the catalog lists nowhere - which is what an old generation looks
/// like while its successor is still loading, or after its successor failed to load. Each of those keeps counting.
/// </para>
/// <para>
/// <b>Retired</b> is replaced and no longer runnable, which takes positive evidence from both places code could run it:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>A shell generation that exists.</b> Every shell container CShells builds copies this host's registrations, so each
/// one constructs its own tracking initializer before its first initializer runs, and that is when this starts counting
/// the generation: before any of its code has read a row. It pins every load context, other than the default one, that
/// a feature in its container comes from - every feature of the catalog snapshot it was built from, enabled or not,
/// since that is what CShells gives every shell container - so a replaced assembly beside an unchanged feature in the
/// same load context keeps counting, as that feature binds it. A generation whose features cannot be read pins every
/// replaced assembly. It stops pinning only once its container has finished disposing: when CShells drained it, once
/// that drain completes, which it does only after the shell's provider has been disposed; otherwise when the container
/// disposes the tracking initializer, which it does after everything the container created later.
/// </description></item>
/// <item><description>
/// <b>The shell generation not built yet.</b> CShells builds the next generation from its runtime feature catalog's
/// current snapshot, which a host refreshes only when it chooses to: <c>Elsa.Foundation.Host</c> skips the refresh after a
/// reconcile while no shell is active. So every load context of a feature the snapshot names, and of a replaced assembly
/// it names, is pinned too, and so is every replaced assembly while the catalog has not been initialized, or cannot be
/// read, since the first build may be reading any of them.
/// </description></item>
/// </list>
/// <para>
/// When a generation stops pinning and that retires something, this publishes the host's report again, so a module gate
/// waiting on the old generation evaluates at once instead of never: nothing else republishes a report whose
/// declarations did not change.
/// </para>
/// <para>
/// One instance serves the whole host: it is registered by instance, so every shell container CShells builds from the
/// host's registrations shares it, and it reads Nuplane's catalog, CShells' feature catalog and the membership it
/// publishes through from the host's own container, bound by <see cref="BindTo"/>. Resolved inside a shell, Nuplane's
/// loader would be a fresh copy that has loaded nothing.
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

    /// <summary>Every shell generation that can still run code, until its container has finished disposing.</summary>
    private readonly ConcurrentDictionary<Generation, byte> _live = new();

    /// <summary>The generation each shell CShells has shown this host belongs to, so a lifecycle notification finds it.</summary>
    private readonly ConditionalWeakTable<IShell, Generation> _shells = new();

    private IServiceProvider? _host;
    private ILogger _logger = NullLogger.Instance;

    private IServiceProvider? Host => Volatile.Read(ref _host);

    /// <summary>
    /// Binds this instance to the host's own container and returns it as the lifecycle subscriber CShells' registry
    /// subscribes. CShells resolves its subscribers from the host container alone, before it builds any shell, and never
    /// copies them into one, so the first binding is the host's; any later one, from whatever container, changes nothing.
    /// </summary>
    public IShellLifecycleSubscriber BindTo(IServiceProvider host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (Interlocked.CompareExchange(ref _host, host, null) is null)
            _logger = host.GetService<ILoggerFactory>()?.CreateLogger<NuplanePackageGenerations>() ?? NullLogger<NuplanePackageGenerations>.Instance;
        return this;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlySet<Assembly>> GetReplacedAsync(CancellationToken cancellationToken = default)
    {
        if (Host?.GetService<IPackageAssemblyCatalog>() is not { } catalog)
            return LoadedAssemblies.NoneSuperseded;

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
        return AssemblyLoadContext.All
            .Where(context => context != AssemblyLoadContext.Default && context.GetType().Assembly.GetName().Name == NuplaneLoadingAssembly)
            .SelectMany(context => context.Assemblies)
            .Where(assembly => !current.Contains(assembly) && currentNames.Contains(NameOf(assembly)))
            .ToHashSet();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlySet<Assembly>> GetRetiredAsync(CancellationToken cancellationToken = default)
    {
        var replaced = await GetReplacedAsync(cancellationToken);
        if (replaced.Count == 0)
            return replaced;

        var pinned = await PinnedByFeatureCatalogAsync(replaced, cancellationToken);
        foreach (var generation in _live.Keys)
            pinned.UnionWith(generation.Contexts ?? PackageContexts(replaced));
        return replaced.Where(assembly => !pinned.Contains(ContextOf(assembly))).ToHashSet();
    }

    /// <summary>
    /// Attaches the drain CShells runs a generation down with, so the generation stops pinning only once that drain has
    /// completed. Also tracks a shell this host never saw initialize, which a CShells shell container always does unless
    /// its copy of the tracking initializer was removed; such a generation is released only by a drain that completes.
    /// </summary>
    public Task OnStateChangedAsync(
        IShell shell,
        ShellLifecycleState previous,
        ShellLifecycleState current,
        CancellationToken cancellationToken = default)
    {
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
            generation = Track(shell, shell.ServiceProvider, leased: false);
        }

        // Drained is awaited by the drain before it disposes the shell; Draining is not, and Drained is skipped when the host's
        // shutdown disposes a shell whose drain overran, so both attach.
        if (current is ShellLifecycleState.Draining or ShellLifecycleState.Drained && shell.Drain is { } drain && generation.Attach(drain))
            _ = ReleaseWhenDrainedAsync(generation, drain);
        return Task.CompletedTask;
    }

    /// <summary>
    /// What every shell container constructs among its initializers, before any of them runs: it counts the generation the
    /// container belongs to until the container disposes it. A container that is not a shell's gets an initializer that
    /// tracks nothing.
    /// </summary>
    internal IShellInitializer Track(IServiceProvider container)
    {
        ArgumentNullException.ThrowIfNull(container);
        IShell? shell;
        try
        {
            shell = container.GetService<IShell>();
            if (shell is null)
                return NotAShell.Instance;
        }
        catch (Exception exception)
        {
            // IShell is registered, so this is a shell container; which one cannot be told, so it is tracked as one whose
            // features cannot be read either, until its container disposes it.
            _logger.LogWarning(exception, "A shell generation could not be identified, so every superseded package generation keeps counting for this host's readability report until its container is disposed.");
            return Track(null, null, leased: true);
        }

        return Track(shell, container, leased: true);
    }

    private Generation Track(IShell? shell, IServiceProvider? container, bool leased)
    {
        var generation = new Generation(this, shell, container is null ? null : FeatureContexts(container, shell), leased);
        if (shell is not null)
            _shells.AddOrUpdate(shell, generation);
        _live.TryAdd(generation, 0);
        return generation;
    }

    /// <summary>
    /// Every load context, other than the default one, of a feature in <paramref name="container"/>, or
    /// <see langword="null"/> when they cannot be read: CShells gives every shell container the feature descriptors of
    /// the catalog snapshot it was built from.
    /// </summary>
    private IReadOnlySet<AssemblyLoadContext>? FeatureContexts(IServiceProvider container, IShell? shell)
    {
        try
        {
            if (container.GetService<IReadOnlyCollection<ShellFeatureDescriptor>>() is { } descriptors)
                return PackageContexts(descriptors.Select(descriptor => descriptor.StartupType?.Assembly));
            _logger.LogWarning(
                "Shell generation {Shell} names no features, so every superseded package generation keeps counting for this host's " +
                "readability report until it is disposed.",
                shell?.Descriptor);
        }
        catch (Exception exception)
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
    /// without CShells builds no shell generation, so it pins nothing.
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

    private static bool IsInitialized(IRuntimeFeatureCatalog catalog)
    {
        try
        {
            _ = catalog.CurrentSnapshot;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private async Task ReleaseWhenDrainedAsync(Generation generation, IDrainOperation drain)
    {
        try
        {
            await drain.WaitAsync();
        }
        catch (Exception exception)
        {
            // The drain completes only after the shell's DisposeAsync returns, and faults when it or the drain threw; which
            // one cannot be told, so the provider may be only partly disposed and still running the generation.
            _logger.LogWarning(
                exception,
                "The drain of shell generation {Shell} failed, so its provider may not have been disposed: every package generation it " +
                "composed from keeps counting for this host's readability report until the host restarts.",
                generation.Name);
            return;
        }

        await ReleaseAsync(generation);
    }

    /// <summary>
    /// Stops <paramref name="generation"/> pinning and publishes the host's report again when that retired something, so
    /// the report stops narrowing on it now rather than at whatever publish happens to come next. Never throws: it runs
    /// from a container's disposal and from a drain's completion.
    /// </summary>
    private async Task ReleaseAsync(Generation generation)
    {
        if (!_live.TryRemove(generation, out _))
            return;

        try
        {
            var retired = await GetRetiredAsync();
            var released = retired.Where(assembly => generation.Contexts?.Contains(ContextOf(assembly)) ?? true).ToArray();
            if (released.Length == 0 || Host?.GetService<IClusterMembership>() is not { } membership)
                return;

            _logger.LogInformation(
                "Shell generation {Shell} was the last to run a superseded package generation; {Assemblies} no longer count for this " +
                "host's readability report, which is published again.",
                generation.Name,
                string.Join(", ", released.Select(assembly => assembly.GetName().ToString())));
            await membership.PublishReportAsync();
        }
        catch (Exception exception)
        {
            // The next publish - an activation, a changed write version, a heartbeat - carries the same report.
            _logger.LogWarning(exception, "The readability report could not be published again after shell generation {Shell} was disposed.", generation.Name);
        }
    }

    private static AssemblyLoadContext ContextOf(Assembly assembly) => AssemblyLoadContext.GetLoadContext(assembly) ?? AssemblyLoadContext.Default;

    /// <summary>The load contexts of <paramref name="assemblies"/> other than the default one, which no package runtime replaces.</summary>
    private static HashSet<AssemblyLoadContext> PackageContexts(IEnumerable<Assembly?> assemblies) =>
        assemblies.OfType<Assembly>().Select(ContextOf).Where(context => context != AssemblyLoadContext.Default).ToHashSet();

    private static string NameOf(Assembly assembly) => assembly.GetName().Name ?? assembly.FullName ?? "";

    /// <summary>
    /// One shell generation, from before its first initializer runs until its container has finished disposing. Its
    /// container creates it as a singleton when CShells resolves the shell's initializers, before any of them runs, and so
    /// disposes it after everything it created since; a drain, when the generation has one, completes only after the whole
    /// provider has been disposed.
    /// </summary>
    private sealed class Generation(NuplanePackageGenerations owner, IShell? shell, IReadOnlySet<AssemblyLoadContext>? contexts, bool leased)
        : IShellInitializer, IAsyncDisposable, IDisposable
    {
        private readonly Lock _gate = new();
        private IDrainOperation? _drain;
        private bool _disposed;

        /// <summary>The load contexts it pins, or <see langword="null"/> for every one a replaced assembly is in.</summary>
        public IReadOnlySet<AssemblyLoadContext>? Contexts => contexts;

        public string Name => shell?.Descriptor.ToString() ?? "(unidentified)";

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <summary>Attaches the drain that releases it, unless one already is or its container already released it.</summary>
        public bool Attach(IDrainOperation drain)
        {
            lock (_gate)
            {
                if (_drain is not null || _disposed)
                    return false;
                _drain = drain;
                return true;
            }
        }

        public ValueTask DisposeAsync() => ReleasedByDisposal() ? new(owner.ReleaseAsync(this)) : ValueTask.CompletedTask;

        public void Dispose()
        {
            if (ReleasedByDisposal())
                _ = owner.ReleaseAsync(this);
        }

        /// <summary>
        /// Whether its container's disposal releases it: only when no drain is attached, since a drain completes after the
        /// whole provider has been disposed, and only when the container is what tracked it.
        /// </summary>
        private bool ReleasedByDisposal()
        {
            lock (_gate)
            {
                if (_disposed)
                    return false;
                _disposed = true;
                return leased && _drain is null;
            }
        }
    }

    /// <summary>What a container that is not a shell's constructs: nothing to track.</summary>
    private sealed class NotAShell : IShellInitializer
    {
        public static readonly NotAShell Instance = new();

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
