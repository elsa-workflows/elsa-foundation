using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Where an EF module keeps the finalization record of its schema families (spec 181, FR-001 and FR-002): two tables
/// beside the module's own migrations-history table, created by the module's own baseline migration, so
/// <see cref="EfMigratePolicy.Validate"/> refuses a module whose database lacks them. Every module context maps them
/// with <see cref="MapSchemaFinalization"/>, whichever membership provider a host composes.
/// </summary>
/// <remarks>
/// <para>
/// One table holds a row per schema family: the finalized version, the intent in flight, the holds, the finish record
/// and their histories. The other holds one row, the opaque database identity (spec 181, Decisions, Q11). Both are
/// named after the module's history module, so modules that share a database never share them, and so the identity is
/// per module and database: every family of one module in one database carries the same identity, and the same module
/// in another database carries another.
/// </para>
/// <para>
/// Both tables belong to their own schema family, <see cref="SchemaFamily"/>, whatever module maps them. Their stamp is
/// checked by <see cref="EfSchemaFinalizationStore"/> before it reads anything else from a row, and
/// <see cref="EfSchemaVersionMaterializationInterceptor"/> leaves them alone on a context that declares a family of
/// its own.
/// </para>
/// </remarks>
public static class EfSchemaFinalization
{
    /// <summary>The schema family both finalization tables belong to, in every module.</summary>
    public const string SchemaFamily = "SchemaFinalization";

    /// <summary>The version this build stamps on every finalization row it writes and reads without skew.</summary>
    public const string SchemaVersion = "1.0.0";

    /// <summary>The per-family record table's name, before the history module name.</summary>
    public const string RecordTablePrefix = "__ElsaSchemaFinalization_";

    /// <summary>The database identity table's name, before the history module name.</summary>
    public const string DatabaseIdentityTablePrefix = "__ElsaDatabaseIdentity_";

    /// <summary>
    /// The longest table name every supported engine keeps whole. PostgreSQL truncates a longer identifier with only a
    /// notice, so two long module names could silently share a table; a longer name is refused instead.
    /// </summary>
    public const int MaxTableNameLength = 63;

    /// <summary>The longest schema family name a record is keyed by.</summary>
    public const int MaxFamilyLength = 128;

    /// <summary>The longest version label a record holds: the width of every schema-version stamp column.</summary>
    public const int MaxVersionLength = 32;

    /// <summary>The per-family record table of the module whose migrations-history module is <paramref name="module"/>.</summary>
    public static string RecordTableName(string module) => TableName(RecordTablePrefix, module);

    /// <summary>The database identity table of the module whose migrations-history module is <paramref name="module"/>.</summary>
    public static string DatabaseIdentityTableName(string module) => TableName(DatabaseIdentityTablePrefix, module);

    /// <summary>
    /// Maps both finalization tables into a module's model, named after <paramref name="module"/>, the module's
    /// migrations-history module name. A module context calls this after its own tables and just before its provider
    /// configuration, so the module's ordinal collation, where it declares one, covers the family key as it covers the
    /// module's own keys, and <see cref="EfSchemaVersion.IndexSchemaVersionStamps"/> then indexes both stamps.
    /// </summary>
    public static ModelBuilder MapSchemaFinalization(this ModelBuilder modelBuilder, string module)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<EfSchemaFinalizationRecordRow>(record =>
        {
            record.ToTable(RecordTableName(module));
            record.HasKey(row => row.Family);
            record.Property(row => row.Family).HasMaxLength(MaxFamilyLength).IsRequired();
            record.Property(row => row.SchemaVersion).HasMaxLength(MaxVersionLength).IsRequired();
            record.Property(row => row.DatabaseIdentity).HasMaxLength(EfSchemaFinalizationStore.DatabaseIdentityLength).IsRequired();
            record.Property(row => row.Revision).IsRequired().IsConcurrencyToken();
            record.Property(row => row.FinalizedVersion).HasMaxLength(MaxVersionLength).IsRequired();
            record.Property(row => row.IntentJson);
            record.Property(row => row.HoldsJson).IsRequired();
            record.Property(row => row.HistoryJson).IsRequired();
            record.Property(row => row.FinishJson);
            record.Property(row => row.FinishHistoryJson).IsRequired();
        });
        modelBuilder.Entity<EfDatabaseIdentityRow>(identity =>
        {
            identity.ToTable(DatabaseIdentityTableName(module));
            identity.HasKey(row => row.Id);
            identity.Property(row => row.Id).ValueGeneratedNever();
            identity.Property(row => row.DatabaseIdentity).HasMaxLength(EfSchemaFinalizationStore.DatabaseIdentityLength).IsRequired();
            identity.Property(row => row.SchemaVersion).HasMaxLength(MaxVersionLength).IsRequired();
        });
        return modelBuilder;
    }

    /// <summary>
    /// Whether <paramref name="clrType"/> is one of the finalization tables' row types, which every module context maps
    /// beside its own entities.
    /// </summary>
    public static bool Maps(Type clrType) =>
        clrType == typeof(EfSchemaFinalizationRecordRow) || clrType == typeof(EfDatabaseIdentityRow);

    private static string TableName(string prefix, string module)
    {
        // The history table's own validation, so a module name is either accepted for all three tables or for none.
        _ = EfMigrationsHistory.TableName(module);
        var name = prefix + module;
        if (name.Length > MaxTableNameLength)
            throw new ArgumentException(
                $"The finalization table '{name}' is longer than the {MaxTableNameLength} characters every supported engine keeps whole. Use a shorter history module name.",
                nameof(module));
        return name;
    }
}
