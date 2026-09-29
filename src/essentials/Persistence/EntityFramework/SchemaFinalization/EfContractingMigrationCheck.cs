using Elsa.Persistence.Schema.SchemaFinalization;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.EntityFramework.SchemaFinalization;

/// <summary>
/// The apply-time half of the expand-only migration guard (spec 185, FR-024 and FR-025, User Story 5): a pending
/// contracting migration, one whose <see cref="ExpandOnlyMigrationOptOutAttribute"/> names a schema family and a
/// version, is not applied until the family's finalized version in the target database is that version or a later one.
/// The build guard decides what may merge; this decides what may run, wherever a module's migrations are applied or
/// validated: <see cref="EfDatabaseMigrator"/> under both policies, and so <see cref="EfModuleMigrator{TContext}"/> at
/// Prepare and <c>dotnet elsa persistence apply</c>, and the enable-time activation guard under both policies.
/// </summary>
/// <remarks>
/// <para>
/// <b>The invariant.</b> No operation of a contracting migration that <see cref="EfDatabaseMigrator"/> applies runs
/// until the family's finalization record in the target database exists at the version the migration names or later.
/// Since a finalized version only moves forward (spec 181, FR-003) and a gate refuses a host that cannot read it
/// (FR-015), every host that reads only versions before that one is refused from then on, whichever order hosts start
/// in. Two paths make it hold, chosen by whether a gate-aware host has admitted the module in the database, which a
/// family's record there shows, unless every record is the migrator's own seed of the pending batch, cut short before its
/// last family's record and untouched since:
/// </para>
/// <list type="bullet">
/// <item><b>Admitted.</b> The check runs before <c>MigrateAsync</c> and refuses the context's whole pending batch, which
/// is what <c>MigrateAsync</c> would apply, while a family is below the version; the batch can only shrink between the
/// check and the migration lock, never grow, and a family found at or past the version stays there. Once the module has
/// been admitted, a family with no record is refused, since nothing then shows its version finalized.</item>
/// <item><b>Not admitted: the migrator seeds first</b> (owner decision on #2093, 2026-09-29; #2136). It applies the
/// pending migrations before the first pending contracting one; creates each contracted family's record, and the
/// database identity, at the version its contracting migrations name, as <c>migrator:&lt;host id or machine name&gt;</c>;
/// reads every record again, refusing as the admitted path does when one is below, because a racing release's gate
/// created it first; and only then applies the contracting migrations and the rest. A record is created by one insert
/// keyed by its family, so of a seed and a racing gate one creates it and the other reads it: either the racing gate
/// is refused, or the re-check refuses the contraction. A process that ends after the identity and before the first
/// record leaves no record, and one that ends between two families' records, or between the seed and the contraction,
/// leaves only records its own seed created, untouched, at the versions it creates them at: either way the next apply
/// seeds what is missing, keeps what exists, and completes. Once a record exists that a member or an operator created,
/// or that has changed since, or that the migrator created for a contraction that has run, the module counts as
/// admitted and a family with no record is refused. A family the build or its chain cannot place is refused before
/// anything runs, so an unplaceable version is never seeded.</item>
/// </list>
/// <para>
/// <b>What is not covered.</b> SQL run outside Elsa, such as the script <c>dotnet elsa persistence script</c> writes for
/// a DBA, creates no record. <see cref="SeedVersionAsync"/> remains for that case: the gate creates a missing record no
/// lower than the version an applied contracting migration names, when the first host to admit the module carries it.
/// </para>
/// <para>
/// A module with no contracting migration costs nothing here: the opt-outs are read as assembly metadata, by name, as
/// <see cref="EfSchemaFamilyCatalog"/> reads declarations, and no database is opened. The migrations-history table is
/// read only when a contracting migration's family is below its version, to learn whether it is pending, on a database
/// no host has admitted the module in, to learn what to seed, where every record is one the migrator created and nothing
/// has changed since, to learn whether it is the migrator's own seed cut short, and when a gate creates the record of a
/// family a contracting migration names, to learn whether it has been applied.
/// </para>
/// </remarks>
public static class EfContractingMigrationCheck
{
    /// <summary>
    /// How the host id of the member that creates a record on a migrator's behalf begins (spec 181, 2026-09-29 note):
    /// <c>migrator:</c> and then the host's id in the fleet, or the machine name where the host composes none or the
    /// persistence tool applies.
    /// </summary>
    public const string MigratorHostIdPrefix = "migrator:";

