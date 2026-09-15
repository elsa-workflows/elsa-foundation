using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(name: "WorkflowsRuntimeTestScopeEntityFrameworkCorePersistence", DisplayName = "Workflows Runtime EF Core Test-Scope Persistence", Description = "Opt-in EF Core persistence for workflow test scopes. Groundwork remains the default.", DependsOn = new object[] { "WorkflowsRuntime" })]
public sealed class RuntimeWorkflowTestScopeEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Provider", Description = "Relational provider: Sqlite, SqlServer, PostgreSql, or MySql.", Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";

    [ManifestSetting(DisplayName = "Connection string", Description = "Optional explicit connection string.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(DisplayName = "Connection name", Description = "Optional configuration connection-string name.", Category = "Persistence")]
    public string? ConnectionName { get; set; }

    [ManifestSetting(DisplayName = "Recovery continuation signing key", Description = "Stable key used to authenticate scope paging cursors.", Category = "Persistence", Secret = true)]
    public string? RecoveryContinuationSigningKey { get; set; }
    public void ConfigureServices(IServiceCollection services) => services.AddRuntimeWorkflowTestScopeEntityFrameworkCore(new() { Provider = Provider, ConnectionString = ConnectionString, ConnectionName = ConnectionName, RecoveryContinuationSigningKey = RecoveryContinuationSigningKey });
}
