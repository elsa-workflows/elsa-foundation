using System.Data;
using System.Data.Common;

namespace Elsa.Etl.Core.Runtime;

/// <summary>
/// The per-provider adapter that isolates one SQL driver from the ETL runtime (specs 1197 and 1198). The runtime's writer and
/// snapshot reader own the protocols; an engine supplies only connections, dialect text, isolation and error classification.
/// </summary>
/// <remarks>
/// An adapter keyed by <see cref="Provider"/>, not a fan-in contributor: several engines may be registered, and each SQL
/// binding resolves exactly the one its provider names. Every column passed in has been validated as
/// <c>[A-Za-z_][A-Za-z0-9_]{0,62}</c>, and every table, ledger included, as <c>name</c> or <c>schema.name</c> of such parts.
/// </remarks>
public interface IEtlSqlEngine
{
    /// <summary>The provider name bindings select, in lower case, such as <c>postgresql</c> or <c>sqlserver</c>.</summary>
    string Provider { get; }

    /// <summary>The most parameters one command may carry.</summary>
    int MaxParameters { get; }

    /// <summary>Creates an unopened connection.</summary>
    DbConnection CreateConnection(string connectionString);

    /// <summary>The connection's target without any secret, for the binding's pinned handle.</summary>
    EtlSqlTarget DescribeTarget(string connectionString);

    /// <summary>A statement to run on a new connection before its transaction starts, or null.</summary>
    string? SessionSetup { get; }

    /// <summary>Idempotent DDL creating the ledger table when it is missing.</summary>
    string CreateLedger(string ledgerTable);

    /// <summary>Inserts one ledger row from <c>@effect</c>, <c>@run</c>, <c>@batch</c>, <c>@hash</c> and <c>@applied</c>.</summary>
    string InsertLedger(string ledgerTable);

    /// <summary>Selects the content hash recorded for <c>@effect</c>.</summary>
    string ReadLedgerHash(string ledgerTable);

    /// <summary>
    /// Counts unique constraints or indexes on exactly the key columns of the table, or returns -1 when the table does not
    /// exist or is not a base table, so a missing table is not mistaken for a missing key.
    /// </summary>
    string CountUniqueKeys(string table, IReadOnlyList<string> keyColumns);

    /// <summary>
    /// One single-row upsert whose parameters are <see cref="Parameter"/> of each column for the given row number; an
    /// existing row with the same key has every non-key column updated.
    /// </summary>
    string Upsert(string table, IReadOnlyList<string> columns, IReadOnlyList<string> keyColumns, int row);

    /// <summary>The parameter name of a column's value in an upsert row.</summary>
    string Parameter(string column, int row);

    /// <summary>The isolation level at which one transaction reads a whole table as of one point in time.</summary>
    IsolationLevel SnapshotIsolation { get; }

    /// <summary>A statement run first inside the snapshot transaction, such as making it read-only, or null.</summary>
    string? SnapshotSetup { get; }

    /// <summary>
    /// A statement that locks the table before the read takes its snapshot, so a concurrent rewrite such as a truncation
    /// completes first; or null. If the engine refuses the lock (for example for a role with column-level grants only), the
    /// read runs without it in a new transaction.
    /// </summary>
    string? SnapshotLock(string table);

    /// <summary>
    /// Whether the engine refused <see cref="SnapshotLock"/> itself, for a reason the read may not share (missing table-level
    /// privilege, a relation that cannot be locked), so the read runs without it. A lock that timed out or was cancelled is
    /// not a refusal: reading without it would lose the protection the lock exists for.
    /// </summary>
    bool IsLockRefusal(Exception exception);

    /// <summary>
    /// Whether the driver returns the extreme <see cref="DateTime"/>, <see cref="DateOnly"/> and <see cref="DateTimeOffset"/>
    /// values for infinite dates and timestamps (Npgsql's default), so a snapshot read refuses them rather than invent a date.
    /// </summary>
    bool ExtremeDatesAreInfinite { get; }

    /// <summary>
    /// Selects the columns of every row of the table, ordered by the key columns ascending; with a watermark column, only rows
    /// whose watermark is greater than the <c>@watermark</c> parameter or is NULL, so a NULL watermark is still read and
    /// rejected as a record error (spec 1199).
    /// </summary>
    string SelectSnapshot(string table, IReadOnlyList<string> columns, IReadOnlyList<string> keyColumns, string? watermarkColumn);

    /// <summary>Classifies an exception the driver raised.</summary>
    EtlSqlErrorKind Classify(Exception exception);
}

/// <summary>A SQL connection's target, with no secret.</summary>
public sealed record EtlSqlTarget(string Host, string? Port, string Database, string? User);

/// <summary>How the ETL writer treats a driver exception.</summary>
public enum EtlSqlErrorKind
{
    /// <summary>A deterministic refusal: the batch can never succeed as written.</summary>
    Rejected,
    /// <summary>A connection loss, timeout, deadlock or serialization failure: retry later.</summary>
    Transient,
    /// <summary>A unique violation.</summary>
    DuplicateKey,
    /// <summary>The object being created already exists.</summary>
    AlreadyExists,
    /// <summary>A table or column the statement names does not exist.</summary>
    MissingObject,
    /// <summary>An upsert's conflict target has no matching unique constraint.</summary>
    MissingConstraint
}
