using System.Reflection;
using CShells.Features;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.Core.Exceptions;
using Elsa.Tasks.Core;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Runtime.Tasks;

/// <summary>
/// Startup pass (FR-004b, research D8 revised) that registers — into the runtime
/// <see cref="IWellKnownTypeRegistry"/> under the shared <see cref="TypeAliasConvention"/> — the CLR types
/// reachable from the loaded activities:
/// <list type="bullet">
///   <item>the <see cref="IActivity"/> implementation <b>itself</b>, so the CLR activity constructor resolves
///   the descriptor's stable alias back to the real activity type at construction time (no
///   <c>Assembly.Load(name, version)</c>);</item>
///   <item>each activity input/output <b>element type</b>, so a complex- or enum-typed input resolves to its
///   real CLR type at compile time instead of falling back to <c>object</c>.</item>
/// </list>
/// In both cases the reflection-only CLR scanner emits <c>CanonicalAlias(type)</c> and this pass registers
/// that same alias↔type pair, so the two sides cannot drift.
/// </summary>
/// <remarks>
/// <para>
/// Source of types, in this order: (a) the assemblies of the features this shell was composed from, which its own
/// <see cref="ShellFeatureDescriptor"/>s name, so they name the release of each package the shell composes; (b) the
/// assemblies surfaced by every registered <see cref="IFeatureAssemblyProvider"/>, the same authoritative source the modular
/// feature catalog uses, which covers dynamically-loaded package activities that are not guaranteed to appear in
/// <see cref="AppDomain.CurrentDomain"/>; (c) the runtime-loaded assemblies (framework activities, composed into the host),
/// less every other loaded assembly with the simple name of a shell feature assembly: an earlier release of that package,
/// which the shell does not compose. This task re-runs on every shell (re)build (it is an <see cref="IStartupTask"/>,
/// replayed by the shell-tasks initializer), so when an activity package becomes available both the activity type and its
/// I/O element types are picked up the next time the shell composes.
/// </para>
/// <para>
/// The order and the exclusion decide which class an alias resolves to after a package is upgraded in place. Its previous
/// release stays loaded (a host-integrated load context is never unloaded, a collectible one lingers until it is collected),
/// so the AppDomain can hold two assemblies that declare the same activity type, and so the same alias, and the first one
/// registered wins. The shell's own features come first so that the release the shell composes claims each alias; a
/// provider resolved inside a shell container cannot be relied on for that (Nuplane's catalog copied into a shell has loaded
/// nothing). The exclusion keeps the earlier release from claiming an alias the new release no longer declares, such as an
/// activity or an I/O type it removed. Registering the earlier class made the new shell construct it: it ignored every input
/// the new release added, or failed to construct because its services are registered under the new release's types.
/// </para>
/// <para>
/// One CLR type is registered per alias, and the alias carries no version, so every node bound to an activity runs the
/// class of the release the shell composes, the newest one after an upgrade, whichever catalog version the node pins
/// (spec 004 leaves loading several versions of one CLR type out of scope). A release must therefore stay input-compatible
/// with the versions workflows are pinned to.
/// </para>
/// <para>
/// Idempotent and fail-fast-tolerant: <see cref="IWellKnownTypeRegistry.RegisterType"/> throws on a genuine
/// duplicate-alias conflict, but the identical (type, alias) pair is a no-op. This pass only registers a type
/// whose canonical alias is not already mapped to a different type, so re-running it (or overlapping with the
/// primitive seed, or the same assembly appearing in several sources) never throws.
/// </para>
/// </remarks>
public sealed class RegisterActivityTypesStartupTask : IStartupTask
{
    private readonly IWellKnownTypeRegistry _wellKnownTypeRegistry;
    private readonly IReadOnlyCollection<IFeatureAssemblyProvider> _assemblyProviders;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RegisterActivityTypesStartupTask> _logger;
    private readonly Func<IEnumerable<Assembly>> _baseAssembliesFactory;

