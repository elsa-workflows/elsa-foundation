using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence",
    DisplayName = "Workflows Runtime EF Core Executable Artifact Persistence",
    Description = "Opt-in EF Core persistence for runtime executable artifacts, templates and source references against a fresh schema. Migration lifecycle is deferred; Groundwork remains the default.",
    DependsOn = new object[] { "WorkflowsRuntime" })]
public class RuntimeArtifactsEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(
        DisplayName = "Provider",
        Description = "Relational provider for the Runtime artifact DbContext: Sqlite, SqlServer, PostgreSql, or MySql. The host must reference that provider package; this feature does not provision schema.",
        Category = "Persistence")]
    public string? Provider { get; set; }

    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "Optional explicit connection string. When omitted, ConnectionName or ConnectionStrings:ElsaRuntimeArtifacts is used. Sqlite defaults to Data Source=elsa-runtime-artifacts.db.",
        Category = "Persistence",
        Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(
        DisplayName = "Connection name",
        Description = "Optional configuration connection-string name. When omitted, ElsaRuntimeArtifacts is used.",
        Category = "Persistence")]
    public string? ConnectionName { get; set; }
    [ManifestSetting(
        DisplayName = "Recovery continuation signing key",
        Description = "At least 32 UTF-8 bytes shared by nodes that consume durable runtime pages. Required for durable EF runtime paging.",
        Category = "Security",
        Secret = true)]
    public string? RecoveryContinuationSigningKey { get; set; }

    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Configure<RuntimeRecoveryContinuationOptions>(options =>
        {
            if (!string.IsNullOrWhiteSpace(RecoveryContinuationSigningKey))
                options.SigningKey = RecoveryContinuationSigningKey;
            options.AllowEphemeralDevelopmentKey = false;
        });
        services.AddRuntimeArtifactsEntityFrameworkCore(new RuntimeArtifactsEntityFrameworkCoreOptions
        {
            Provider = Provider ?? "Sqlite",
            ConnectionString = ConnectionString,
            ConnectionName = ConnectionName
        });
    }
}
