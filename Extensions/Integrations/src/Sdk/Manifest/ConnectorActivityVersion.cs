using System.Reflection;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Primitives.Versioning;

namespace Elsa.Integrations.Sdk.Manifest;

/// <summary>
/// Activity version order from foundation CLR reconciliation: <c>[Version]</c>, then informational,
/// then assembly <c>Major.Minor.Build</c> (#2726 rule 2). The package does not declare a competing
/// version.
/// </summary>
internal static class ConnectorActivityVersion
{
    public static string Resolve(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var versionAttribute = type.GetCustomAttribute<VersionAttribute>(inherit: true);
        if (versionAttribute is not null)
        {
            if (!SemVer.TryParse(versionAttribute.Version, out _))
                throw new InvalidOperationException($"Activity '{type.FullName}' has invalid [Version] '{versionAttribute.Version}'.");
            return versionAttribute.Version;
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
}
