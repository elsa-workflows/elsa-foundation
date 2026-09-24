using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Modularity.EntityFramework.Extensions;
using Elsa.Persistence.EntityFramework.Tooling;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

[assembly: EfToolingShellDefaults(typeof(Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Tests.SharedPersistenceTestShellDefaults))]

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Exercises named-resource materialization through a real CShells shell. SQLite keeps the
/// host-restart proof self-contained; shared PostgreSQL placement is covered by the shared-resource fixture.
/// </summary>
public sealed class SharedPersistenceCompositionTests : IDisposable
{
    private const string ShellName = "shared-design-persistence";
    private const string FeatureName = "WorkflowsDesignEntityFrameworkCore";
    private const string DraftState = "{\"activities\":[{\"nodeId\":\"persisted-node\"}]}";
    private readonly string databasePath = Path.Join(Path.GetTempPath(), $"elsa-shared-design-{Guid.NewGuid():N}.db");

    private string ConnectionString => $"Data Source={databasePath};Pooling=False";

    [Fact]
    public async Task Named_resource_materializes_to_design_and_state_survives_shell_restart()
    {
        await using (var host = CreateHost())
        {
            var shell = await host.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
            await using var scope = shell.ServiceProvider.CreateAsyncScope();
            var options = scope.ServiceProvider.GetRequiredService<WorkflowsDesignEntityFrameworkCoreOptions>();

            Assert.Equal("Sqlite", options.Provider);
            Assert.Equal("Shared", options.ConnectionName);
            Assert.Null(options.ConnectionString);

            var database = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>();
            Assert.Equal(databasePath, database.Database.GetDbConnection().DataSource);
            Assert.NotEmpty(await database.Database.GetAppliedMigrationsAsync());
            Assert.Empty(await database.Database.GetPendingMigrationsAsync());
            database.Definitions.Add(new WorkflowDefinition
            {
                Id = "definition-1",
                TenantId = "tenant-a",
                Name = "Shared resource design"
            });
            database.Drafts.Add(new WorkflowDefinitionDraft
            {
                Id = "draft-1",
                TenantId = "tenant-a",
                WorkflowDefinitionId = "definition-1",
                StateSource = DraftState
            });
            await database.SaveChangesAsync();
        }

        await using (var host = CreateHost())
        {
            var shell = await host.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
            await using var scope = shell.ServiceProvider.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>();
            var draft = await database.Drafts.AsNoTracking().SingleAsync(x => x.Id == "draft-1");

            Assert.Equal("definition-1", draft.WorkflowDefinitionId);
            Assert.Equal(DraftState, draft.StateSource);
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            File.Delete(path);
    }

    /// <summary>Builds the CShells service-provider host from the same root resource and shell settings as Workbench.</summary>
    private ServiceProvider CreateHost()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Elsa:Persistence:DefaultResource"] = "primary",
                ["Elsa:Persistence:Resources:primary:Provider"] = "Sqlite",
                ["Elsa:Persistence:Resources:primary:ConnectionName"] = "Shared",
                ["ConnectionStrings:Shared"] = ConnectionString,
                [$"CShells:Shells:{ShellName}:Features:{FeatureName}"] = null
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddEfPersistenceResources(configuration, typeof(SharedPersistenceCompositionTests).Assembly);
        services.AddCShells(shells => shells
            .WithAssemblies(typeof(WorkflowsDesignEntityFrameworkCoreFeature).Assembly)
            .WithConfigurationProvider(configuration));
        return services.BuildServiceProvider(validateScopes: true);
    }
}

public sealed class SharedPersistenceTestShellDefaults : IEfToolingShellDefaults
{
    public void Configure(CShells.Configuration.ShellBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);
    }
}
