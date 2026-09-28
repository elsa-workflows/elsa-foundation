using System.Reflection;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// What <see cref="EfSchemaFamilyCatalog.Discover"/> read off one <see cref="EfSchemaFamilyAttribute"/> declaration: the
/// family, the canonical name of its owning EF module as that module's <see cref="EfModuleAttribute"/> spells it, the
/// version this build writes, and the assembly that declared it.
/// </summary>
public sealed record EfSchemaFamilyDescriptor(string Name, string Module, string CurrentVersion, Assembly Assembly)
{
    /// <summary>
    /// The versions this declaration can read, as opaque labels in chain order ending at <see cref="CurrentVersion"/>
    /// (spec 180, FR-004). No chain is declared yet, so it is the current version alone; B4 (#2100) adds every
    /// predecessor the chain reaches without a gap.
    /// </summary>
    public IReadOnlyList<string> ReadableVersions => [CurrentVersion];
}
