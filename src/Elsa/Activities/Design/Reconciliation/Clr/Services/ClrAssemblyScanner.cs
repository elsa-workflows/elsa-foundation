using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Reconciliation.Clr.Contracts;
using Elsa.Activities.Design.Reconciliation.Core.Models;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Elsa.Activities.Design.Reconciliation.Clr.Services;

/// <summary>
/// Reflection-only scanner (R5) that reads activity-bearing assemblies from a folder via a
/// <see cref="MetadataLoadContext"/> and produces one <see cref="ActivityVersionReconciliationModel"/>
/// per discovered <c>IActivity</c> implementation. No type is loaded into the execution context, so
/// scanning never runs author code or pollutes the default <see cref="AssemblyLoadContext"/>.
/// </summary>
/// <remarks>
/// Resilient scan (FR-023): a DLL that exposes no activities is skipped silently; a DLL that fails
/// to load or reflect is logged and skipped; the pass never aborts wholesale. Per-activity faults —
/// an invalid <c>[Version]</c> or an unresolvable assembly version — still throw (the
/// <see cref="ActivityTypeVersionResolver"/> raises a domain exception) so a misconfigured activity is
/// loud rather than silently dropped.
/// </remarks>
public sealed class ClrAssemblyScanner(
    IActivityTypeVersionResolver versionResolver,
    IActivityTypeCategoryResolver categoryResolver,
    ILogger<ClrAssemblyScanner> logger) : IClrAssemblyScanner
{
    private static readonly string ActivityInterfaceFullName = typeof(IActivity).FullName!;
    private static readonly string InputArgumentFullName = typeof(InputArgument).FullName!;
    private static readonly string OutputArgumentFullName = typeof(OutputArgument).FullName!;
    private static readonly string RequiredAttributeFullName = typeof(RequiredAttribute).FullName!;
    private static readonly string ActivityInputAttributeFullName = typeof(ActivityInputAttribute).FullName!;
    private static readonly string ActivityStructureAttributeFullName = typeof(ActivityStructureAttribute).FullName!;
    private static readonly string ActivityChildSlotAttributeFullName = typeof(ActivityChildSlotAttribute).FullName!;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<ActivityVersionReconciliationModel> Scan(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return [];

        var folderDlls = Directory.EnumerateFiles(folderPath, "*.dll", SearchOption.TopDirectoryOnly).ToList();
        if (folderDlls.Count == 0)
            return [];

        using var context = new MetadataLoadContext(new PathAssemblyResolver(BuildResolverPaths(folderDlls)));
        var models = new List<ActivityVersionReconciliationModel>();

        foreach (var dll in folderDlls)
        {
            Assembly assembly;
            try
            {
                assembly = context.LoadFromAssemblyPath(dll);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipping '{Dll}': assembly could not be loaded for reflection-only scanning.", dll);
                continue;
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                logger.LogWarning(ex, "Partial type load in '{Dll}'; scanning the types that resolved.", dll);
                types = ex.Types.Where(t => t is not null).ToArray()!;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipping '{Dll}': types could not be reflected.", dll);
                continue;
            }

            foreach (var type in types)
            {
                bool isActivityType;

                try
                {
                    isActivityType = IsActivityType(type);
                }
                catch (Exception ex) when (IsRecoverableReflectionException(ex))
                {
                    logger.LogWarning(ex, "Skipping type '{Type}' in '{Dll}': interfaces could not be resolved during activity scanning.", type.FullName, dll);
                    continue;
                }

                if (!isActivityType)
                    continue;

                models.Add(
                    BuildModel(type, assembly)
                );
            }
        }

        return models;
    }

    private ActivityVersionReconciliationModel BuildModel(Type type, Assembly assembly)
    {
        var version = versionResolver.Resolve(type, assembly);
        var category = categoryResolver.Resolve(type, assembly);
        var attributes = type.GetCustomAttributesData().ToArray();

        var inputs = new List<InputDefinition>();
        var outputs = new List<OutputDefinition>();

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (DerivesFrom(property.PropertyType, InputArgumentFullName))
            {
                var valueType = GetArgumentValueType(property.PropertyType);
                var metadata = ReadActivityInputMetadata(property, valueType);
                inputs.Add(new InputDefinition(
                    ReferenceKey: property.Name,
                    Name: property.Name,
                    Type: ToTypeReference(valueType),
                    StorageDriverType: null,
                    DisplayName: property.Name,
                    Category: null,
                    Order: metadata.Order,
                    IsRequired: HasRequired(property),
                    DefaultValue: metadata.DefaultValue,
                    DefaultSyntax: metadata.DefaultSyntax));
            }

            else if (DerivesFrom(property.PropertyType, OutputArgumentFullName))
                outputs.Add(new OutputDefinition(
                    ReferenceKey: property.Name,
                    Name: property.Name,
                    Type: ToTypeReference(GetArgumentValueType(property.PropertyType)),
                    StorageDriverType: null,
                    DisplayName: property.Name,
                    Category: null,
                    IsRequired: HasRequired(property)));
        }

        return new ActivityVersionReconciliationModel(
            Id: null,
            Version: version,
            ActivityTypeKey: type.FullName!,
            DisplayName: null,
            Category: category,
            Description: null,
            DescriptorType: typeof(ClrActivityDescriptor).FullName!,
            Descriptor: new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(type)),
            Inputs: inputs,
            Outputs: outputs,
            DesignFacets: BuildDesignFacets(attributes),
            // Keep CLR catalog content stable for already-reconciled activity versions. Runtime trigger
            // classification is derived from the CLR descriptor by ExecutableNodeCompiler instead; changing
            // this value in place would invalidate persisted same-version hashes during an upgrade.
            ExecutionType: ActivityExecutionType.Action);
    }

    private static IReadOnlyCollection<ActivityDesignFacet> BuildDesignFacets(IReadOnlyCollection<CustomAttributeData> attributes)
    {
        var structureAttribute = attributes.FirstOrDefault(attribute => attribute.AttributeType.FullName == ActivityStructureAttributeFullName);
        if (structureAttribute is null)
            return [];

        var kind = ReadRequiredStringConstructorArgument(structureAttribute, 0);
        var schemaVersion = ReadRequiredStringConstructorArgument(structureAttribute, 1);
        var mode = ReadNamedStringArgument(structureAttribute, nameof(ActivityStructureAttribute.Mode)) ?? "generic";
        var supportsScopedVariables = ReadNamedBoolArgument(structureAttribute, nameof(ActivityStructureAttribute.SupportsScopedVariables));
        var slots = attributes
            .Where(attribute => attribute.AttributeType.FullName == ActivityChildSlotAttributeFullName)
            .Select(ToSlotDescriptor)
            .ToArray();
        var payload = new ActivityStructureDesignFacetPayload(
            mode,
            supportsScopedVariables,
            slots,
            BuildInitialPayload(mode, slots));

        return [new ActivityDesignFacet(kind, schemaVersion, JsonSerializer.SerializeToElement(payload, SerializerOptions))];
    }

    private static ActivityChildSlotDesignDescriptor ToSlotDescriptor(CustomAttributeData attribute) =>
        new(
            ReadRequiredStringConstructorArgument(attribute, 0),
            ReadRequiredStringConstructorArgument(attribute, 1),
            ReadRequiredStringConstructorArgument(attribute, 2),
            ReadRequiredStringConstructorArgument(attribute, 3),
            ReadNamedStringArgument(attribute, nameof(ActivityChildSlotAttribute.CollectionProperty)),
            ReadNamedStringArgument(attribute, nameof(ActivityChildSlotAttribute.ChildProperty)),
            ReadNamedStringArgument(attribute, nameof(ActivityChildSlotAttribute.LabelProperty)),
            ReadNamedStringArgument(attribute, nameof(ActivityChildSlotAttribute.SlotNameTemplate)));

    private static IReadOnlyDictionary<string, object?> BuildInitialPayload(
        string mode,
        IReadOnlyCollection<ActivityChildSlotDesignDescriptor> slots)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var slot in slots)
        {
            var property = slot.CollectionProperty ?? slot.Property;
            if (payload.ContainsKey(property))
                continue;

            payload[property] = slot.CollectionProperty is not null || slot.Cardinality == ActivityChildSlotCardinalities.Many
                ? Array.Empty<object>()
                : null;
        }

        if (mode == "flowchart")
        {
            payload.TryAdd("connections", Array.Empty<object>());
            payload.TryAdd("startNodeId", null);
            payload.TryAdd("nodeMetadata", new Dictionary<string, object>(StringComparer.Ordinal));
            payload.TryAdd("connectionMetadata", new Dictionary<string, object>(StringComparer.Ordinal));
        }

        return payload;
    }

    private static string ReadRequiredStringConstructorArgument(CustomAttributeData attribute, int index)
    {
        if (attribute.ConstructorArguments.Count <= index || attribute.ConstructorArguments[index].Value is not string value || string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Attribute '{attribute.AttributeType.FullName}' is missing required string constructor argument {index}.");

        return value;
    }

    private static string? ReadNamedStringArgument(CustomAttributeData attribute, string name) =>
        attribute.NamedArguments.FirstOrDefault(argument => argument.MemberName == name).TypedValue.Value as string;

    private static bool ReadNamedBoolArgument(CustomAttributeData attribute, string name)
    {
        var value = attribute.NamedArguments.FirstOrDefault(argument => argument.MemberName == name).TypedValue.Value;
        return value is bool boolValue && boolValue;
    }

    private static bool IsActivityType(Type type) =>
        type is { IsClass: true, IsAbstract: false }
        && type.GetInterfaces().Any(i => i.FullName == ActivityInterfaceFullName);

    private static bool IsRecoverableReflectionException(Exception exception) =>
        exception is FileNotFoundException or FileLoadException or TypeLoadException or BadImageFormatException;

    // Walk the base-property chain: a [Required] declared on a base class's input/output property must
    // be honoured even though a reflection-only MetadataLoadContext gives no inherit-aware attribute
    // read (issue #417 item 3).
    private static bool HasRequired(PropertyInfo property) =>
        ReflectionOnlyAttributes.HasAttributeUpPropertyChain(property, RequiredAttributeFullName);

    private static ActivityInputMetadata ReadActivityInputMetadata(PropertyInfo property, Type? valueType)
    {
        var attribute = ReflectionOnlyAttributes.FindAttributeUpPropertyChain(property, ActivityInputAttributeFullName);
        if (attribute is null)
            return new ActivityInputMetadata(0, null, null);

        var order = ReadNamedSingleArgument(attribute, nameof(ActivityInputAttribute.Order)) ?? 0;
        var defaultValue = ReadNamedStringArgument(attribute, nameof(ActivityInputAttribute.DefaultValue));
        var defaultSyntax = ReadNamedStringArgument(attribute, nameof(ActivityInputAttribute.DefaultSyntax));

        return new ActivityInputMetadata(
            order,
            string.IsNullOrWhiteSpace(defaultValue) ? null : ParseDefaultValue(defaultValue, valueType),
            defaultSyntax);
    }

    private static float? ReadNamedSingleArgument(CustomAttributeData attribute, string name)
    {
        var value = attribute.NamedArguments.FirstOrDefault(argument => argument.MemberName == name).TypedValue.Value;
        return value switch
        {
            float single => single,
            double number => (float)number,
            _ => null
        };
    }

    private static JsonElement ParseDefaultValue(string value, Type? valueType)
    {
        var typeName = valueType?.FullName;

        if (typeName == typeof(bool).FullName && bool.TryParse(value, out var boolValue))
            return JsonSerializer.SerializeToElement(boolValue);
        if (typeName == typeof(byte).FullName && byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var byteValue))
            return JsonSerializer.SerializeToElement(byteValue);
        if (typeName == typeof(sbyte).FullName && sbyte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sbyteValue))
            return JsonSerializer.SerializeToElement(sbyteValue);
        if (typeName == typeof(short).FullName && short.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var shortValue))
            return JsonSerializer.SerializeToElement(shortValue);
        if (typeName == typeof(ushort).FullName && ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ushortValue))
            return JsonSerializer.SerializeToElement(ushortValue);
        if (typeName == typeof(int).FullName && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
            return JsonSerializer.SerializeToElement(intValue);
        if (typeName == typeof(uint).FullName && uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uintValue))
            return JsonSerializer.SerializeToElement(uintValue);
        if (typeName == typeof(long).FullName && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
            return JsonSerializer.SerializeToElement(longValue);
        if (typeName == typeof(ulong).FullName && ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ulongValue))
            return JsonSerializer.SerializeToElement(ulongValue);
        if (typeName == typeof(float).FullName && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
            return JsonSerializer.SerializeToElement(floatValue);
        if (typeName == typeof(double).FullName && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
            return JsonSerializer.SerializeToElement(doubleValue);
        if (typeName == typeof(decimal).FullName && decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue))
            return JsonSerializer.SerializeToElement(decimalValue);

        return JsonSerializer.SerializeToElement(value);
    }

    private sealed record ActivityInputMetadata(float Order, JsonElement? DefaultValue, string? DefaultSyntax);

    private static bool DerivesFrom(Type? type, string fullName)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (current.FullName == fullName)
                return true;

        return false;
    }

    private static Type? GetArgumentValueType(Type? propertyType)
    {
        for (var current = propertyType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericArguments() is [var single])
                return single;
        }

        return null;
    }

    // Reflection-only path: types come from a MetadataLoadContext, so the runtime well-known type
    // registry can't resolve them. The element alias is produced by the shared TypeAliasConvention —
    // a reserved bare alias for BCL primitives (string→"String", int→"Int32"), else the dotted FullName.
    // The framework's runtime registration pass registers activity I/O CLR types under the SAME
    // convention, so these aliases resolve back to the real CLR type at compile time (FR-004b).
    private static TypeReference ToTypeReference(Type? valueType) =>
        valueType is null
            ? new TypeReference("Object")
            : TypeReferenceFactory.FromClrType(valueType, TypeAliasConvention.CanonicalAlias);

    // The host's own dependency closure (base directory, trusted-platform-assemblies, runtime
    // directory) is enumerated from disk and is process-lifetime-invariant, so it is read once and
    // cached instead of re-globbing on every Scan() call (issue #417 item 2). AppDomain assemblies
    // are NOT cached here: that set can grow as assemblies load lazily, and enumerating it is
    // in-memory (no disk IO), so it stays live per call to preserve resolution behaviour exactly.
    private static readonly Lazy<IReadOnlyList<string>> InvariantFrameworkPaths = new(CollectInvariantFrameworkPaths);

    // Instance method (not static): the collision path logs through the injected logger. Precedence
    // is unchanged (first-wins: folder → AppDomain → cached framework closure); we only surface the
    // drop that was previously silent (issue #417 item 4).
    private Dictionary<string, string>.ValueCollection BuildResolverPaths(IEnumerable<string> folderDlls)
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);

        void Add(string path)
        {
            if (!seenPaths.Add(path))
                return;

            var name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(name))
                return;

            if (byName.TryGetValue(name, out var existing))
            {
                // A later source (AppDomain or framework closure) offers the same simple-name as an
                // already-registered path. First-wins is intentional, but the drop must not be silent:
                // divergent copies of the same assembly can otherwise mask a version the author shipped.
                logger.LogWarning(
                    "Duplicate assembly name '{AssemblyName}' during reflection-only resolver setup; keeping '{KeptPath}' and skipping '{SkippedPath}'.",
                    name, existing, path);
                return;
            }

            byName[name] = path;
        }

        // Author assemblies win over framework copies so we read the version the author shipped.
        foreach (var dll in folderDlls)
            Add(dll);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
                continue;

            var location = assembly.Location;
            if (!string.IsNullOrEmpty(location) && File.Exists(location))
                Add(location);
        }

        // Cached framework closure (base directory → TPA → runtime directory), in the same
        // first-wins order as before; folder + AppDomain entries above still take precedence.
        foreach (var dll in InvariantFrameworkPaths.Value)
            Add(dll);

        return byName.Values;
    }

    private static IReadOnlyList<string> CollectInvariantFrameworkPaths()
    {
        var paths = new List<string>();

        // The host's own dependency closure (e.g. the activity base types and their references)
        // lives next to the host binary; these are not necessarily loaded into the AppDomain yet,
        // so add them explicitly to satisfy reflection-only base-type/reference resolution.
        var baseDirectory = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDirectory) && Directory.Exists(baseDirectory))
            paths.AddRange(Directory.EnumerateFiles(baseDirectory, "*.dll"));

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedPlatformAssemblies)
            paths.AddRange(trustedPlatformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        paths.AddRange(Directory.EnumerateFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"));

        return paths;
    }
}
