using System.Reflection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Recognizes an explicitly declared cumulative migration without rewriting physical history.
/// Installed uniformly by the provider binding so module metadata does not fragment EF's service-provider cache.
/// </summary>
internal sealed class EfMigrationHistoryRepository(IHistoryRepository inner, IRelationalTypeMappingSource mappings, IMigrationsAssembly migrations) : IHistoryRepository
{
    private readonly IReadOnlyDictionary<string, string> cumulativeInitials = migrations.Migrations
        .SelectMany(migration => migration.Value.GetCustomAttributes<EfMigrationIncludesAttribute>()
            .Select(includes => new KeyValuePair<string, string>(includes.MigrationId, migration.Key)))
        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    public IReadOnlyList<HistoryRow> GetAppliedMigrations() => IncludeBaseline(inner.GetAppliedMigrations());

    public async Task<IReadOnlyList<HistoryRow>> GetAppliedMigrationsAsync(CancellationToken cancellationToken = default) =>
        IncludeBaseline(await inner.GetAppliedMigrationsAsync(cancellationToken));

    private IReadOnlyList<HistoryRow> IncludeBaseline(IReadOnlyList<HistoryRow> rows)
    {
        if (cumulativeInitials.Count == 0)
            return rows;
        var result = rows.ToList();
        foreach (var (baseline, cumulative) in cumulativeInitials)
            if (!rows.Any(row => row.MigrationId == baseline) && rows.FirstOrDefault(row => row.MigrationId == cumulative) is { } applied)
                result.Add(new HistoryRow(baseline, applied.ProductVersion));
        return result.OrderBy(row => row.MigrationId, StringComparer.Ordinal).ToArray();
    }

    public string GetBeginIfNotExistsScript(string migrationId) => IncludeCumulativeGuard(inner.GetBeginIfNotExistsScript(migrationId), migrationId);

    private string IncludeCumulativeGuard(string script, string migrationId)
    {
        if (!cumulativeInitials.TryGetValue(migrationId, out var cumulative))
            return script;
        // Preserve the provider's quoting, configured history schema and block syntax. Both IDs mean this
        // baseline's tables already exist. Refuse an unexpected provider shape instead of emitting unsafe SQL.
        var mapping = mappings.GetMapping(typeof(string));
        var baselineLiteral = mapping.GenerateSqlLiteral(migrationId);
        var comparison = "= " + baselineLiteral;
        var index = script.IndexOf(comparison, StringComparison.Ordinal);
        if (index < 0 || index != script.LastIndexOf(comparison, StringComparison.Ordinal))
            throw new NotSupportedException("The cumulative migration baseline guard does not recognize this provider's migration-history predicate.");
        return script.Replace(comparison, $"IN ({baselineLiteral}, {mapping.GenerateSqlLiteral(cumulative)})", StringComparison.Ordinal);
    }

    public bool Exists() => inner.Exists();
    public Task<bool> ExistsAsync(CancellationToken cancellationToken = default) => inner.ExistsAsync(cancellationToken);
    public void Create() => inner.Create();
    public Task CreateAsync(CancellationToken cancellationToken = default) => inner.CreateAsync(cancellationToken);
    public bool CreateIfNotExists() => inner.CreateIfNotExists();
    public Task<bool> CreateIfNotExistsAsync(CancellationToken cancellationToken = default) => inner.CreateIfNotExistsAsync(cancellationToken);
    public LockReleaseBehavior LockReleaseBehavior => inner.LockReleaseBehavior;
    public IMigrationsDatabaseLock AcquireDatabaseLock() => inner.AcquireDatabaseLock();
    public Task<IMigrationsDatabaseLock> AcquireDatabaseLockAsync(CancellationToken cancellationToken = default) => inner.AcquireDatabaseLockAsync(cancellationToken);
    public string GetCreateScript() => inner.GetCreateScript();
    public string GetCreateIfNotExistsScript() => inner.GetCreateIfNotExistsScript();
    public string GetInsertScript(HistoryRow row) => inner.GetInsertScript(row);
    public string GetDeleteScript(string migrationId) => inner.GetDeleteScript(migrationId);
    public string GetBeginIfExistsScript(string migrationId) => inner.GetBeginIfExistsScript(migrationId);
    public string GetEndIfScript() => inner.GetEndIfScript();
}

/// <summary>Decorates this module's actual provider history service, including its migration lock.</summary>
internal sealed class EfMigrationHistoryOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? info;
    public DbContextOptionsExtensionInfo Info => info ??= new OptionsInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
        var original = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IHistoryRepository));
        if (original is null)
            return; // Non-relational contexts have no migration history.
        services.Remove(original);
        services.AddScoped<IHistoryRepository>(provider => new EfMigrationHistoryRepository(
            (IHistoryRepository)(original.ImplementationInstance ?? original.ImplementationFactory?.Invoke(provider) ??
                ActivatorUtilities.CreateInstance(provider, original.ImplementationType!)),
            provider.GetRequiredService<IRelationalTypeMappingSource>(), provider.GetRequiredService<IMigrationsAssembly>()));
    }

    public void Validate(IDbContextOptions options) { }

    private sealed class OptionsInfo(EfMigrationHistoryOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;
        public override string LogFragment => "CumulativeMigrationBaseline ";
        public override int GetServiceProviderHashCode() => 0;
        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is OptionsInfo;
        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) => debugInfo["Elsa:CumulativeMigrationBaseline"] = "1";
    }
}
