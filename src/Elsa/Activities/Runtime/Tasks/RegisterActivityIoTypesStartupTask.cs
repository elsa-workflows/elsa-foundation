using System.Reflection;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.Core.Exceptions;
using Elsa.Tasks.Core;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Runtime.Tasks;

/// <summary>
/// Startup pass (FR-004b, research D8 revised) that registers the CLR types reachable through framework
/// activity inputs/outputs into the runtime <see cref="IWellKnownTypeRegistry"/> under the shared
/// <see cref="TypeAliasConvention"/>. This is what makes a complex- or enum-typed activity input resolve to
/// its real CLR type at compile time instead of falling back to <c>object</c>: the reflection-only CLR scanner
/// emits <c>CanonicalAlias(type)</c> for each input/output element type, and this pass registers that same
/// alias↔type pair so the alias resolves.
/// </summary>
/// <remarks>
/// <para>
/// Source of types: the RUNTIME-loaded activity types — every <see cref="IActivity"/> implementation in the
/// loaded assemblies. Framework activities are loaded because their features are composed into the host, so this
/// covers them. Externally-uploaded / dynamic extension-builder activities whose assemblies are not loaded into
/// the runtime are a documented edge: their input/output types are not reached here and may resolve to
/// <c>object</c> until their assemblies are loaded.
/// </para>
/// <para>
/// Idempotent and fail-fast-tolerant: <see cref="IWellKnownTypeRegistry.RegisterType"/> throws on a genuine
/// duplicate-alias conflict, but the identical (type, alias) pair is a no-op. This pass only registers a type
/// whose canonical alias is not already mapped to a different type, so re-running it (or overlapping with the
/// primitive seed) never throws.
/// </para>
/// </remarks>
public sealed class RegisterActivityIoTypesStartupTask(
    IWellKnownTypeRegistry wellKnownTypeRegistry,
    ILogger<RegisterActivityIoTypesStartupTask> logger)
    : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        foreach (var elementType in EnumerateActivityIoElementTypes())
            TryRegister(elementType);

        return Task.CompletedTask;
    }

    private IEnumerable<Type> EnumerateActivityIoElementTypes()
    {
        foreach (var activityType in EnumerateActivityTypes())
        {
            PropertyInfo[] properties;
            try
            {
                properties = activityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            }
            catch (Exception ex) when (IsRecoverableReflectionException(ex))
            {
                logger.LogDebug(ex, "Skipping activity '{Activity}': properties could not be reflected.", activityType.FullName);
                continue;
            }

            foreach (var property in properties)
            {
                if (!IsArgumentProperty(property.PropertyType))
                    continue;

                var valueType = GetArgumentValueType(property.PropertyType);
                if (valueType is null)
                    continue;

                // Decompose collections (T[]/List<T>/HashSet<T>) to the element type — the registry stores the
                // ELEMENT alias; the collection shape is encoded separately (TypeReference.CollectionKind /
                // the converter's []/List<>/HashSet<> wrapper).
                var (elementType, _) = TypeReferenceFactory.Decompose(valueType);
                yield return elementType;
            }
        }
    }

    private IEnumerable<Type> EnumerateActivityTypes()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
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
                logger.LogDebug(ex, "Skipping assembly '{Assembly}': types could not be reflected.", assembly.FullName);
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
        if (wellKnownTypeRegistry.TryGetType(alias, out var existing))
            return;

        // The same CLR type already registered under a (curated) alias: leave that alias as the canonical one.
        if (wellKnownTypeRegistry.TryGetAlias(elementType, out _))
            return;

        try
        {
            wellKnownTypeRegistry.RegisterType(elementType, alias);
        }
        catch (DuplicateTypeAliasException ex)
        {
            // A race with another contributor registered the same alias/type first; tolerate it.
            logger.LogDebug(ex, "Activity I/O type '{Type}' alias '{Alias}' was already registered.", elementType.FullName, alias);
        }
        catch (ReservedAliasNamespaceException ex)
        {
            // Should not happen: convention only yields bare aliases for reserved primitives. Log and skip.
            logger.LogWarning(ex, "Activity I/O type '{Type}' produced reserved bare alias '{Alias}'; skipping.", elementType.FullName, alias);
        }
    }

    private static bool IsArgumentProperty(Type propertyType) =>
        DerivesFrom(propertyType, typeof(InputArgument)) || DerivesFrom(propertyType, typeof(OutputArgument));

    private static bool IsActivityType(Type type) =>
        type is { IsClass: true, IsAbstract: false } && typeof(IActivity).IsAssignableFrom(type);

    private static bool DerivesFrom(Type? type, Type baseType)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (current == baseType)
                return true;

        return false;
    }

    // Mirrors ClrAssemblyScanner.GetArgumentValueType: the first generic base with a single type argument is the
    // value type (InputArgument<T>/OutputArgument<T>).
    private static Type? GetArgumentValueType(Type? propertyType)
    {
        for (var current = propertyType; current is not null; current = current.BaseType)
            if (current.IsGenericType && current.GetGenericArguments() is [var single])
                return single;

        return null;
    }

    private static bool IsRecoverableReflectionException(Exception exception) =>
        exception is FileNotFoundException or FileLoadException or TypeLoadException or BadImageFormatException or ReflectionTypeLoadException;
}
