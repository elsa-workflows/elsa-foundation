using System.Runtime.Loader;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Persistence.EntityFramework;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
/// the entry holds only the versions all of them read (FR-021). They must name the same owning EF module - or agree
/// that the family is shared, owned by none (spec 180, FR-001) - if they do not, that one family is isolated rather
/// than let it take the whole report down with it (FR-020): its entry names one of the disagreeing modules, which may
/// be <see langword="null"/> when a shared declaration disagrees with an owned one, credits no version, and an error
/// is logged naming the family and every conflicting declaration, so <see cref="ReadsSchemaVersion"/> counts the host
/// for that family and fails it — the conservative direction — while every other family is still reported normally. A
/// declaration <see cref="EfSchemaFamilyCatalog"/> refuses still fails the whole report: a family left out of it would
/// let a version finalize that this host cannot read.
/// </para>
/// <para>
/// The database identity and the observed finalized version come from spec 181's finalization record, which B5 (#2101)
/// adds. Until then the host has read no record, so both are <see langword="null"/>, and an entry that names no
/// database counts for every database: the conservative direction (FR-019; Decisions, Q19).
/// </para>
/// </remarks>
public sealed class EfSchemaReadabilitySource(ILogger<EfSchemaReadabilitySource>? logger = null) : IMemberReportSource<ReadabilitySection>
{
    private readonly ILogger _logger = logger ?? NullLogger<EfSchemaReadabilitySource>.Instance;

    /// <summary>Reads the declarations of every assembly loaded in this process, across every <see cref="AssemblyLoadContext"/>.</summary>
    public ValueTask<ReadabilitySection> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(EfSchemaFamilyCatalog.Discover(AssemblyLoadContext.All.SelectMany(context => context.Assemblies)), _logger));
    }

    /// <summary>
    /// The section <paramref name="declarations"/> yield: one entry per family, ordered by family so that a host whose
    /// declarations have not changed publishes an equal report whatever order its load contexts enumerate in. A family
    /// whose loaded declarations disagree on the owning EF module is logged to <paramref name="logger"/> and reported
    /// with no readable version rather than dropped, so a malformed family never omits every other one from the report.
    /// </summary>
    public static ReadabilitySection Read(IEnumerable<EfSchemaFamilyDescriptor> declarations, ILogger? logger = null) =>
        new(declarations
            .GroupBy(declaration => declaration.Name, StringComparer.Ordinal)
            .OrderBy(family => family.Key, StringComparer.Ordinal)
            .Select(family => Entry(family, logger ?? NullLogger.Instance)));

    private static ReadabilityEntry Entry(IGrouping<string, EfSchemaFamilyDescriptor> declarations, ILogger logger)
    {
        var modules = declarations.Select(declaration => declaration.Module).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (modules.Length > 1)
        {
            // Isolated rather than thrown (spec 183, FR-020): a family whose declarations disagree on the owning
            // module must not take every other family's entry out of the report with it. It is reported reading no
            // version - the conservative direction, since ReadsSchemaVersion then counts and fails this host for it -
            // and the disagreement is logged so it is still visible.
            logger.LogError(
                "Schema family '{Family}' is declared under more than one EF module: {Declarations}. A family's " +
                "declarations must agree on its owning EF module, owned or shared (spec 180, FR-001); this host " +
                "reports it as reading no version until its declarations agree.",
                declarations.Key,
                string.Join(", ", declarations.Select(declaration => $"'{declaration.Module ?? "no module (shared)"}' in {declaration.Assembly.GetName().Name}")));

            return new ReadabilityEntry(declarations.Key, modules.Order(StringComparer.Ordinal).First(), []);
        }

        var readable = declarations
            .Select(declaration => declaration.ReadableVersions.AsEnumerable())
            .Aggregate((versions, next) => versions.Intersect(next, StringComparer.Ordinal));

        return new ReadabilityEntry(declarations.Key, modules[0], readable);
    }
}
