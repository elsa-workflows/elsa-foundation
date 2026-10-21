using Elsa.Etl.Core;
using Elsa.Etl.Design;
using Elsa.Etl.Design.Stores;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Etl.Tests;

public sealed class EtlDesignHostTests
{
    [Fact]
    public async Task Host_auto_migrates_then_reopens_under_validate_and_reads_the_published_artifact()
    {
        await using var database = new TestDatabase();
        var scope = new EtlScope("tenant-a", "partition-a");
        var pipeline = EtlTestPipelines.Create();
        EtlArtifact artifact;

        using (var host = CreateHost(database.ConnectionString, EfMigratePolicy.AutoMigrate))
        {
            await host.StartAsync();
            await using var services = host.Services.CreateAsyncScope();
            var provider = services.ServiceProvider;
            Assert.IsType<EtlDesignSqliteDbContext>(provider.GetRequiredService<EtlDesignDbContext>());

            var store = provider.GetRequiredService<IEtlDefinitionStore>();
            var compiler = provider.GetRequiredService<IEtlArtifactCompiler>();
            var created = await store.CreateDraftAsync(scope, "orders", pipeline);
            Assert.Equal(EtlDefinitionWriteStatus.Created, created.Status);
            artifact = Assert.IsType<EtlArtifact>(compiler.Compile("orders", 1, pipeline).Artifact);
            Assert.Equal(EtlPublicationStatus.Published,
                (await store.PublishAsync(scope, "orders", created.Draft!.Revision, artifact)).Status);

            await host.StopAsync();
        }

        // A separate host and fresh service provider prove that startup validates the same persisted schema.
        using (var host = CreateHost(database.ConnectionString, EfMigratePolicy.Validate))
        {
            await host.StartAsync();
            await using var services = host.Services.CreateAsyncScope();
            var store = services.ServiceProvider.GetRequiredService<IEtlDefinitionStore>();
            var draft = Assert.IsType<EtlDefinitionDraft>(await store.FindDraftAsync(scope, "orders"));
            var reopened = Assert.IsType<EtlArtifact>(await store.FindPublishedAsync(scope, "orders", 1));

            Assert.Equal(1, draft.LastPublishedVersion);
            Assert.Equal(artifact.ArtifactId, reopened.ArtifactId);
            Assert.Equal(artifact.ContentHash, reopened.ContentHash);
            Assert.Equal(artifact.Pipeline.GetRawText(), reopened.Pipeline.GetRawText());
        }
    }

    [Fact]
    public async Task Normal_host_registration_publishes_and_reopens_connector_definitions()
    {
        await using var database = new TestDatabase();
        var scope = new EtlScope("tenant-a", "partition-a");
        var pipeline = EtlV2TestPipelines.Create();
        EtlArtifact artifact;
        using (var host = CreateHost(database.ConnectionString, EfMigratePolicy.AutoMigrate))
        {
            await host.StartAsync();
            await using var services = host.Services.CreateAsyncScope();
            var provider = services.ServiceProvider;
            var store = provider.GetRequiredService<IEtlDefinitionStore>();
            var connectorStore = provider.GetRequiredService<IEtlDefinitionStoreV2>();
            Assert.Same(store, connectorStore);
            var compiler = provider.GetRequiredService<IEtlArtifactCompilerV2>();
            Assert.Same(provider.GetRequiredService<IEtlArtifactCompiler>(), compiler);
            var created = await connectorStore.CreateDraftAsync(scope, "orders", pipeline);
            Assert.Equal(EtlDefinitionWriteStatus.Created, created.Status);
            artifact = Assert.IsType<EtlArtifact>(compiler.Compile("orders", 1, pipeline).Artifact);
            Assert.Equal(EtlPublicationStatus.Published,
                (await store.PublishAsync(scope, "orders", created.Draft!.Revision, artifact)).Status);
            await host.StopAsync();
        }
        using (var host = CreateHost(database.ConnectionString, EfMigratePolicy.Validate))
        {
            await host.StartAsync();
            await using var services = host.Services.CreateAsyncScope();
            var provider = services.ServiceProvider;
            var draft = Assert.IsType<EtlDefinitionDraftV2>(await provider.GetRequiredService<IEtlDefinitionStoreV2>().FindDraftAsync(scope, "orders"));
            var reopened = Assert.IsType<EtlArtifact>(await provider.GetRequiredService<IEtlDefinitionStore>().FindPublishedAsync(scope, "orders", 1));
            Assert.Equal(1, draft.LastPublishedVersion);
            Assert.Equal(2, reopened.FormatVersion);
            Assert.Equal(artifact.ArtifactId, reopened.ArtifactId);
            Assert.Equal(artifact.ContentHash, reopened.ContentHash);
            Assert.Equal(artifact.Pipeline.GetRawText(), reopened.Pipeline.GetRawText());
            Assert.Equal(2, Assert.Single(await provider.GetRequiredService<IEtlDefinitionCatalog>().ListDefinitionsAsync(scope, null, 10)).FormatVersion);
        }
    }

    private static IHost CreateHost(string connectionString, EfMigratePolicy policy)
    {
        var builder = Host.CreateApplicationBuilder();
        new EtlDesignFeature().ConfigureServices(builder.Services);
        new EtlDesignEntityFrameworkCoreFeature
        {
            Provider = "Sqlite",
            ConnectionString = connectionString
        }.ConfigureServices(builder.Services);
        builder.Services.Configure<EfMigrateOptions>(options => options.Policy = policy);
        return builder.Build();
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "Elsa.Etl.Host.Tests", Guid.NewGuid().ToString("N"));

        public string ConnectionString { get; }

        public TestDatabase()
        {
            Directory.CreateDirectory(_directory);
            ConnectionString = $"Data Source={Path.Combine(_directory, "etl.db")};Pooling=False";
        }

        public ValueTask DisposeAsync()
        {
            Directory.Delete(_directory, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
