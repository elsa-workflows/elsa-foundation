using Elsa.Persistence.EntityFramework;

namespace Elsa.Foundation.DataProtection.EntityFrameworkCore;

/// <summary>
/// The EF key store's settings (#2191). They follow the other EF modules': provider, connection string or connection name,
/// schema and pooling.
/// </summary>
public sealed class EfDataProtectionKeyStoreOptions
{
    /// <summary>The subsection of <see cref="DataProtectionConfigurationExtensions.SectionName"/> these are read from.</summary>
    public const string SectionKey = "EntityFrameworkCore";

    /// <summary>Relational provider for the key store: Sqlite, SqlServer, PostgreSql or MySql.</summary>
    /// <remarks>SQLite serves several processes on one machine only; across machines use one of the other three.</remarks>
    public string Provider { get; set; } = EfProviderNames.Sqlite;

    /// <summary>
    /// Optional explicit connection string. When omitted, <see cref="ConnectionName"/> or the shared
    /// <c>ConnectionStrings:Elsa</c> is used, read from the host's configuration, so the keys live with the platform's data.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>Named connection under <c>ConnectionStrings</c> when <see cref="ConnectionString"/> is omitted.</summary>
    public string? ConnectionName { get; set; }

    /// <summary>Optional database schema for the key table and its migrations history table.</summary>
    public string? Schema { get; set; }

    /// <summary>Reuse contexts from a pool instead of constructing one per operation.</summary>
    public bool Pooling { get; set; }
}
