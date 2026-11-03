using System.Data.Common;

namespace Elsa.Etl.Core.Runtime;

/// <summary>
/// The per-provider adapter that isolates one SQL driver from the ETL runtime (spec 1197). The runtime's writer owns the
/// delivery protocol; an engine supplies only connections, dialect text and error classification.
/// </summary>
/// <remarks>
/// Contributor contract keyed by <see cref="Provider"/>: several engines may be registered, and each SQL binding resolves the
/// one its provider names. Every identifier passed in has already been validated as <c>[A-Za-z_][A-Za-z0-9_]{0,62}</c>.
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

    /// <summary>Counts unique constraints or indexes on exactly the key columns of the table.</summary>
    string CountUniqueKeys(string table, IReadOnlyList<string> keyColumns);

    /// <summary>
    /// One single-row upsert whose parameters are <see cref="Parameter"/> of each column for the given row number; an
    /// existing row with the same key has every non-key column updated.
    /// </summary>
    string Upsert(string table, IReadOnlyList<string> columns, IReadOnlyList<string> keyColumns, int row);

    /// <summary>The parameter name of a column's value in an upsert row.</summary>
    string Parameter(string column, int row);

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
