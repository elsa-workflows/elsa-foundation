using CShells.Features;
using Elsa.Etl.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Persistence.EntityFrameworkCore;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("ETL")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "EtlRuntimeEntityFrameworkCore",
    DisplayName = "ETL Runtime EF Core Persistence",
    Description = "SQLite persistence for finite ETL runs, reservations, accepted output and terminal delivery.")]
[UsesEfModule("EtlRuntime")]
public sealed class EtlRuntimeEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Provider", Description = "Relational provider. This initial ETL runtime module supports SQLite.", Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";

    [ManifestSetting(DisplayName = "Connection string", Description = "Optional ETL runtime persistence connection string. If omitted, the shared Elsa connection is used.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(DisplayName = "Connection name", Description = "Optional named connection under ConnectionStrings.", Category = "Persistence")]
    public string? ConnectionName { get; set; }

    [ManifestSetting(DisplayName = "Schema", Description = "Optional database schema. SQLite ignores schemas.", Category = "Persistence")]
    public string? Schema { get; set; }

    [ManifestSetting(DisplayName = "Pooled contexts", Description = "Reuse the ETL runtime DbContext from a pool.", Category = "Persistence")]
    public bool Pooling { get; set; }

    [ManifestSetting(DisplayName = "Preparation lease seconds", Description = "Finite lease duration for snapshot preparation workers.", Category = "Runtime")]
    public int PreparationLeaseSeconds { get; set; } = 30;

    [ManifestSetting(DisplayName = "Partition lease seconds", Description = "Finite lease duration for partition workers.", Category = "Runtime")]
    public int PartitionLeaseSeconds { get; set; } = 30;

    [ManifestSetting(DisplayName = "Delivery lease seconds", Description = "Finite lease duration for native result delivery workers.", Category = "Runtime")]
    public int DeliveryLeaseSeconds { get; set; } = 30;

    [ManifestSetting(DisplayName = "Maximum partitions per run", Description = "Host cap on partition rows created for one ETL run.", Category = "Runtime")]
    public int MaximumPartitionsPerRun { get; set; } = 128;

    [ManifestSetting(DisplayName = "Maximum page size", Description = "Host cap on ETL run, partition and manifest page size.", Category = "Runtime")]
    public int MaximumPageSize { get; set; } = 100;

    public void ConfigureServices(IServiceCollection services)
    {
        var options = new EtlRuntimeEntityFrameworkCoreOptions
        {
            Provider = Provider,
            ConnectionString = ConnectionString,
            ConnectionName = ConnectionName,
            Schema = Schema,
            Pooling = Pooling,
            PreparationLease = TimeSpan.FromSeconds(PreparationLeaseSeconds),
            PartitionLease = TimeSpan.FromSeconds(PartitionLeaseSeconds),
            DeliveryLease = TimeSpan.FromSeconds(DeliveryLeaseSeconds),
            MaximumPartitionsPerRun = MaximumPartitionsPerRun,
            MaximumPageSize = MaximumPageSize
        };
        services.AddEtlRuntimeEntityFrameworkCore(options);
        services.AddEfModuleMigrations<EtlRuntimeDbContext>(options.Provider);
    }
}
