using System.Collections.Concurrent;
using System.Reflection;
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
/// <b>Retired</b> is replaced and no longer runnable. Every shell generation CShells has not yet disposed - an active
/// one, including one whose reload failed, or one still draining - keeps counting every replaced assembly in any load
/// context it composes a replaced feature from, so while an old and a new generation are both live the report still
/// intersects them. When the last such generation is disposed, this publishes the host's report again, so a module
/// gate waiting on the old generation evaluates at once instead of never: nothing else republishes a report whose
/// declarations did not change.
/// </para>
/// <para>
/// One instance serves the whole host: it is registered by instance, so every shell container CShells builds from the
/// host's registrations shares it, and it reads Nuplane's catalog and publishes through the host's own container, bound
/// by <see cref="BindTo"/>. Resolved inside a shell, Nuplane's loader would be a fresh copy that has loaded nothing.
/// </para>
/// </remarks>
public sealed class NuplanePackageGenerations : ISupersededAssemblySource, IShellLifecycleSubscriber
{
    /// <summary>
    /// The assembly that defines every load context Nuplane gives a package, a graph's or a single package's
    /// (<c>PackageAssemblyLoadContext</c> and the internal graph contexts). Named rather than referenced, so composing
    /// readability takes Nuplane's abstractions and not its runtime; a rename leaves every context counting.
    /// </summary>
    public const string NuplaneLoadingAssembly = "Nuplane.Loading";

    /// <summary>
    /// Every shell generation not yet disposed, with the assemblies its features come from; <see langword="null"/> when
    /// they could not be read, which keeps every replaced assembly counting until the generation is disposed.
    /// </summary>
    private readonly ConcurrentDictionary<IShell, IReadOnlyList<Assembly>?> _live = new(ReferenceEqualityComparer.Instance);

    private volatile IServiceProvider? _host;
    private ILogger _logger = NullLogger.Instance;

    /// <summary>
    /// Binds this instance to the host's own container and returns it as the lifecycle subscriber CShells' registry
    /// subscribes. CShells builds its subscribers from the host container alone and never copies them into a shell, so
    /// this runs once, with the host's container, before the first shell activates.
    /// </summary>
    public IShellLifecycleSubscriber BindTo(IServiceProvider host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _logger = host.GetService<ILoggerFactory>()?.CreateLogger<NuplanePackageGenerations>() ?? NullLogger<NuplanePackageGenerations>.Instance;
        _host = host;
        return this;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlySet<Assembly>> GetReplacedAsync(CancellationToken cancellationToken = default)
    {
        if (_host?.GetService<IPackageAssemblyCatalog>() is not { } catalog)
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

        var pinned = new HashSet<AssemblyLoadContext?>();
        foreach (var features in _live.Values)
            pinned.UnionWith((features ?? [.. replaced]).Where(replaced.Contains).Select(AssemblyLoadContext.GetLoadContext));
        return replaced.Where(assembly => !pinned.Contains(AssemblyLoadContext.GetLoadContext(assembly))).ToHashSet();
    }

    /// <inheritdoc />
    public async Task OnStateChangedAsync(
        IShell shell,
        ShellLifecycleState previous,
        ShellLifecycleState current,
        CancellationToken cancellationToken = default)
    {
        if (current != ShellLifecycleState.Disposed)
        {
            _live.GetOrAdd(shell, FeatureAssemblies);
            return;
        }

        if (_live.TryRemove(shell, out var features))
            await PublishIfItRetiredAsync(shell, features, cancellationToken);
    }

    /// <summary>
    /// Publishes the host's report again when <paramref name="shell"/>, just disposed, was what kept a replaced generation
    /// counting, so the report stops narrowing on it now rather than at whatever publish happens to come next.
    /// </summary>
    private async Task PublishIfItRetiredAsync(IShell shell, IReadOnlyList<Assembly>? features, CancellationToken cancellationToken)
    {
        try
        {
            var retired = await GetRetiredAsync(cancellationToken);
            var composed = features?.Select(AssemblyLoadContext.GetLoadContext).ToHashSet();
            var released = retired.Where(assembly => composed is null || composed.Contains(AssemblyLoadContext.GetLoadContext(assembly))).ToArray();
            if (released.Length == 0 || _host?.GetService<IClusterMembership>() is not { } membership)
                return;

            _logger.LogInformation(
                "Shell generation {Shell} was the last to run a superseded package generation; {Assemblies} no longer count for this " +
                "host's readability report, which is published again.",
                shell.Descriptor,
                string.Join(", ", released.Select(assembly => assembly.GetName().ToString())));
            await membership.PublishReportAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The next publish - an activation, a changed write version, a heartbeat - carries the same report.
            _logger.LogWarning(exception, "The readability report could not be published again after shell generation {Shell} was disposed.", shell.Descriptor);
        }
    }

    /// <summary>
    /// The assemblies <paramref name="shell"/>'s features come from, as the feature descriptors CShells gives every shell
    /// container name them, or <see langword="null"/> when they cannot be read.
    /// </summary>
    private IReadOnlyList<Assembly>? FeatureAssemblies(IShell shell)
    {
        try
        {
            if (shell.ServiceProvider.GetService<IReadOnlyCollection<ShellFeatureDescriptor>>() is { } descriptors)
                return [.. descriptors.Select(descriptor => descriptor.StartupType?.Assembly).OfType<Assembly>().Distinct()];
        }
        catch (ObjectDisposedException)
        {
            // Torn down before it could be read; treated as unreadable below.
        }

        _logger.LogWarning(
            "The features of shell generation {Shell} could not be read, so every superseded package generation keeps counting for this " +
            "host's readability report until it is disposed.",
            shell.Descriptor);
        return null;
    }

    private static string NameOf(Assembly assembly) => assembly.GetName().Name ?? assembly.FullName ?? "";
}
