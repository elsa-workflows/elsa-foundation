using Elsa.Activities.Design.Reconciliation.Clr.Contracts;
using Elsa.Activities.Design.Reconciliation.Clr.Exceptions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Primitives.Versioning;
using System.Reflection;

namespace Elsa.Activities.Design.Reconciliation.Clr.Services;

/// <summary>
/// Resolves a CLR activity's author-controlled SemVer 2.0.0 version (FR-020). Pure decision
/// function over already-extracted metadata so it is exhaustively unit-testable without loading
/// assemblies; the <see cref="ClrAssemblyScanner"/> does the reflection-only reading and feeds
/// the raw values in.
/// </summary>
public sealed class ActivityTypeVersionResolver : IActivityTypeVersionResolver
{
    private static readonly string VersionAttributeFullName = typeof(VersionAttribute).FullName!;
    private static readonly string InformationalVersionAttributeFullName = typeof(AssemblyInformationalVersionAttribute).FullName!;

    /// <summary>
    /// Resolution order: the <c>[Version]</c> attribute value if present (must be valid SemVer,
    /// else <see cref="InvalidActivityVersionException"/>); else the assembly's informational
    /// version when it is valid SemVer; else the 4-part assembly version's
    /// <c>Major.Minor.Build</c> mapped to <c>MAJOR.MINOR.PATCH</c>; else
    /// <see cref="UnresolvableAssemblyVersionException"/>.
    /// </summary>
    public string Resolve(Type type, Assembly assembly)
    {
        var assemblyName = assembly.GetName();
        var versionAttributeValue = ReadStringArgument(type.GetCustomAttributesData(), VersionAttributeFullName);
        var activityTypeFullName = type.FullName!;        
        var informationalVersion = ReadStringArgument(assembly.GetCustomAttributesData(), InformationalVersionAttributeFullName);
        var assemblyVersion = assemblyName.Version;
   

        if (versionAttributeValue is not null)
        {
            if (!SemVer.TryParse(versionAttributeValue, out _))
                throw new InvalidActivityVersionException(activityTypeFullName, versionAttributeValue);

            return versionAttributeValue;
        }

        if (informationalVersion is not null && SemVer.TryParse(informationalVersion, out _))
            return informationalVersion;

        if (assemblyVersion is not null)
        {
            var patch = assemblyVersion.Build < 0 ? 0 : assemblyVersion.Build;
            var mapped = $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{patch}";
            if (SemVer.TryParse(mapped, out _))
                return mapped;
        }

        throw new UnresolvableAssemblyVersionException(activityTypeFullName, assemblyName.Name ?? assemblyName.FullName ?? string.Empty);
    }    

    private static string? ReadStringArgument(IEnumerable<CustomAttributeData> attributes, string attributeFullName)
    {
        var data = attributes.FirstOrDefault(a => a.AttributeType.FullName == attributeFullName);
        return data?.ConstructorArguments is [{ Value: string value }, ..] ? value : null;
    }
}
