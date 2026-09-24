using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Events;
using Elsa.Api.Capabilities;
using Elsa.Modularity.EntityFramework.Extensions;
using Elsa.Persistence.EntityFramework.Tooling;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

[assembly: EfToolingShellDefaults(typeof(Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests.SharedPersistenceTestShellDefaults))]

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Exercises named-resource materialization through a real CShells shell. SQLite keeps the
/// host-restart proof self-contained; shared PostgreSQL placement is covered by the shared-resource fixture.
/// </summary>
public sealed class SharedPersistenceCompositionTests : IDisposable
{
    private const string ShellName = "shared-publishing-persistence";
    private const string FeatureName = "WorkflowsPublishingEntityFrameworkCore";
    private const string PublicationStatus = "Active";
    private readonly string databasePath = Path.Join(Path.GetTempPath(), $"elsa-shared-publishing-{Guid.NewGuid():N}.db");

    private string ConnectionString => $"Data Source={databasePath};Pooling=False";

    [Fact]
    public async Task Named_resource_materializes_to_publishing_and_publication_state_survives_shell_restart()
    {
        var timestamp = DateTimeOffset.UtcNow;
        await using (var host = CreateHost())
        {
            var shell = await host.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
            await using var scope = shell.ServiceProvider.CreateAsyncScope();
            var options = scope.ServiceProvider.GetRequiredService<PublishingEntityFrameworkCoreOptions>();

            Assert.Equal("Sqlite", options.Provider);
            Assert.Equal("Shared", options.ConnectionName);
            Assert.Null(options.ConnectionString);

            var database = scope.ServiceProvider.GetRequiredService<PublishingSnapshotReviewDbContext>();
            Assert.Equal(databasePath, database.Database.GetDbConnection().DataSource);
            Assert.NotEmpty(await database.Database.GetAppliedMigrationsAsync());
            Assert.Empty(await database.Database.GetPendingMigrationsAsync());
            foreach (var designDatabase in new DbContext[]
                     {
                         scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(),
                         scope.ServiceProvider.GetRequiredService<ActivitiesDesignDbContext>()
                     })
            {
                Assert.Equal(databasePath, designDatabase.Database.GetDbConnection().DataSource);
                Assert.NotEmpty(await designDatabase.Database.GetAppliedMigrationsAsync());
            }
            database.PublicationRecords.Add(new PublicationRecordEntity
            {
                Id = "publication-record-1",
                PublicationId = "publication-1",
                PublicationIdHash = "publication-hash-1",
                SlotId = "slot-1",
                SlotIdHash = "slot-hash-1",
                SlotName = "production",
                WorkflowDefinitionId = "definition-1",
                WorkflowDefinitionVersionId = "definition-version-1",
                ArtifactId = "artifact-1",
                ExpectedSlotRevision = 4,
                Status = PublicationStatus,
                CreatedAtUtcTicks = timestamp.UtcTicks,
                CreatedAtOffsetMinutes = 0,
                ActivatedAtUtcTicks = timestamp.UtcTicks,
                ActivatedAtOffsetMinutes = 0,
                TenantId = "tenant-a",
                TenantIdHash = "tenant-hash-a",
                Revision = 2
            });
            await database.SaveChangesAsync();
        }

        await using (var host = CreateHost())
        {
            var shell = await host.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
            await using var scope = shell.ServiceProvider.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<PublishingSnapshotReviewDbContext>();
            var publication = await database.PublicationRecords.AsNoTracking().SingleAsync(x => x.Id == "publication-record-1");

            Assert.Equal("publication-1", publication.PublicationId);
            Assert.Equal("definition-1", publication.WorkflowDefinitionId);
            Assert.Equal(PublicationStatus, publication.Status);
            Assert.Equal(4, publication.ExpectedSlotRevision);
            Assert.Equal(2, publication.Revision);
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
                [$"CShells:Shells:{ShellName}:Features:{FeatureName}"] = null,
                [$"CShells:Shells:{ShellName}:Features:WorkflowsDesignEntityFrameworkCore"] = null,
                [$"CShells:Shells:{ShellName}:Features:ActivitiesDesignEntityFrameworkCore"] = null
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddEfPersistenceResources(configuration, typeof(SharedPersistenceCompositionTests).Assembly);
        services.AddCShells(shells => shells
            .WithAssemblies(
                typeof(EventsFeature).Assembly,
                typeof(ApiCapabilitiesFeature).Assembly,
                typeof(WorkflowsRuntimeTriggersFeature).Assembly,
                typeof(WorkflowsPublishingFeature).Assembly,
                typeof(PublishingEntityFrameworkCoreFeature).Assembly,
                typeof(WorkflowsDesignEntityFrameworkCoreFeature).Assembly,
                typeof(ActivitiesDesignEntityFrameworkCoreFeature).Assembly)
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
