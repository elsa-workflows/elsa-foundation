namespace Elsa.Persistence.EntityFramework.SchemaFinalization;

/// <summary>
/// One schema family's finalization record in one module's database (spec 181, FR-001). The version columns are the
/// state an operator reads at a glance; the structured parts are JSON the store reads only after checking
/// <see cref="SchemaVersion"/>.
/// </summary>
internal sealed class EfSchemaFinalizationRecordRow
{
    public string Family { get; set; } = null!;

    /// <summary>The persisted-schema version of this row: <see cref="EfSchemaFinalization.SchemaVersion"/>.</summary>
    public string SchemaVersion { get; set; } = null!;

    /// <summary>A copy of the module's database identity, taken when the record was created and never changed.</summary>
    public string DatabaseIdentity { get; set; } = null!;

    /// <summary>Raised by exactly one on every change, and compared on every change, so every change is a compare-and-set.</summary>
    public long Revision { get; set; }

    public string FinalizedVersion { get; set; } = null!;

    public string? IntentJson { get; set; }

    public string HoldsJson { get; set; } = null!;

    public string HistoryJson { get; set; } = null!;

    public string? FinishJson { get; set; }

    public string FinishHistoryJson { get; set; } = null!;
}

/// <summary>The one row that holds a module's opaque database identity (spec 181, Decisions, Q11).</summary>
internal sealed class EfDatabaseIdentityRow
{
    /// <summary>Always <see cref="EfSchemaFinalizationStore.DatabaseIdentityRowId"/>, so a second insert fails on the key instead of adding a second identity.</summary>
    public int Id { get; set; }

    public string DatabaseIdentity { get; set; } = null!;

    /// <summary>The persisted-schema version of this row: <see cref="EfSchemaFinalization.SchemaVersion"/>.</summary>
    public string SchemaVersion { get; set; } = null!;
}
