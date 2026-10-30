using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore;

/// <summary>
/// Recognizes the cumulative template Initial written by #3472 as including the retained September baseline.
/// The physical history is never rewritten. All subsequent Design changes use incremental migrations.
/// </summary>
internal sealed class WorkflowsDesignMigrationHistory(IHistoryRepository inner, IRelationalTypeMappingSource mappings) : IHistoryRepository
{
    private static readonly IReadOnlyDictionary<string, string> CumulativeInitials = new Dictionary<string, string>
    {
        ["20260928040709_Initial"] = "20261030020430_Initial",
        ["20260928040711_Initial"] = "20261030020432_Initial",
        ["20260928040712_Initial"] = "20261030020433_Initial",
        ["20260928040714_Initial"] = "20261030020434_Initial"
    };

    public IReadOnlyList<HistoryRow> GetAppliedMigrations() => IncludeBaseline(inner.GetAppliedMigrations());

    public async Task<IReadOnlyList<HistoryRow>> GetAppliedMigrationsAsync(CancellationToken cancellationToken = default) =>
        IncludeBaseline(await inner.GetAppliedMigrationsAsync(cancellationToken));

    private static IReadOnlyList<HistoryRow> IncludeBaseline(IReadOnlyList<HistoryRow> rows)
    {
        var result = rows.ToList();
        foreach (var (baseline, cumulative) in CumulativeInitials)
            if (!rows.Any(row => row.MigrationId == baseline) && rows.FirstOrDefault(row => row.MigrationId == cumulative) is { } applied)
                result.Add(new HistoryRow(baseline, applied.ProductVersion));
        return result.OrderBy(row => row.MigrationId, StringComparer.Ordinal).ToArray();
    }

    public string GetBeginIfNotExistsScript(string migrationId) => IncludeCumulativeGuard(inner.GetBeginIfNotExistsScript(migrationId), migrationId);

    private string IncludeCumulativeGuard(string script, string migrationId)
    {
        if (!CumulativeInitials.TryGetValue(migrationId, out var cumulative))
            return script;
        // Preserve the provider's quoting, configured history schema and block syntax. Both IDs mean this
        // baseline's tables already exist. Refuse an unexpected provider shape instead of emitting unsafe SQL.
        var mapping = mappings.GetMapping(typeof(string));
        var baselineLiteral = mapping.GenerateSqlLiteral(migrationId);
        var comparison = "= " + baselineLiteral;
        var index = script.IndexOf(comparison, StringComparison.Ordinal);
        if (index < 0 || index != script.LastIndexOf(comparison, StringComparison.Ordinal))
            throw new NotSupportedException("The Workflows Design baseline guard does not recognize this provider's migration-history predicate.");
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
internal sealed class WorkflowsDesignMigrationHistoryOptions : IDbContextOptionsExtension
{
    public DbContextOptionsExtensionInfo Info => new OptionsInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
        var original = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IHistoryRepository));
        if (original is null)
            return; // Non-relational contexts have no migration history.
        services.Remove(original);
        services.AddScoped<IHistoryRepository>(provider => new WorkflowsDesignMigrationHistory(
            (IHistoryRepository)(original.ImplementationInstance ?? original.ImplementationFactory?.Invoke(provider) ??
                ActivatorUtilities.CreateInstance(provider, original.ImplementationType!)),
            provider.GetRequiredService<IRelationalTypeMappingSource>()));
    }

    public void Validate(IDbContextOptions options) { }

    private sealed class OptionsInfo(WorkflowsDesignMigrationHistoryOptions extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;
        public override string LogFragment => "RetainedDesignBaseline ";
        public override int GetServiceProviderHashCode() => 0;
        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is OptionsInfo;
        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) => debugInfo["Elsa:RetainedDesignBaseline"] = "1";
    }
}
