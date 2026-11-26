using Elsa.Persistence.EntityFramework;
using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
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
[UsesEfModule("Workflows.Runtime")]
[EfPersistenceResourceParticipant]
public sealed class RuntimeOperationalStateEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Provider", Description = "Relational provider: Sqlite, SqlServer, PostgreSql, or MySql.", Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";

    [ManifestSetting(DisplayName = "Connection string", Description = "Optional explicit connection string.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(DisplayName = "Connection name", Description = "Optional configuration connection-string name.", Category = "Persistence")]
    public string? ConnectionName { get; set; }

    [ManifestSetting(
        DisplayName = "Schema",
        Description = "Optional database schema for this module's tables and its own migrations history table. Falls back to Elsa:Persistence:EntityFramework:Schema, then to the provider's own default. Ignored on Sqlite, which has no schemas, and refused on MySql, where a schema is a database: name it in the connection string there instead.",
        Category = "Persistence")]
    public string? Schema { get; set; }

    [ManifestSetting(
        DisplayName = "Pooled contexts",
        Description = "Reuse DbContext instances from a pool instead of constructing one per scope. Safe for every first-party module context, which carries nothing but its options.",
        Category = "Persistence")]
    public bool Pooling { get; set; }

    [ManifestSetting(DisplayName = "Recovery continuation signing key", Description = "Optional shared signing key for runtime recovery continuations.", Category = "Persistence", Secret = true)]
    public string? RecoveryContinuationSigningKey { get; set; }

    public void ConfigureServices(IServiceCollection services) => services.AddRuntimeOperationalStateEntityFrameworkCore(new()
    {
        Provider = Provider,
        ConnectionString = ConnectionString,
        ConnectionName = ConnectionName,
        Schema = Schema,
        Pooling = Pooling,
        RecoveryContinuationSigningKey = RecoveryContinuationSigningKey
    })
        .AddEfModuleMigrations<RuntimeDbContext>(Provider);
}