    private static readonly string OptOutAttributeName = typeof(ExpandOnlyMigrationOptOutAttribute).FullName!;

    /// <summary>
    /// The refusal of <paramref name="context"/>'s pending batch, or null when every pending contracting migration may be
    /// applied. A record that exists but cannot be read throws, so a caller fails closed. On a database no host has
    /// admitted the module in, only a contracting migration whose family or version this build cannot place is refused:
    /// applying the batch there creates the family's record first.
    /// </summary>
    /// <param name="pending">The context's pending migration ids when the caller has read them already; read here otherwise, and only when needed.</param>
    public static async Task<EfContractingMigrationRefusedException?> FindRefusalAsync(
        DbContext context,
        IReadOnlyCollection<string>? pending = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var contracting = ContractingMigrations(context);
        if (contracting.Count == 0)
            return null;

        var families = EfSchemaModuleFamilies.ForContext(context.GetType());
        var unsafeMigrations = await IsAdmittedAsync(context, contracting, families, pending, cancellationToken)
            ? await JudgeAsync(contracting, families, new EfSchemaFinalizationStore(context), cancellationToken)
            : contracting.Select(migration => Place(migration, families).Refusal).OfType<EfContractingMigrationRefusal>().ToArray();
        return unsafeMigrations.Count == 0 ? null : await RefuseBatchAsync(context, families, unsafeMigrations, pending, cancellationToken);
    }

    /// <summary>
    /// Applies <paramref name="context"/>'s pending migrations, as <see cref="EfDatabaseMigrator"/> does under
    /// <see cref="EfMigratePolicy.AutoMigrate"/>: refused while a pending contracting migration's family is finalized
    /// below the version it names, and, on a database no host has admitted the module in, with each contracted family's
    /// record created before any contracting migration runs.
    /// </summary>
    /// <param name="host">This host's member in the fleet, which a created record names prefixed with <see cref="MigratorHostIdPrefix"/>; the machine name when null.</param>
    /// <exception cref="EfContractingMigrationRefusedException">A pending contracting migration may not be applied yet.</exception>
    internal static Task MigrateAsync(DbContext context, SchemaFinalizationMember? host, CancellationToken cancellationToken) =>
        MigrateAsync(context, host, EfSchemaModuleFamilies.ForContext(context.GetType()), cancellationToken);

