using CShells.Features;
using Elsa.Persistence.EntityFramework;
using Elsa.Rules.Persistence.Core;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Rules.Persistence.EntityFrameworkCore;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Rules")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(name: "RulesRuntimeEntityFrameworkCore", DisplayName = "Rules Runtime EF Core Persistence",
    Description = "Relational persistence for durable rule inference and native terminal delivery.")]
[UsesEfModule("RulesRuntime")]
public class RulesRuntimeEntityFrameworkCoreFeature : IShellFeature
{
    private static readonly EfModuleBinding Binding = EfModuleBinding.For(typeof(RulesDbContext));

    [ManifestSetting(DisplayName = "Provider", Description = "Relational provider; this build supports SQLite.", Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";
    [ManifestSetting(DisplayName = "Connection string", Description = "Optional provider connection string; defaults to the shared Elsa connection.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }
    [ManifestSetting(DisplayName = "Connection name", Description = "Optional named connection under ConnectionStrings.", Category = "Persistence")]
    public string? ConnectionName { get; set; }
    [ManifestSetting(DisplayName = "Schema", Description = "Optional database schema; SQLite ignores schemas.", Category = "Persistence")]
    public string? Schema { get; set; }
    [ManifestSetting(DisplayName = "Pooled contexts", Description = "Reuse provider contexts while preserving fresh per-transaction state.", Category = "Persistence")]
    public bool Pooling { get; set; }
    [ManifestSetting(DisplayName = "Work lease seconds", Description = "Database-time work lease duration, from 1 through 86400 seconds.", Category = "Runtime")]
    public int WorkLeaseSeconds { get; set; } = 60;
    [ManifestSetting(DisplayName = "Delivery lease seconds", Description = "Database-time terminal delivery lease duration, from 1 through 86400 seconds.", Category = "Runtime")]
    public int DeliveryLeaseSeconds { get; set; } = 60;

    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (WorkLeaseSeconds is < 1 or > 86_400) throw new ArgumentOutOfRangeException(nameof(WorkLeaseSeconds));
        if (DeliveryLeaseSeconds is < 1 or > 86_400) throw new ArgumentOutOfRangeException(nameof(DeliveryLeaseSeconds));
        var provider = EfRelationalProviderBinding.Normalize(Provider);
        if (provider != "sqlite") throw new NotSupportedException("This Rules persistence build supports SQLite only.");
        var connection = ConnectionString;
        var connectionName = ConnectionName;
        var schema = Schema;
        var workLease = TimeSpan.FromSeconds(WorkLeaseSeconds);
        var deliveryLease = TimeSpan.FromSeconds(DeliveryLeaseSeconds);
        void Configure(IServiceProvider services, DbContextOptionsBuilder builder) =>
            Binding.Apply(builder, services, provider, connection, connectionName, schema);

        // A fresh context per store transaction avoids retaining stale tracked state after a conflict.
        // The shared binding preserves normal connection resolution and schema write admission.
        if (Pooling)
            services.AddPooledDbContextFactory<RulesSqliteDbContext>(Configure);
        else
            services.AddDbContextFactory<RulesSqliteDbContext>(Configure, ServiceLifetime.Scoped);
        services.AddScoped<IDbContextFactory<RulesDbContext>, RulesContextFactory>();
        services.AddScoped<RulesDbContext>(services => services.GetRequiredService<IDbContextFactory<RulesDbContext>>().CreateDbContext());
        services.TryAddScoped<EfRuleRunStore>(services => new(
            services.GetRequiredService<IDbContextFactory<RulesDbContext>>(),
            services.GetRequiredService<IRuleAdmissionValidator>(), workLease,
            services.GetRequiredService<IRuleRoundValidator>(),
            services.GetService<IRuleDeliveryProofValidator>(), deliveryLease));
        services.TryAddScoped<IRuleRunStore>(services => services.GetRequiredService<EfRuleRunStore>());
        // The reader is independently replaceable. By default it follows the selected store,
        // so overriding a combined provider never reads from the built-in database by accident.
        services.TryAddScoped<IRuleRunReader>(services => services.GetRequiredService<IRuleRunStore>() as IRuleRunReader
            ?? throw new InvalidOperationException("A Rules store replacement must implement IRuleRunReader or register its own reader."));
        services.AddEfModuleMigrations<RulesDbContext>(Provider);
    }

    private sealed class RulesContextFactory(IDbContextFactory<RulesSqliteDbContext> factory) : IDbContextFactory<RulesDbContext>
    {
        public RulesDbContext CreateDbContext() => factory.CreateDbContext();
        public async Task<RulesDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            await factory.CreateDbContextAsync(cancellationToken);
    }
}