    public RegisterActivityTypesStartupTask(
        IWellKnownTypeRegistry wellKnownTypeRegistry,
        IEnumerable<IFeatureAssemblyProvider> assemblyProviders,
        IServiceProvider serviceProvider,
        ILogger<RegisterActivityTypesStartupTask> logger)
        : this(wellKnownTypeRegistry, assemblyProviders, serviceProvider, logger, static () => AppDomain.CurrentDomain.GetAssemblies())
    {
    }

    // Test seam: lets a unit test substitute the baseline (runtime-loaded) assembly source so the
    // IFeatureAssemblyProvider path can be exercised in isolation from the host AppDomain (§2.23.3).
    public RegisterActivityTypesStartupTask(
        IWellKnownTypeRegistry wellKnownTypeRegistry,
        IEnumerable<IFeatureAssemblyProvider> assemblyProviders,
        IServiceProvider serviceProvider,
        ILogger<RegisterActivityTypesStartupTask> logger,
        Func<IEnumerable<Assembly>> baseAssembliesFactory)
    {
        _wellKnownTypeRegistry = wellKnownTypeRegistry;
        _assemblyProviders = assemblyProviders as IReadOnlyCollection<IFeatureAssemblyProvider> ?? assemblyProviders.ToArray();
        _serviceProvider = serviceProvider;
        _logger = logger;
        _baseAssembliesFactory = baseAssembliesFactory;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var assemblies = await CollectAssembliesAsync(cancellationToken);

        foreach (var type in EnumerateRegistrableTypes(assemblies))
            TryRegister(type);
    }

