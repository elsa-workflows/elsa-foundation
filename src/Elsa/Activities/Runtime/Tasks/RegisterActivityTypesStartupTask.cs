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
/// Source of types: the union of (a) the runtime-loaded assemblies (framework activities — composed into the
/// host) and (b) the assemblies surfaced by every registered <see cref="IFeatureAssemblyProvider"/>. The
/// provider set is the same authoritative source the modular feature catalog uses, so it covers
/// dynamically-loaded / externally-uploaded extension-builder activities whose assemblies are loaded into the
/// runtime on a shell (re)load but are not guaranteed to appear in <see cref="AppDomain.CurrentDomain"/>. This
/// task re-runs on every shell (re)build (it is an <see cref="IStartupTask"/>, replayed by the shell-tasks
/// initializer), so when an extension-builder activity package becomes available both the activity type and its
/// I/O element types are picked up the next time the shell composes.
/// </para>
/// <para>
/// Idempotent and fail-fast-tolerant: <see cref="IWellKnownTypeRegistry.RegisterType"/> throws on a genuine
/// duplicate-alias conflict, but the identical (type, alias) pair is a no-op. This pass only registers a type
/// whose canonical alias is not already mapped to a different type, so re-running it (or overlapping with the
/// primitive seed, or the same assembly appearing in both sources) never throws.
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

    // Baseline (runtime-loaded) assemblies unioned with the assemblies surfaced by every registered
    // IFeatureAssemblyProvider — the modular host's package/extension-builder assemblies. De-duplicated by
    // assembly identity so an assembly present in both sources is scanned once; the registration itself is
    // idempotent regardless.
    private async Task<IReadOnlyCollection<Assembly>> CollectAssembliesAsync(CancellationToken cancellationToken)
    {
        var assemblies = new HashSet<Assembly>(_baseAssembliesFactory());

        foreach (var provider in _assemblyProviders)
        {
            IEnumerable<Assembly> providerAssemblies;
            try
            {
                providerAssemblies = await provider.GetAssembliesAsync(_serviceProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A misbehaving provider must not abort host startup; the framework activities still register.
                _logger.LogWarning(ex, "Skipping feature assembly provider '{Provider}': assemblies could not be enumerated.", provider.GetType().FullName);
                continue;
            }

            foreach (var assembly in providerAssemblies)
                assemblies.Add(assembly);
        }

        return assemblies;
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
