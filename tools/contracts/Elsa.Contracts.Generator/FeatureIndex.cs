using System.Reflection;
using CShells.Features;

namespace Elsa.Contracts.Generator;

/// <summary>
/// Feature-id → feature CLR type map over every built src assembly, so a fragment projection can compose
/// a feature's DependsOn closure across assemblies the way the shell does at runtime. Built once per
/// merge run; duplicate ids keep the first (ordinal by assembly path) and warn — the runtime treats
/// feature ids as unique per package graph (framework §2.19).
/// </summary>
public sealed class FeatureIndex
{
    private const string ShellFeatureAttributeName = "ShellFeatureAttribute";
    private readonly Dictionary<string, Type> featureTypesById = new(StringComparer.Ordinal);

    public static FeatureIndex Build(IEnumerable<string> assemblyPaths, Diagnostics diagnostics)
    {
        var index = new FeatureIndex();
        foreach (var assemblyPath in assemblyPaths.OrderBy(path => path, StringComparer.Ordinal))
        {
            Assembly assembly;
            try
            {
                assembly = TargetAssembly.LoadForExecution(assemblyPath);
            }
            catch (Exception exception) when (exception is IOException or BadImageFormatException)
            {
                diagnostics.Warning(assemblyPath, "ELSACT007",
                    $"Assembly could not be loaded for the feature index: {exception.GetBaseException().Message}");
                continue;
            }

            foreach (var type in TargetAssembly.GetLoadableTypes(assembly))
            {
                if (type is not { IsClass: true, IsAbstract: false } || !typeof(IShellFeature).IsAssignableFrom(type))
                    continue;

                var id = ReadFeatureId(type);
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                if (!index.featureTypesById.TryAdd(id, type))
                    diagnostics.Warning(assemblyPath, "ELSACT008",
                        $"Feature id '{id}' is declared by both '{index.featureTypesById[id].FullName}' and '{type.FullName}'; keeping the first.");
            }
        }

        return index;
    }

    public bool TryGetFeatureType(string featureId, out Type featureType) =>
        featureTypesById.TryGetValue(featureId, out featureType!);

    public static string? ReadFeatureId(Type featureType) =>
        featureType.GetCustomAttributesData()
            .FirstOrDefault(attribute => attribute.AttributeType.Name == ShellFeatureAttributeName)
            is { ConstructorArguments.Count: > 0 } attributeData
            ? attributeData.ConstructorArguments[0].Value as string
            : null;

    public static IReadOnlyList<string> ReadDependsOn(Type featureType)
    {
        var attribute = featureType.GetCustomAttributesData()
            .FirstOrDefault(candidate => candidate.AttributeType.Name == ShellFeatureAttributeName);
        if (attribute is null)
            return [];

        var value = attribute.NamedArguments.FirstOrDefault(argument => argument.MemberName == "DependsOn").TypedValue.Value;
        if (value is not IReadOnlyCollection<CustomAttributeTypedArgument> entries)
            return [];

        return entries
            .Select(entry => entry.Value is CustomAttributeTypedArgument nested ? nested.Value as string : entry.Value as string)
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Select(entry => entry!)
            .ToArray();
    }
}