    // The shell's own feature assemblies first, then the providers', then the baseline less every earlier release of a
    // shell feature assembly, each assembly once. The order and the exclusion are load-bearing: see the remarks.
    private async Task<IReadOnlyCollection<Assembly>> CollectAssembliesAsync(CancellationToken cancellationToken)
    {
        var assemblies = new List<Assembly>();
        var seen = new HashSet<Assembly>();

        var shellFeatureAssemblies = (_serviceProvider.GetService(typeof(IReadOnlyCollection<ShellFeatureDescriptor>)) as IReadOnlyCollection<ShellFeatureDescriptor> ?? [])
            .Select(descriptor => descriptor.StartupType?.Assembly)
            .OfType<Assembly>()
            .ToHashSet();
        var shellFeatureAssemblyNames = shellFeatureAssemblies
            .Select(assembly => assembly.GetName().Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var assembly in shellFeatureAssemblies)
            if (seen.Add(assembly))
                assemblies.Add(assembly);

        foreach (var provider in _assemblyProviders)
        {
            IEnumerable<Assembly> providerAssemblies;
            try
            {
                providerAssemblies = await provider.GetAssembliesAsync(_serviceProvider, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A misbehaving provider must not abort host startup; the framework activities still register.
                _logger.LogWarning(ex, "Skipping feature assembly provider '{Provider}': assemblies could not be enumerated.", provider.GetType().FullName);
                continue;
            }

            foreach (var assembly in providerAssemblies)
                if (seen.Add(assembly))
                    assemblies.Add(assembly);
        }

        // A loaded assembly named like one of the shell's feature assemblies, but not that assembly, is a release the shell
        // does not compose: none of its types may claim an alias.
        foreach (var assembly in _baseAssembliesFactory())
            if (!IsEarlierRelease(assembly) && seen.Add(assembly))
                assemblies.Add(assembly);

        return assemblies;

        bool IsEarlierRelease(Assembly assembly) =>
            !shellFeatureAssemblies.Contains(assembly) && shellFeatureAssemblyNames.Contains(assembly.GetName().Name ?? "");
    }

    private IEnumerable<Type> EnumerateRegistrableTypes(IEnumerable<Assembly> assemblies)
    {
        foreach (var activityType in EnumerateActivityTypes(assemblies))
        {
            // The activity CLR type itself — the CLR construction descriptor resolves its stable alias here.
            yield return activityType;

            PropertyInfo[] properties;
            try
            {
                properties = activityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            }
            catch (Exception ex) when (IsRecoverableReflectionException(ex))
            {
                _logger.LogDebug(ex, "Skipping activity '{Activity}': properties could not be reflected.", activityType.FullName);
                continue;
            }

            foreach (var property in properties.Where(property => ActivityInputPropertyResolver.FindAttribute(property) is not null))
                yield return TypeReferenceFactory.Decompose(property.PropertyType).ElementType;

            foreach (var resultType in GetActivityResultTypes(activityType))
            {
                yield return TypeReferenceFactory.Decompose(resultType).ElementType;
                foreach (var property in resultType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                             .Where(property => property.GetCustomAttribute<OutputAttribute>(inherit: true) is not null))
                    yield return TypeReferenceFactory.Decompose(property.PropertyType).ElementType;
            }
        }
    }

    private IEnumerable<Type> EnumerateActivityTypes(IEnumerable<Assembly> assemblies)
    {
        foreach (var assembly in assemblies)
        {
            if (assembly.IsDynamic)
                continue;

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).ToArray()!;
            }
            catch (Exception ex) when (IsRecoverableReflectionException(ex))
            {
                _logger.LogDebug(ex, "Skipping assembly '{Assembly}': types could not be reflected.", assembly.FullName);
                continue;
            }

            foreach (var type in types)
            {
                if (IsActivityType(type))
                    yield return type;
            }
        }
    }

    private void TryRegister(Type elementType)
    {
        // Open generic parameters and similar exotics have no stable FullName-based identity; skip them.
        if (elementType.IsGenericParameter || elementType.ContainsGenericParameters)
            return;

        // A Nullable<T> element (e.g. TimeSpan?/long?) shares its underlying value type's identity for alias
        // purposes. Register the underlying T so it resolves to its reserved/dotted canonical alias; the registry
        // then derives the "T?" nullable companion. Registering Nullable<T> directly would yield a bare "<Alias>?"
        // (e.g. "TimeSpan?") that the reserved-namespace guard rejects, leaving the nullable element unregistered.
        elementType = Nullable.GetUnderlyingType(elementType) ?? elementType;

        var alias = TypeAliasConvention.CanonicalAlias(elementType);

        // Already mapped to this exact type (e.g. a primitive seeded earlier, or a re-run): nothing to do.
        if (_wellKnownTypeRegistry.TryGetType(alias, out _))
            return;

        // The same CLR type already registered under a (curated) alias: leave that alias as the canonical one.
        if (_wellKnownTypeRegistry.TryGetAlias(elementType, out _))
            return;

        try
        {
            _wellKnownTypeRegistry.RegisterType(elementType, alias);
        }
        catch (DuplicateTypeAliasException ex)
        {
            // A race with another contributor registered the same alias/type first; tolerate it.
            _logger.LogDebug(ex, "Activity type '{Type}' alias '{Alias}' was already registered.", elementType.FullName, alias);
        }
        catch (ReservedAliasNamespaceException ex)
        {
            // Should not happen: convention only yields bare aliases for reserved primitives. Log and skip.
            _logger.LogWarning(ex, "Activity type '{Type}' produced reserved bare alias '{Alias}'; skipping.", elementType.FullName, alias);
        }
    }

    private static bool IsActivityType(Type type) =>
        type is { IsClass: true, IsAbstract: false } && typeof(IActivity).IsAssignableFrom(type);

    private static IEnumerable<Type> GetActivityResultTypes(Type activityType) =>
        activityType.GetInterfaces()
            .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IActivityResult<>))
            .Select(candidate => candidate.GetGenericArguments()[0])
            .Distinct();

    private static bool IsRecoverableReflectionException(Exception exception) =>
        exception is FileNotFoundException or FileLoadException or TypeLoadException or BadImageFormatException or ReflectionTypeLoadException;
}
