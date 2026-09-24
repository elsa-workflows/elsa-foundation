using CShells.DependencyInjection;
using CShells.Lifecycle;
using Elsa.Locking.Core;
using Elsa.Modularity.EntityFramework.Extensions;
using Elsa.Persistence.EntityFramework.Tooling;
using Elsa.Tasks;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[assembly: EfToolingShellDefaults(typeof(Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.RuntimeSharedPersistenceTestShellDefaults))]

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class SharedPersistenceCompositionTests : IDisposable
{
    private const string ShellName = "shared-runtime-persistence";
    private const string FeatureName = "WorkflowsRuntimeEntityFrameworkCore";
    private const string RecoverySigningKey = "shared-runtime-recovery-signing-key-32-bytes";
    private const string HierarchySigningKey = "shared-runtime-hierarchy-signing-key-32-bytes";
    private readonly string databasePath = Path.Join(Path.GetTempPath(), $"elsa-shared-runtime-{Guid.NewGuid():N}.db");

    private string ConnectionString => $"Data Source={databasePath};Pooling=False";

    [Fact]
    public async Task Named_resource_materializes_to_runtime_and_execution_state_survives_shell_restart()
    {
        const string executionId = "shared-runtime-execution";
        var now = DateTimeOffset.UtcNow;
        var state = new WorkflowExecutionState(
            executionId,
            new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"),
            WorkflowExecutionStatus.Completed,
            null,
            now,
            now,
            now,
            now,
            null,
            null,
            null,
            new Dictionary<string, string>());

        await using (var host = CreateHost())
        {
            var shell = await host.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
            await using var scope = shell.ServiceProvider.CreateAsyncScope();
            var options = scope.ServiceProvider.GetRequiredService<RuntimeWorkflowExecutionEntityFrameworkCoreOptions>();

            Assert.Equal("Sqlite", options.Provider);
            Assert.Equal("Shared", options.ConnectionName);
            Assert.Null(options.ConnectionString);

            var database = scope.ServiceProvider.GetRequiredService<RuntimeDbContext>();
            Assert.Equal(databasePath, database.Database.GetDbConnection().DataSource);
            Assert.NotEmpty(await database.Database.GetAppliedMigrationsAsync());
            Assert.Empty(await database.Database.GetPendingMigrationsAsync());

            await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(state);
        }

        await using (var host = CreateHost())
        {
            var shell = await host.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
            await using var scope = shell.ServiceProvider.CreateAsyncScope();
            var persisted = await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(executionId);

            Assert.NotNull(persisted);
            Assert.Equal(WorkflowExecutionStatus.Completed, persisted.Status);
            Assert.Equal("artifact", persisted.PinnedExecutable.ArtifactId);
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            File.Delete(path);
    }

    private ServiceProvider CreateHost()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Elsa:Persistence:DefaultResource"] = "primary",
                ["Elsa:Persistence:Resources:primary:Provider"] = "Sqlite",
                ["Elsa:Persistence:Resources:primary:ConnectionName"] = "Shared",
                ["ConnectionStrings:Shared"] = ConnectionString,
                [$"CShells:Shells:{ShellName}:Features:{FeatureName}"] = null,
                [$"CShells:Shells:{ShellName}:Features:{FeatureName}:RecoveryContinuationSigningKey"] = RecoverySigningKey,
                [$"CShells:Shells:{ShellName}:Features:{FeatureName}:HierarchyCursorSigningKey"] = HierarchySigningKey,
                [$"CShells:Shells:{ShellName}:Features:{RuntimeEntityFrameworkCoreFeatureTests.RuntimeRootFeature.Name}"] = null
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDistributedLockProvider, RuntimeEntityFrameworkCoreFeatureTests.ProcessLockProvider>();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddEfPersistenceResources(configuration, typeof(SharedPersistenceCompositionTests).Assembly);
        services.AddCShells(shells => shells
            .WithAssemblies(
                typeof(RuntimeEntityFrameworkCoreFeature).Assembly,
                typeof(WorkflowsRuntimeResumptionFeature).Assembly,
                typeof(TasksFeature).Assembly,
                typeof(RuntimeEntityFrameworkCoreFeatureTests.RuntimeRootFeature).Assembly)
            .WithConfigurationProvider(configuration));
        return services.BuildServiceProvider(validateScopes: true);
    }
}

public sealed class RuntimeSharedPersistenceTestShellDefaults : IEfToolingShellDefaults
{
    public void Configure(CShells.Configuration.ShellBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);
    }
}
