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
[ShellFeature(
    name: "WorkflowsRuntimeWorkflowExecutionEntityFrameworkCorePersistence",
    DisplayName = "Workflows Runtime EF Core Workflow Execution State Persistence",
    Description = "Opt-in EF Core persistence for workflow execution state. Groundwork remains the default.",
    DependsOn = new object[] { "WorkflowsRuntimeResumption" })]
public class RuntimeWorkflowExecutionEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Provider", Description = "Relational provider: Sqlite, SqlServer, PostgreSql, or MySql.", Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";
    [ManifestSetting(DisplayName = "Connection string", Description = "Optional explicit connection string.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }
    [ManifestSetting(DisplayName = "Connection name", Description = "Optional configuration connection-string name.", Category = "Persistence")]
    public string? ConnectionName { get; set; }
    [ManifestSetting(DisplayName = "Recovery continuation signing key", Description = "At least 32 UTF-8 bytes shared by nodes consuming durable pages.", Category = "Security", Secret = true)]
    public string? RecoveryContinuationSigningKey { get; set; }

    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddRuntimeWorkflowExecutionEntityFrameworkCore(new RuntimeWorkflowExecutionEntityFrameworkCoreOptions { Provider = Provider, ConnectionString = ConnectionString, ConnectionName = ConnectionName, RecoveryContinuationSigningKey = RecoveryContinuationSigningKey });
        services.AddEfModuleMigrations<BookmarkStateDbContext>(Provider);
    }
}
