using Elsa.Persistence.EntityFramework;
using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(name: "WorkflowsRuntimeAlterationEntityFrameworkCorePersistence", DisplayName = "Workflows Runtime EF Core Alteration Persistence", Description = "Opt-in EF Core persistence for alteration plans and jobs. Groundwork remains the default.", DependsOn = new object[] { "WorkflowsRuntimeResumption" })]
public sealed class RuntimeWorkflowAlterationEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Provider", Description = "Relational provider: Sqlite, SqlServer, PostgreSql, or MySql.", Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";

    [ManifestSetting(DisplayName = "Connection string", Description = "Optional explicit connection string.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(DisplayName = "Connection name", Description = "Optional configuration connection-string name.", Category = "Persistence")]
    public string? ConnectionName { get; set; }

    [ManifestSetting(DisplayName = "Recovery continuation signing key", Description = "At least 32 UTF-8 bytes shared by nodes consuming durable pages.", Category = "Security", Secret = true)]
    public string? RecoveryContinuationSigningKey { get; set; }
    public void ConfigureServices(IServiceCollection services) => services.AddRuntimeWorkflowAlterationEntityFrameworkCore(new() { Provider = Provider, ConnectionString = ConnectionString, ConnectionName = ConnectionName, RecoveryContinuationSigningKey = RecoveryContinuationSigningKey })
        .AddEfModuleMigrations<BookmarkStateDbContext>(Provider);
}
