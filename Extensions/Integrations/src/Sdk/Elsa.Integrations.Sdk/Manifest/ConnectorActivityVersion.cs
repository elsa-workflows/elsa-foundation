using System.Reflection;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Primitives.Versioning;

namespace Elsa.Integrations.Sdk.Manifest;

/// <summary>
/// Activity version order from foundation CLR reconciliation: <c>[Version]</c> walking the base
/// chain (issue #417), then informational, then assembly <c>Major.Minor.Build</c> (#2726 rule 2).
/// <c>ActivityTypeVersionResolver</c> lives in design reconciliation; this package does not
/// reference that assembly. The walk matches
/// <c>ReflectionOnlyAttributes.ReadStringArgumentUpTypeChain</c>.
/// </summary>
internal static class ConnectorActivityVersion
{
    private static readonly string VersionAttributeFullName = typeof(VersionAttribute).FullName!;

    public static string Resolve(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var versionAttributeValue = ReadVersionUpTypeChain(type);
        if (versionAttributeValue is not null)
        {
            if (!SemVer.TryParse(versionAttributeValue, out _))
                throw new InvalidOperationException($"Activity '{type.FullName}' has invalid [Version] '{versionAttributeValue}'.");
            return versionAttributeValue;
        }

        var assembly = type.Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (informational is not null && SemVer.TryParse(informational, out _))
            return informational;

        var assemblyVersion = assembly.GetName().Version;
        if (assemblyVersion is not null)
        {
            var patch = assemblyVersion.Build < 0 ? 0 : assemblyVersion.Build;
            var mapped = $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{patch}";
            if (SemVer.TryParse(mapped, out _))
                return mapped;
        }

        throw new InvalidOperationException($"Activity '{type.FullName}' has no resolvable SemVer.");
    }

    /// <summary>
    /// <see cref="VersionAttribute"/> is <c>Inherited = false</c>. Walk each declaring type the
    /// same way CLR reconciliation does, rather than <c>GetCustomAttribute(inherit: true)</c>.
    /// </summary>
    private static string? ReadVersionUpTypeChain(Type type)
    {
        for (var current = (Type?)type; current is not null; current = current.BaseType)
        {
            var data = current.GetCustomAttributesData()
                .FirstOrDefault(attribute => attribute.AttributeType.FullName == VersionAttributeFullName);
            if (data?.ConstructorArguments is [{ Value: string value }, ..])
                return value;
        }

        return null;
    }
}
