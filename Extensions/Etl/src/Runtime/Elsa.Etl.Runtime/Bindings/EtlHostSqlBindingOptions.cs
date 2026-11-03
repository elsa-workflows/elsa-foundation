namespace Elsa.Etl.Runtime;

/// <summary>A trusted host SQL revision that ETL pipelines may deliver their main route to (spec 1197).</summary>
public sealed class EtlHostSqlBindingOptions
{
    /// <summary>The default ledger table name.</summary>
    public const string DefaultLedgerTable = "elsa_etl_effect_ledger";

    /// <summary>The trusted tenant identifier, or null for the explicit null-tenant scope.</summary>
    public string? TenantId { get; set; }

    /// <summary>The required opaque persistence partition for this binding.</summary>
    public string PersistencePartition { get; set; } = string.Empty;

    /// <summary>The bounded alias referenced by a published ETL pipeline.</summary>
    public string Alias { get; set; } = string.Empty;

    /// <summary>The SQL connector role; only <c>MainOutput</c> is supported.</summary>
    public string Role { get; set; } = "MainOutput";

    /// <summary>The immutable host revision identifier.</summary>
    public string Revision { get; set; } = string.Empty;

    /// <summary>Whether new runs may pin this revision.</summary>
    public bool IsCurrent { get; set; } = true;

    /// <summary>The engine provider name, <c>postgresql</c> or <c>sqlserver</c>; its package must be installed.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The connection string, supplied through host configuration; it is never pinned or exposed.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>The tables pipelines may write, as <c>name</c> or <c>schema.name</c>, compared ignoring case.</summary>
    public IList<string> AllowedTables { get; set; } = new List<string>();

    /// <summary>The ledger table recording applied effects, as <c>name</c> or <c>schema.name</c>.</summary>
    public string LedgerTable { get; set; } = DefaultLedgerTable;

    /// <summary>Whether the writer creates a missing ledger table; otherwise a missing ledger fails the run.</summary>
    public bool CreateLedger { get; set; } = true;
}
