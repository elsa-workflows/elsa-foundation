using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

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
/// <b>The invariant.</b> No operation of a contracting migration runs against a database in which a gate-aware host has
/// admitted the module while the migration's family is finalized below the version it names. It holds because the
/// check runs before <c>MigrateAsync</c> and refuses the context's whole pending batch, which is what
/// <c>MigrateAsync</c> would apply; because the batch can only shrink between the check and the migration lock, never
/// grow; and because a finalized version only moves forward (spec 181, FR-003), so a family found at or past the
/// version stays there.
/// </para>
/// <para>
/// <b>A database no host has admitted the module in is not refused.</b> Before the module's record table exists, or its
/// database identity, which a gate creates when it first admits the module, no gate-aware host has read or written the
/// module's rows there, so nothing reads what the batch removes, and a fresh install of a release that carries a
/// contracting migration must not be refused forever: nothing could ever create the record the check waits for. Once
/// the module has been admitted, a family with no record is refused, since nothing then shows its version finalized.
/// </para>
/// <para>
/// <b>What such a database's record starts at.</b> A contracting migration applied there has removed what every version
/// before the one it names reads, so the schema no longer serves those versions, and a record created at the admitting
/// host's oldest readable version would admit a host that reads only them. <see cref="SeedVersionAsync"/> is the version
/// the gate creates a family's record at instead: the latest version any applied contracting migration of the family
/// names, when that is later than the oldest version the host reads (#2136). It is derived from the host's own
/// migrations, so it holds when the first host to admit the module carries the contracting migration, as the host that
/// applied it does; a build older than the migration that admits the module first knows nothing of it.
/// </para>
/// <para>
/// A module with no contracting migration costs nothing here: the opt-outs are read as assembly metadata, by name, as
/// <see cref="EfSchemaFamilyCatalog"/> reads declarations, and no database is opened. The migrations-history table is
/// read only when a contracting migration's family is below its version, to learn whether it is pending, and when a
/// gate creates the record of a family a contracting migration names, to learn whether it has been applied.
/// </para>
/// </remarks>
public static class EfContractingMigrationCheck
{
    private static readonly string OptOutAttributeName = typeof(ExpandOnlyMigrationOptOutAttribute).FullName!;

    /// <summary>
    /// The refusal of <paramref name="context"/>'s pending batch, or null when every pending contracting migration may be
    /// applied. A record that exists but cannot be read throws, so a caller fails closed.
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
        var module = families?.Module ?? context.GetType().Name;
        if (!await EfSchemaFinalizationCheck.RecordTableExistsAsync(context, cancellationToken))
            return null;
        var store = new EfSchemaFinalizationStore(context);
        if (await store.FindDatabaseIdentityAsync(cancellationToken) is null)
            return null;

        var records = new Dictionary<string, SchemaFinalizationRecord?>(StringComparer.Ordinal);
        var unsafeMigrations = new List<EfContractingMigrationRefusal>();
        foreach (var migration in contracting)
        {
            if (await JudgeAsync(migration, families, store, records, cancellationToken) is { } refusal)
                unsafeMigrations.Add(refusal);
        }

        if (unsafeMigrations.Count == 0)
            return null;

        var batch = (pending ?? (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray()).ToArray();
        var refused = unsafeMigrations.Where(refusal => batch.Contains(refusal.Migration, StringComparer.Ordinal)).ToArray();
        return refused.Length == 0 ? null : new EfContractingMigrationRefusedException(module, batch, refused);
    }

    private static async Task<EfContractingMigrationRefusal?> JudgeAsync(
        ContractingMigration migration,
        EfSchemaModuleFamilies? families,
        EfSchemaFinalizationStore store,
        Dictionary<string, SchemaFinalizationRecord?> records,
        CancellationToken cancellationToken)
    {
        if (migration.Family is not { } family || migration.Version is not { } required)
            return Refuse(migration, finalized: null, EfContractingMigrationRefusalReason.Incomplete);
        if (families?.Chains.FirstOrDefault(chain => StringComparer.Ordinal.Equals(chain.Family, family)) is not { } chain)
            return Refuse(migration, finalized: null, EfContractingMigrationRefusalReason.UnknownFamily);
        var requiredAt = IndexOf(chain.ReadableVersions, required);
        if (requiredAt < 0)
            return Refuse(migration, finalized: null, EfContractingMigrationRefusalReason.UnknownVersion);

        if (!records.TryGetValue(family, out var record))
            records[family] = record = await store.FindAsync(family, cancellationToken);
        if (record is null)
            return Refuse(migration, finalized: null, EfContractingMigrationRefusalReason.NoRecord);
        // A finalized version this build's chain does not place is either older than anything it reads, and so below
        // the required version, or newer than it can read, which the gate refuses the module for anyway: refused both ways.
        return IndexOf(chain.ReadableVersions, record.FinalizedVersion) >= requiredAt
            ? null
            : Refuse(migration, record.FinalizedVersion, EfContractingMigrationRefusalReason.NotFinalized);
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
        foreach (var migration in contracting.Where(migration => applied.Contains(migration.Id)))
        {
            // An opt-out that names no family names no version of this one either.
            var version = migration.Family is null ? null : migration.Version;
            var at = version is null ? -1 : IndexOf(readable, version);
            if (at < 0)
                throw new EfSchemaActivationRefusedException(module, chain.Family, EfSchemaActivationRefusal.ContractedUnreadable, version, readable);
            seed = Math.Max(seed, at);
        }

        return readable[seed];
    }

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

    private static string? Named(System.Reflection.CustomAttributeData attribute, string member) =>
        attribute.NamedArguments.FirstOrDefault(argument => argument.MemberName == member).TypedValue.Value as string;

    private sealed record ContractingMigration(string Id, string? Family, string? Version);
}
