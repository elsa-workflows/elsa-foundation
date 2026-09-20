using System.Reflection;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// What <see cref="EfModuleCatalog.Discover"/> read off one <see cref="EfModuleAttribute"/> declaration,
/// plus the assembly that declared it.
/// </summary>
public sealed record EfModuleDescriptor(
    string Name,
    Type ContextType,
    string HistoryModule,
    Type? Sqlite,
    Type? SqlServer,
    Type? PostgreSql,
    Type? MySql,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<Type> PostMigration,
    string? DisplayName,
    Assembly Assembly)
{
    /// <summary>The frozen migrations-history table this module's host actually uses.</summary>
    public string HistoryTableName => EfMigrationsHistory.TableName(HistoryModule);

    /// <summary>The provider-derived context <paramref name="provider"/> selects, or <c>null</c> when this module does not support it.</summary>
    public Type? ProviderContext(string provider) =>
        EfRelationalProviderBinding.Select(provider, Name, Sqlite, SqlServer, PostgreSql, MySql);

    /// <summary>
    /// The provider-derived context <paramref name="provider"/> selects. A <c>null</c> provider property on the
    /// descriptor means that provider is unsupported for this module (FR-016), so this throws a clear
    /// <see cref="NotSupportedException"/> naming the module and the provider rather than handing back <c>null</c>.
    /// </summary>
    public Type RequireProviderContext(string provider) =>
        ProviderContext(provider) ?? throw new NotSupportedException(
            $"Module '{Name}' does not support the {EfRelationalProviderBinding.Normalize(provider)} provider.");
}
