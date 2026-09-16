using Elsa.Persistence.EntityFramework;
using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "WorkflowsRuntimeOperationalStateEntityFrameworkCorePersistence",
    DisplayName = "Workflows Runtime EF Core Operational State Persistence",
    Description = "Opt-in EF Core persistence for runtime operational state, execution liveness, workflow holds, incidents, and runtime attention.",
    DependsOn = new object[] { "WorkflowsRuntimeResumption" })]
public sealed class RuntimeOperationalStateEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Provider", Description = "Relational provider: Sqlite, SqlServer, PostgreSql, or MySql.", Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";

    [ManifestSetting(DisplayName = "Connection string", Description = "Optional explicit connection string.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(DisplayName = "Connection name", Description = "Optional configuration connection-string name.", Category = "Persistence")]
    public string? ConnectionName { get; set; }

    [ManifestSetting(DisplayName = "Recovery continuation signing key", Description = "Optional shared signing key for runtime recovery continuations.", Category = "Persistence", Secret = true)]
    public string? RecoveryContinuationSigningKey { get; set; }

    public void ConfigureServices(IServiceCollection services) => services.AddRuntimeOperationalStateEntityFrameworkCore(new()
    {
        Provider = Provider,
        ConnectionString = ConnectionString,
        ConnectionName = ConnectionName,
        RecoveryContinuationSigningKey = RecoveryContinuationSigningKey
    })
        .AddEfModuleMigrations<BookmarkStateDbContext>(Provider);
}
