using System.Reflection;
using Elsa.Persistence.EntityFramework.ResourceResolution;

namespace Elsa.Persistence.EntityFramework.Tooling;

/// <summary>Combines explicit enrollment with existing feature and EF module metadata.</summary>
internal static class EfPersistenceParticipantCatalog
{
    public static IReadOnlyList<EnrolledPersistenceParticipant> Discover(
        IEnumerable<Assembly> assemblies,
        IEnumerable<string>? opaqueFeatureIds = null)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var available = assemblies.Distinct().ToArray();
        var modules = EfModuleCatalog.Discover(available);
        var opaque = opaqueFeatureIds?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        return EfProviderAgreement.Discover(available)
            .Where(usage => usage.FeatureType.IsDefined(typeof(EfPersistenceResourceParticipantAttribute), inherit: false))
            .Select(usage => Describe(usage, modules, opaque))
            .ToArray();
    }

    private static EnrolledPersistenceParticipant Describe(
        EfFeatureModuleUsage usage,
        IReadOnlyList<EfModuleDescriptor> modules,
        IReadOnlySet<string> opaque)
    {
        var contexts = usage.Modules
            .Select(name => EfModuleCatalog.Find(modules, name)?.ContextType.FullName)
            .Where(name => name is not null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Missing or conflicting ownership is represented as unresolved metadata for the EF validator.
        // Discovery must not make an otherwise legacy shell fail merely because a module is unavailable.
        var contextIdentity = contexts.Length == 1 && usage.Modules.Count == 1 ? contexts[0]! : string.Empty;

        return new EnrolledPersistenceParticipant(
            usage.Feature,
            usage.Modules,
            contextIdentity,
            usage.DeclaresProvider,
            opaque.Contains(usage.Feature));
    }
}
