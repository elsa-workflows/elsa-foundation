using Elsa.Persistence.EntityFramework;
using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Design")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(name: "WorkflowsDesignEntityFrameworkCore", DisplayName = "Workflows Design Entity Framework Core Persistence", Description = "Opt-in EF Core persistence for Workflows Design definitions, versions, drafts, layouts and lifecycle operation receipts. Schema provisioning and default selection remain deferred.")]
public class WorkflowsDesignEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Provider", Description = "Sqlite, SqlServer, PostgreSql, or MySql.", Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";
    [ManifestSetting(DisplayName = "Connection string", Description = "Optional explicit provider connection string.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }
    [ManifestSetting(DisplayName = "Connection name", Description = "Named connection under ConnectionStrings.", Category = "Persistence")]
    public string? ConnectionName { get; set; }
    public virtual void ConfigureServices(IServiceCollection services) => services.AddWorkflowsDesignEntityFrameworkCore(new WorkflowsDesignEntityFrameworkCoreOptions { Provider = Provider, ConnectionString = ConnectionString, ConnectionName = ConnectionName })
        .AddEfModuleMigrations<WorkflowsDesignDbContext>(Provider);
}