    /// <summary><see cref="MigrateAsync(DbContext, SchemaFinalizationMember?, CancellationToken)"/> as a build that declares <paramref name="families"/> runs it.</summary>
    internal static async Task MigrateAsync(DbContext context, SchemaFinalizationMember? host, EfSchemaModuleFamilies? families, CancellationToken cancellationToken)
    {
        var contracting = ContractingMigrations(context);
        if (contracting.Count > 0)
        {
            if (!await IsAdmittedAsync(context, contracting, families, pending: null, cancellationToken))
                await SeedAsync(context, contracting, families, host, cancellationToken);
            else if (await JudgeAsync(contracting, families, new EfSchemaFinalizationStore(context), cancellationToken) is { Count: > 0 } unsafeMigrations &&
                     await RefuseBatchAsync(context, families, unsafeMigrations, pending: null, cancellationToken) is { } refusal)
                throw refusal;
        }

        await context.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// The version the module's gate creates <paramref name="chain"/>'s family's record at in <paramref name="context"/>'s
    /// database (spec 181, Edge Cases, "A database with no record yet"): the oldest version this host reads, or, when a
    /// contracting migration of the family has been applied there, the latest version any such migration names, if that
    /// is later. The schema serves no version before it. The migrations-history table is read only when the module has a
    /// contracting migration that names the family or names no family.
    /// </summary>
    /// <exception cref="EfSchemaActivationRefusedException">
    /// An applied contracting migration of the family names a version this host's chain does not read, or names no
    /// version or no family, so where the record would start cannot be placed. Refused rather than started at the oldest
    /// version this host reads, which the schema may no longer serve.
    /// </exception>
    public static async Task<string> SeedVersionAsync(DbContext context, string module, EfSchemaChain chain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(chain);
        var readable = chain.ReadableVersions;
        var contracting = ContractingMigrations(context)
            .Where(migration => migration.Family is null || StringComparer.Ordinal.Equals(migration.Family, chain.Family))
            .ToArray();
        if (contracting.Length == 0)
            return readable[0];

        var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var seed = 0;
        // An opt-out that names no family names no version of this one either.
        foreach (var version in contracting.Where(migration => applied.Contains(migration.Id)).Select(migration => migration.Family is null ? null : migration.Version))
        {
            var at = version is null ? -1 : IndexOf(readable, version);
            if (at < 0)
                throw new EfSchemaActivationRefusedException(module, chain.Family, EfSchemaActivationRefusal.ContractedUnreadable, version, readable);
            seed = Math.Max(seed, at);
        }

        return readable[seed];
    }

    /// <summary>
    /// Applies <paramref name="context"/>'s pending migrations that come before <paramref name="migration"/> in the order
    /// EF applies them, and no others. <c>IMigrator.MigrateAsync(target)</c> is not used: it reverts every applied
    /// migration after its target, and another host's migrator may have applied past it by the time EF's migration lock
    /// is taken. The context's own migrator type runs instead, over a view of the module's migrations that ends before
    /// <paramref name="migration"/>, so it applies what that view holds and reverts nothing it does not.
    /// </summary>
    internal static async Task MigrateBeforeAsync(DbContext context, string migration, CancellationToken cancellationToken)
    {
        var migrator = (IMigrator)ActivatorUtilities.CreateInstance(
            context.GetInfrastructure(),
            context.GetService<IMigrator>().GetType(),
            new MigrationsBefore(context.GetService<IMigrationsAssembly>(), migration));
        await migrator.MigrateAsync(cancellationToken: cancellationToken);
    }

    /// <summary>
    /// The path for a database no host has admitted the module in: applies the pending migrations before the first pending
    /// contracting one, creates each contracted family's record, and reads the records again, leaving the contracting
    /// migrations and the rest to the caller's <c>MigrateAsync</c>.
    /// </summary>
    private static async Task SeedAsync(
        DbContext context,
        IReadOnlyList<ContractingMigration> contracting,
        EfSchemaModuleFamilies? families,
        SchemaFinalizationMember? host,
        CancellationToken cancellationToken)
    {
        var module = ModuleOf(context, families);
        var (pending, seeding) = await PlanSeedAsync(context, contracting, families, pending: null, cancellationToken);
        if (seeding.Length == 0)
            return;
        if (seeding.Select(entry => entry.Placement.Refusal).OfType<EfContractingMigrationRefusal>().ToArray() is { Length: > 0 } unplaceable)
            throw new EfContractingMigrationRefusedException(module, pending, unplaceable);

        var first = seeding[0].Migration.Id;
        var applied = pending.TakeWhile(id => !StringComparer.Ordinal.Equals(id, first)).ToArray();
        if (applied.Length > 0)
            await MigrateBeforeAsync(context, first, cancellationToken);
        if (!await EfSchemaFinalizationCheck.RecordTableExistsAsync(context, cancellationToken))
            throw new InvalidOperationException(
                $"EF module '{module}' was not migrated past '{first}': its migrations before that contracting migration do not " +
                "create the finalization record table, so the record a contracting migration waits for cannot be created " +
                "before it runs (spec 181, FR-002).");

        var store = new EfSchemaFinalizationStore(context);
        var creator = SchemaFinalizationActor.Of(MigratorMember(host));
        // A record that exists, the migrator's own from a seed cut short or a racing gate's, is kept as it is.
        foreach (var family in ByFamily(seeding))
        {
            var chain = family[0].Chain!;
            await store.GetOrCreateAsync(chain.Family, await SeedTargetAsync(context, module, family, cancellationToken), chain.ReadableVersions, creator, cancellationToken);
        }

        // A racing release's gate may have created a record first, below the version: then the contraction may not run.
        if (await JudgeAsync(seeding.Select(entry => entry.Migration), families, store, cancellationToken) is { Count: > 0 } refusals)
            throw new EfContractingMigrationRefusedException(module, pending.Skip(applied.Length).ToArray(), refusals, applied);
    }

    /// <summary>The refusal of the pending batch for those of <paramref name="unsafeMigrations"/> it holds, or null when it holds none.</summary>
    private static async Task<EfContractingMigrationRefusedException?> RefuseBatchAsync(
        DbContext context,
        EfSchemaModuleFamilies? families,
        IReadOnlyList<EfContractingMigrationRefusal> unsafeMigrations,
        IReadOnlyCollection<string>? pending,
        CancellationToken cancellationToken)
    {
        var batch = (pending ?? (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray()).ToArray();
        var refused = unsafeMigrations.Where(refusal => batch.Contains(refusal.Migration, StringComparer.Ordinal)).ToArray();
        return refused.Length == 0 ? null : new EfContractingMigrationRefusedException(ModuleOf(context, families), batch, refused);
    }

    /// <summary>
    /// Whether the module has been admitted in the database, so that a family with no record there is refused rather than
    /// seeded. It has not while no family has a record: a gate creates the database identity and then each family's
    /// record before the module serves anything, and the seed does the same, so an identity with no record is an
    /// admission or a seed that ended before its first record, and nothing has read or written the module's rows. Nor has
    /// it while every record is the migrator's own seed of this pending batch, cut short before its last family's record
    /// (#2136): created by a <see cref="MigratorHostIdPrefix"/> member, unchanged since, of a family a pending contracting
    /// migration this build places names, at the version this seed creates it at. The next apply completes that seed.
    /// </summary>
    /// <remarks>
    /// Any other record counts as an admission, the loud direction: one a member or an operator created, one changed
    /// since, and one the migrator created for a contraction that has run. A gate that admits a module whose families all
    /// have a record creates none and leaves no trace, so a record the migrator created is no evidence that no gate has
    /// admitted the module once its own contraction may have run. Each record's revision and family are read first, so a
    /// record changed since, or of a family no contracting migration names, is neither parsed nor followed by a read of
    /// the migrations-history table.
    /// </remarks>
    private static async Task<bool> IsAdmittedAsync(
        DbContext context,
        IReadOnlyList<ContractingMigration> contracting,
        EfSchemaModuleFamilies? families,
        IReadOnlyCollection<string>? pending,
        CancellationToken cancellationToken)
    {
        if (!await EfSchemaFinalizationCheck.RecordTableExistsAsync(context, cancellationToken))
            return false;
        var rows = await context.Set<EfSchemaFinalizationRecordRow>()
            .AsNoTracking()
            .Select(row => new { row.Family, row.Revision })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return false;
        if (rows.Any(row => row.Revision != 1 || !contracting.Any(migration => StringComparer.Ordinal.Equals(migration.Family, row.Family))))
            return true;

        var store = new EfSchemaFinalizationStore(context);
        var records = new List<SchemaFinalizationRecord>();
        foreach (var row in rows)
        {
            if (await store.FindAsync(row.Family, cancellationToken) is not { History: [{ Transition: SchemaFinalizationTransition.Created, Actor.Member.HostId: var creator }, ..] } record ||
                !creator.StartsWith(MigratorHostIdPrefix, StringComparison.Ordinal))
                return true;
            records.Add(record);
        }

        var (_, seeding) = await PlanSeedAsync(context, contracting, families, pending, cancellationToken);
        if (seeding.Any(entry => entry.Placement.Refusal is not null))
            return true;
        var seeded = ByFamily(seeding).ToDictionary(family => family[0].Chain!.Family, StringComparer.Ordinal);
        var module = ModuleOf(context, families);
        foreach (var record in records)
        {
            if (!seeded.TryGetValue(record.Family, out var family) ||
                !StringComparer.Ordinal.Equals(record.FinalizedVersion, await SeedTargetAsync(context, module, family, cancellationToken)))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The context's pending migrations, and those of them that are contracting, in the order EF applies them, which is
    /// the pending order, each placed on its family's chain in this build.
    /// </summary>
    private static async Task<(string[] Pending, (ContractingMigration Migration, Placement Placement)[] Seeding)> PlanSeedAsync(
        DbContext context,
        IReadOnlyList<ContractingMigration> contracting,
        EfSchemaModuleFamilies? families,
        IReadOnlyCollection<string>? pending,
        CancellationToken cancellationToken)
    {
        var batch = (pending ?? await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
        var seeding = batch
            .Select(id => contracting.FirstOrDefault(migration => StringComparer.Ordinal.Equals(migration.Id, id)))
            .OfType<ContractingMigration>()
            .Select(migration => (Migration: migration, Placement: Place(migration, families)))
            .ToArray();
        return (batch, seeding);
    }

    /// <summary>The placements of <paramref name="seeding"/>, which all place, by family, in the order of each family's first contracting migration.</summary>
    private static IEnumerable<Placement[]> ByFamily(IEnumerable<(ContractingMigration Migration, Placement Placement)> seeding) =>
        seeding.GroupBy(entry => entry.Placement.Chain!.Family, entry => entry.Placement, StringComparer.Ordinal).Select(family => family.ToArray());

    /// <summary>
    /// The version the seed creates a family's record at, <paramref name="family"/> being the placements of its pending
    /// contracting migrations: the latest version any of them names, and no lower than what an applied one already left
    /// the schema serving, which is where the gate would start the record once the batch has applied.
    /// </summary>
    private static async Task<string> SeedTargetAsync(DbContext context, string module, IReadOnlyList<Placement> family, CancellationToken cancellationToken)
    {
        var chain = family[0].Chain!;
        var at = Math.Max(
            IndexOf(chain.ReadableVersions, await SeedVersionAsync(context, module, chain, cancellationToken)),
            family.Max(placement => placement.At));
        return chain.ReadableVersions[at];
    }

    /// <summary>The refusal of each of <paramref name="migrations"/> the family's record in the database does not yet allow.</summary>
    private static async Task<IReadOnlyList<EfContractingMigrationRefusal>> JudgeAsync(
        IEnumerable<ContractingMigration> migrations,
        EfSchemaModuleFamilies? families,
        EfSchemaFinalizationStore store,
        CancellationToken cancellationToken)
    {
        var records = new Dictionary<string, SchemaFinalizationRecord?>(StringComparer.Ordinal);
        var refusals = new List<EfContractingMigrationRefusal>();
        foreach (var migration in migrations)
        {
            var placement = Place(migration, families);
            if (placement.Refusal is { } unplaceable)
            {
                refusals.Add(unplaceable);
                continue;
            }

            var chain = placement.Chain!;
            if (!records.TryGetValue(chain.Family, out var record))
                records[chain.Family] = record = await store.FindAsync(chain.Family, cancellationToken);
            if (record is null)
                refusals.Add(Refuse(migration, finalized: null, EfContractingMigrationRefusalReason.NoRecord));
            // A finalized version this build's chain does not place is either older than anything it reads, and so below
            // the required version, or newer than it can read, which the gate refuses the module for anyway: refused both ways.
            else if (IndexOf(chain.ReadableVersions, record.FinalizedVersion) < placement.At)
                refusals.Add(Refuse(migration, record.FinalizedVersion, EfContractingMigrationRefusalReason.NotFinalized));
        }

        return refusals;
    }

    /// <summary>Where <paramref name="migration"/>'s named version sits on its family's chain in this build, or why it cannot be placed.</summary>
    private static Placement Place(ContractingMigration migration, EfSchemaModuleFamilies? families)
    {
        if (migration.Family is not { } family || migration.Version is not { } required)
            return new(null, -1, Refuse(migration, finalized: null, EfContractingMigrationRefusalReason.Incomplete));
        if (families?.Chains.FirstOrDefault(chain => StringComparer.Ordinal.Equals(chain.Family, family)) is not { } chain)
            return new(null, -1, Refuse(migration, finalized: null, EfContractingMigrationRefusalReason.UnknownFamily));
        var at = IndexOf(chain.ReadableVersions, required);
        return at < 0
            ? new(null, -1, Refuse(migration, finalized: null, EfContractingMigrationRefusalReason.UnknownVersion))
            : new(chain, at, null);
    }

    /// <summary>The member a record created on a migrator's behalf names: <see cref="MigratorHostIdPrefix"/> and the host's id, in the host's incarnation.</summary>
    private static SchemaFinalizationMember MigratorMember(SchemaFinalizationMember? host) =>
        new(MigratorHostIdPrefix + (host?.HostId ?? Environment.MachineName), host?.Incarnation ?? EfSchemaModuleGate.ProcessIncarnation);

    private static string ModuleOf(DbContext context, EfSchemaModuleFamilies? families) => families?.Module ?? context.GetType().Name;

    private static EfContractingMigrationRefusal Refuse(ContractingMigration migration, string? finalized, EfContractingMigrationRefusalReason reason) =>
        new(migration.Id, migration.Family, migration.Version, finalized, reason);

    private static int IndexOf(IReadOnlyList<string> versions, string version)
    {
        for (var index = 0; index < versions.Count; index++)
        {
            if (StringComparer.Ordinal.Equals(versions[index], version))
                return index;
        }

        return -1;
    }

    /// <summary>
    /// Every migration of <paramref name="context"/>'s migrations assembly whose opt-out names a schema family or a
    /// version, in id order. The attribute is matched by its full name and read as metadata, never constructed.
    /// </summary>
    private static IReadOnlyList<ContractingMigration> ContractingMigrations(DbContext context) =>
        context.GetService<IMigrationsAssembly>().Migrations
            .Select(migration => (Id: migration.Key, OptOut: migration.Value.GetCustomAttributesData()
                .FirstOrDefault(attribute => StringComparer.Ordinal.Equals(attribute.AttributeType.FullName, OptOutAttributeName))))
            .Where(migration => migration.OptOut is not null)
            .Select(migration => new ContractingMigration(
                migration.Id,
                Named(migration.OptOut!, nameof(ExpandOnlyMigrationOptOutAttribute.SchemaFamily)),
                Named(migration.OptOut!, nameof(ExpandOnlyMigrationOptOutAttribute.FinalizedVersion))))
            .Where(migration => migration.Family is not null || migration.Version is not null)
            .OrderBy(migration => migration.Id, StringComparer.Ordinal)
            .ToArray();

    private static string? Named(CustomAttributeData attribute, string member) =>
        attribute.NamedArguments.FirstOrDefault(argument => argument.MemberName == member).TypedValue.Value as string;

    private sealed record ContractingMigration(string Id, string? Family, string? Version);

    /// <summary>A contracting migration's family chain and the position of the version it names, or the refusal when neither can be placed.</summary>
    private readonly record struct Placement(EfSchemaChain? Chain, int At, EfContractingMigrationRefusal? Refusal);

    /// <summary>
    /// The module's migrations that EF orders before <paramref name="end"/>, and nothing else: a migrator over this view
    /// applies only those and reverts nothing it does not know.
    /// </summary>
    private sealed class MigrationsBefore(IMigrationsAssembly migrations, string end) : IMigrationsAssembly
    {
        public IReadOnlyDictionary<string, TypeInfo> Migrations { get; } =
            migrations.Migrations.TakeWhile(migration => !StringComparer.Ordinal.Equals(migration.Key, end)).ToDictionary(StringComparer.Ordinal);

        public ModelSnapshot? ModelSnapshot => migrations.ModelSnapshot;

        public Assembly Assembly => migrations.Assembly;

        public string? FindMigrationId(string nameOrId) =>
            migrations.FindMigrationId(nameOrId) is { } id && Migrations.ContainsKey(id) ? id : null;

        public Migration CreateMigration(TypeInfo migrationClass, string activeProvider) => migrations.CreateMigration(migrationClass, activeProvider);
    }
}
