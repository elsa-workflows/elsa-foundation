using System.Runtime.Loader;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Persistence.EntityFramework;

namespace Elsa.Cluster.Readability;

/// <summary>
/// This host's readability report (spec 183, FR-019 to FR-022): one entry for each schema family whose declaration
/// (spec 180, FR-001) is loaded in the process, from any load context, holding the versions every loaded declaration of
/// that family can read.
/// </summary>
/// <remarks>
/// <para>
/// It reads declarations and nothing else, so no configuration can change what it reports (FR-020, MR-002). It reads
/// them each time a provider publishes, so a package Nuplane loads is reported by the first publish after its assembly
/// loads. A family stays while any declaration of it is loaded, whether or not a shell has its module enabled (FR-022):
/// a host that can still write a family's rows has to be counted for it.
/// </para>
/// <para>
/// When several declarations of one family are loaded, as an old and a new generation of a package are during a reload,
/// the entry holds only the versions all of them read (FR-021). They must name the same owning EF module; if they do
/// not, the report is refused rather than published, because either module would misstate the host. A declaration
/// <see cref="EfSchemaFamilyCatalog"/> refuses fails the report the same way: a family left out of it would let a version
/// finalize that this host cannot read.
/// </para>
/// <para>
/// The database identity and the observed finalized version come from spec 181's finalization record, which B5 (#2101)
/// adds. Until then the host has read no record, so both are <see langword="null"/>, and an entry that names no
/// database counts for every database: the conservative direction (FR-019; Decisions, Q19).
/// </para>
/// </remarks>
public sealed class EfSchemaReadabilitySource : IMemberReportSource<ReadabilitySection>
{
    /// <summary>Reads the declarations of every assembly loaded in this process, across every <see cref="AssemblyLoadContext"/>.</summary>
    public ValueTask<ReadabilitySection> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(EfSchemaFamilyCatalog.Discover(AssemblyLoadContext.All.SelectMany(context => context.Assemblies))));
    }

    /// <summary>
    /// The section <paramref name="declarations"/> yield: one entry per family, ordered by family so that a host whose
    /// declarations have not changed publishes an equal report whatever order its load contexts enumerate in.
    /// </summary>
    public static ReadabilitySection Read(IEnumerable<EfSchemaFamilyDescriptor> declarations) =>
        new(declarations
            .GroupBy(declaration => declaration.Name, StringComparer.Ordinal)
            .OrderBy(family => family.Key, StringComparer.Ordinal)
            .Select(Entry));

    private static ReadabilityEntry Entry(IGrouping<string, EfSchemaFamilyDescriptor> declarations)
    {
        var modules = declarations.Select(declaration => declaration.Module).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (modules.Length > 1)
            throw new InvalidOperationException(
                $"Schema family '{declarations.Key}' is declared under more than one EF module: " +
                $"{string.Join(", ", declarations.Select(declaration => $"'{declaration.Module}' in {declaration.Assembly.GetName().Name}"))}. " +
                "A family belongs to exactly one EF module (spec 180, FR-001).");

        var readable = declarations
            .Select(declaration => declaration.ReadableVersions.AsEnumerable())
            .Aggregate((versions, next) => versions.Intersect(next, StringComparer.Ordinal));

        return new ReadabilityEntry(declarations.Key, modules[0], readable);
    }
}
