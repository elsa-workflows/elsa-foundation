using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Reconciliation.Clr.Contracts;
using Elsa.Activities.Design.Reconciliation.Core.Models;
using Elsa.Activities.Runtime.Core;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Runtime.InteropServices;

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

            foreach (var type in types.Where(IsActivityType))
            {
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

        var inputs = new List<InputDefinition>();
        var outputs = new List<OutputDefinition>();

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (DerivesFrom(property.PropertyType, InputArgumentFullName))
                inputs.Add(new InputDefinition(
                    ReferenceKey: property.Name,
                    Name: property.Name,
                    Type: ToTypeInformation(GetArgumentValueType(property.PropertyType)),
                    StorageDriverType: null,
                    DisplayName: property.Name,
                    Category: null,
                    IsRequired: HasRequired(property)));

            else if (DerivesFrom(property.PropertyType, OutputArgumentFullName))
                outputs.Add(new OutputDefinition(
                    ReferenceKey: property.Name,
                    Name: property.Name,
                    Type: ToTypeInformation(GetArgumentValueType(property.PropertyType)),
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
            ImplementationKind: ClrImplementationDescriptor.KindValue,
            ImplementationDescriptor: new ClrImplementationDescriptor(TypeInformation.FromType(type)),
            Inputs: inputs,
            Outputs: outputs,
            Ports: []);
    }

    private static bool IsActivityType(Type type) =>
        type is { IsClass: true, IsAbstract: false }
        && type.GetInterfaces().Any(i => i.FullName == ActivityInterfaceFullName);

    private static bool HasRequired(PropertyInfo property) =>
        property.GetCustomAttributesData().Any(a => a.AttributeType.FullName == RequiredAttributeFullName);

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

    private static TypeInformation ToTypeInformation(Type? valueType) =>
        valueType is null ? TypeInformation.Object : TypeInformation.FromType(valueType);

    private static Dictionary<string, string>.ValueCollection BuildResolverPaths(IEnumerable<string> folderDlls)
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Add(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrEmpty(name) && !byName.ContainsKey(name))
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

        // The host's own dependency closure (e.g. the activity base types and their references)
        // lives next to the host binary; these are not necessarily loaded into the AppDomain yet,
        // so add them explicitly to satisfy reflection-only base-type/reference resolution.
        var baseDirectory = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDirectory) && Directory.Exists(baseDirectory))
            foreach (var dll in Directory.EnumerateFiles(baseDirectory, "*.dll"))
                Add(dll);

        foreach (var dll in Directory.EnumerateFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
            Add(dll);

        return byName.Values;
    }
}
