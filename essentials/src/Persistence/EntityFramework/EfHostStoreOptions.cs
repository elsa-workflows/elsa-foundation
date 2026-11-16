namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// The settings every EF store a host composes on its own container from its configuration accepts, rather than through a
/// shell feature: the provider, the connection, the schema and pooling, as every first-party EF module names them. A store's
/// own options derive from this and add only what is specific to the store; <see cref="EfHostConfigurationReader"/> reads
/// these.
/// </summary>
public abstract class EfHostStoreOptions
{
    /// <summary>Relational provider for the store: Sqlite, SqlServer, PostgreSql or MySql.</summary>
    /// <remarks>SQLite serves several processes on one machine only; it suits development and tests.</remarks>
    public string Provider { get; set; } = EfProviderNames.Sqlite;

    /// <summary>
    /// Optional explicit connection string. When omitted, <see cref="ConnectionName"/> or the shared
    /// <c>ConnectionStrings:Elsa</c> is used, read from the host's configuration. It must point at the primary: a read
    /// replica cannot give read-after-write.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>Named connection under <c>ConnectionStrings</c> when <see cref="ConnectionString"/> is omitted.</summary>
    public string? ConnectionName { get; set; }

    /// <summary>Optional database schema for the store's tables and its migrations history table.</summary>
    public string? Schema { get; set; }

    /// <summary>Reuse contexts from a pool instead of constructing one per operation.</summary>
    public bool Pooling { get; set; }
}
