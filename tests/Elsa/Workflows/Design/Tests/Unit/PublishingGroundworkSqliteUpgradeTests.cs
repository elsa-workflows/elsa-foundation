using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Sqlite;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Design.Persistence.EFCore.Sqlite;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Sqlite;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Sqlite.DependencyInjection;
using Groundwork.Core.Capabilities;
using Groundwork.Documents.Store;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

public sealed class PublishingGroundworkSqliteUpgradeTests : IAsyncDisposable
{
    private const string ExistingDefinitionId = "11rX9zJyZW0";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"elsa-publishing-upgrade-{Guid.NewGuid():N}");
    private string DesignConnection => $"Data Source={Path.Combine(_directory, "design.db")}";
    private string RuntimeConnection => $"Data Source={Path.Combine(_directory, "runtime.db")}";
    private string PublishingConnection => $"Data Source={Path.Combine(_directory, "publishing.db")}";

    [Fact]
    public async Task Adding_publishing_lane_preserves_existing_ef_design_data_and_persists_publication_authority()
    {
        Directory.CreateDirectory(_directory);
        await SeedExistingDesignDefinitionAsync();
        var review = Review("review-token");

        await using (var upgradedHost = await BuildHostAsync())
        {
            await AssertExistingDefinitionVisibleAsync(upgradedHost);
            Assert.NotSame(
                upgradedHost.GetRequiredService<IDocumentStore>(),
                upgradedHost.GetRequiredService<PublishingGroundworkDocumentStoreHolder>().Store);

            Assert.True(await upgradedHost.GetRequiredService<IPublicationSnapshotReviewStore>().TryAddAsync(review));
            var activated = await upgradedHost.GetRequiredService<IPublicationSlotStore>()
                .TryActivateAsync("definition-1", "default", "publication-1", 0, DateTimeOffset.UnixEpoch);
            Assert.True(activated.Succeeded);
        }

        await using var restartedHost = await BuildHostAsync();
        await AssertExistingDefinitionVisibleAsync(restartedHost);
        Assert.Equal(review, await restartedHost.GetRequiredService<IPublicationSnapshotReviewStore>()
            .FindAsync(review.PreflightToken));
        Assert.Equal("publication-1", (await restartedHost.GetRequiredService<IPublicationSlotStore>()
            .FindAsync("definition-1", "default"))?.ActivePublicationId);
    }

    private async Task<ServiceProvider> BuildHostAsync()
    {
        var services = new ServiceCollection()
            .AddSingleton<ISystemClock, TestSystemClock>();
        new SqliteWorkflowsDesignPersistenceShellFeature
        {
            ConnectionString = DesignConnection,
            RunMigrations = false,
            UseCommands = false
        }.ConfigureServices(services);
        services.AddSqliteGroundworkDocumentStore(
            RuntimeConnection,
            ElsaRuntimeStorageManifest.Create(),
            new ProviderIdentity("upgrade-runtime-sqlite", "1.0.0"));
        services.AddGroundworkRuntimeStores();
        services.AddGroundworkPublishingSqlitePersistence(PublishingConnection);
        var provider = services.BuildServiceProvider();
        await provider.InitializeGroundworkStoreAsync();
        return provider;
    }

    private async Task SeedExistingDesignDefinitionAsync()
    {
        var services = new ServiceCollection()
            .AddSingleton<ISystemClock, TestSystemClock>()
            .AddScoped<IEntityModelCreatingHandler, SqliteEntityModelCreatingHandler>();
        await using var provider = services.BuildServiceProvider();
        var options = new DbContextOptionsBuilder<WorkflowsDesignDbContext>()
            .UseSqlite(DesignConnection, builder => builder.MigrationsAssembly(
                typeof(SqliteWorkflowsDesignPersistenceShellFeature).Assembly.GetName().Name))
            .Options;
        await using var context = new WorkflowsDesignDbContext(options, provider);
        await context.Database.MigrateAsync();
        context.WorkflowDefinitions.Add(new WorkflowDefinition { Id = ExistingDefinitionId, Name = "Existing workflow" });
        await context.SaveChangesAsync();
    }

    private static async Task AssertExistingDefinitionVisibleAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var definition = await scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>()
            .FindByIdAsync(ExistingDefinitionId);
        Assert.Equal("Existing workflow", definition?.Name);
    }

    private static PublicationSnapshotReview Review(string token) => new(
        token, "sha256:candidate", "definition-1", PublicationAction.Replace, "default",
        PublicationPolicySource.Workflow, 7, PublicationAction.Replace, "default", null,
        0, null, "tenant-a", DateTimeOffset.UtcNow.AddHours(1));

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
        return ValueTask.CompletedTask;
    }

    private sealed class TestSystemClock : ISystemClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
