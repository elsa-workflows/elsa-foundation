using System.Runtime.Loader;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Persistence.Schema;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Cluster.Readability;

/// <summary>
/// This host's readability report (spec 183, FR-019 to FR-022): one entry for each schema family whose declaration
/// (spec 180, FR-001) is loaded in the process, from any load context, holding the versions every loaded declaration of
/// that family can read, less any an upgrade has retired (FR-021, amended 2026-09-29).
/// </summary>
/// <remarks>
/// <para>
/// It reads declarations and nothing else, so no configuration can change what it reports (FR-020, MR-002). It reads
/// them each time a provider publishes, so a package Nuplane loads is reported by the first publish after its assembly
/// loads. A family stays while any declaration of it is loaded and not retired, whether or not a shell has its module
/// enabled (FR-022): a host that can still write a family's rows has to be counted for it.
/// </para>
/// <para>
/// When several declarations of one family are loaded, as an old and a new generation of a package are during a reload,
/// the entry holds only the versions all of them read (FR-021). The declarations of one family must name the same
/// owning EF module - or agree that the family is shared, owned by none (spec 180, FR-001) - and if they do not, that
/// one family is isolated rather than let it take the whole report down with it (FR-020): its entry names one of the
/// disagreeing modules, which may be <see langword="null"/> when a shared declaration disagrees with an owned one,
/// credits no version, and an error is logged naming the family and every conflicting declaration, so
/// <see cref="ReadsSchemaVersion"/> counts the host for that family and fails it — the conservative direction — while
/// every other family is still reported normally. A declaration <see cref="EfSchemaFamilyCatalog"/> refuses still fails
/// the whole report: a family left out of it would let a version finalize that this host cannot read.
/// </para>
/// <para>
/// An old generation stops counting only once the host's <see cref="ISupersededAssemblySource"/> names it retired (FR-021,
/// amended 2026-09-29): a newer generation of the same assembly replaced it in the host's active package set, no shell
/// generation whose container has not finished disposing composes a feature from its load context, and the feature
/// catalog the next shell generation is built from names neither it nor a feature in its load context. Until then, and
/// always on a host that composes no such source, it narrows the entry, so a load context a package runtime never
/// unloads holds an upgraded host back until the old generation can no longer run rather than until the process restarts.
/// </para>
/// <para>
/// Each declaration credits its readable set (spec 180, FR-004): its current version and every predecessor its upcaster
/// chain reaches without a gap, computed by the same function a store's read uses to decide which rows it accepts, so a
/// host never reports a version its reads refuse. A chain with a fault the build and the family's registration refuse
/// (FR-005) still credits only what it reaches, never a version below a gap, and the fault is logged as an error.
/// </para>
/// <para>
/// The database identity and the observed finalized version come from the finalization records this host's gates have
/// read (spec 181, FR-001 and FR-010), through the host's one <see cref="EfSchemaFinalizationObservations"/>. An entry
/// names a database only while every record of the family this host read carries that one identity and no activation
/// of the family is between publishing and reading; otherwise, and before any record is read, it names none, which
/// counts for every database: the conservative direction (FR-019; Decisions, Q19). Naming the database read most
/// recently instead would let a second database this host serves finalize a version it cannot read.
/// </para>
/// <para>
/// An entry also says whether the family's module is active in this host: admitted by a gate that has not stopped
/// (FR-019, amended 2026-09-30; see <see cref="ReadabilityEntry.ModuleActive"/>). With no observations to ask, every entry
/// is active.
/// </para>
/// </remarks>
public sealed class EfSchemaReadabilitySource(
    ILogger<EfSchemaReadabilitySource>? logger = null,
    EfSchemaFinalizationObservations? observations = null,
    ISupersededAssemblySource? superseded = null) : IMemberReportSource<ReadabilitySection>
{
    private readonly ILogger _logger = logger ?? NullLogger<EfSchemaReadabilitySource>.Instance;

    /// <summary>
    /// Reads the declarations of every assembly loaded in this process, across every <see cref="AssemblyLoadContext"/>,
    /// except the ones <see cref="ISupersededAssemblySource.GetRetiredAsync"/> names.
    /// </summary>
    public async ValueTask<ReadabilitySection> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Read(EfSchemaFamilyCatalog.Discover(await LoadedAssemblies.ExceptRetiredAsync(superseded, cancellationToken)), _logger, observations);
    }

    /// <summary>
    /// The section <paramref name="declarations"/> yield: one entry per family, ordered by family so that a host whose
    /// declarations have not changed publishes an equal report whatever order its load contexts enumerate in. A family
    /// whose loaded declarations disagree on the owning EF module is logged to <paramref name="logger"/> and reported
    /// with no readable version rather than dropped, so a malformed family never omits every other one from the report.
    /// </summary>
    public static ReadabilitySection Read(
        IEnumerable<EfSchemaFamilyDescriptor> declarations,
        ILogger? logger = null,
        EfSchemaFinalizationObservations? observations = null) =>
        new(declarations
            .GroupBy(declaration => declaration.Name, StringComparer.Ordinal)
            .OrderBy(family => family.Key, StringComparer.Ordinal)
            .Select(family => Observed(Entry(family, logger ?? NullLogger.Instance), observations)));

    /// <summary>
    /// <paramref name="entry"/> with the database identity, observed finalized version and activity this host recorded, if
    /// any. A source built with no <see cref="EfSchemaFinalizationObservations"/>, or given another instance than the
    /// gates report to, cannot tell which modules are active, so it leaves every entry active, the direction that counts the
    /// member (<see cref="ReadabilityEntry.ModuleActive"/>).
    /// </summary>
    private static ReadabilityEntry Observed(ReadabilityEntry entry, EfSchemaFinalizationObservations? observations)
    {
        if (observations is null)
            return entry;
        var observed = observations.Find(entry.Family);
        return new ReadabilityEntry(entry.Family, entry.EfModule, entry.ReadableVersions, observed.DatabaseIdentity, observed.ObservedFinalizedVersion, observed.ModuleActive);
    }

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

        foreach (var declaration in declarations.Where(declaration => declaration.Defects.Count > 0))
            logger.LogError(
                "Schema family '{Family}' declared in {Assembly} has an upcaster chain this build refuses (spec 180, FR-005): {Defects} " +
                "This host reports only the versions the chain still reaches: {Readable}.",
                declarations.Key,
                declaration.Assembly.GetName().Name,
                string.Join(" ", declaration.Defects),
                string.Join(", ", declaration.ReadableVersions.Select(version => $"'{version}'")));

        var readable = declarations
            .Select(declaration => declaration.ReadableVersions.AsEnumerable())
            .Aggregate((versions, next) => versions.Intersect(next, StringComparer.Ordinal));

        return new ReadabilityEntry(declarations.Key, modules[0], readable);
    }
}
