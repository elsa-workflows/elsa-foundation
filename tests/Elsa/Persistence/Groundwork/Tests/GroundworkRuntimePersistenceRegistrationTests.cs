using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Sqlite;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkRuntimePersistenceRegistrationTests
{
    [Fact]
    public void Default_Runtime_Composition_Keeps_InMemory_Store()
    {
        var services = new ServiceCollection();
        services.TryAddSingleton<IBookmarkStateStore, InMemoryBookmarkStateStore>();
        services.TryAddSingleton<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<InMemoryBookmarkStateStore>(provider.GetRequiredService<IBookmarkStateStore>());
        Assert.IsType<InMemoryWorkflowExecutableStore>(provider.GetRequiredService<IWorkflowExecutableStore>());
    }

    [Fact]
    public void AddGroundworkRuntimeStores_Replaces_InMemory_Store()
    {
        var services = new ServiceCollection();
        services.TryAddSingleton<IBookmarkStateStore, InMemoryBookmarkStateStore>();
        services.TryAddSingleton<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();
        services.AddSingleton<IDocumentStore>(new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create()));

        services.AddGroundworkRuntimeStores();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<GroundworkBookmarkStateStore>(provider.GetRequiredService<IBookmarkStateStore>());
        Assert.IsType<GroundworkWorkflowExecutableStore>(provider.GetRequiredService<IWorkflowExecutableStore>());
    }

    [Fact]
    public async Task Sqlite_Feature_Wires_DocumentStore_And_Bridge()
    {
        var services = new ServiceCollection();
        services.TryAddSingleton<IBookmarkStateStore, InMemoryBookmarkStateStore>();
        services.TryAddSingleton<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();

        new SqliteGroundworkRuntimePersistenceShellFeature().ConfigureServices(services);

        await using var provider = services.BuildServiceProvider();

        Assert.IsType<SqliteGroundworkDocumentStore>(provider.GetRequiredService<IDocumentStore>());
        Assert.IsType<GroundworkBookmarkStateStore>(provider.GetRequiredService<IBookmarkStateStore>());
        Assert.IsType<GroundworkWorkflowExecutableStore>(provider.GetRequiredService<IWorkflowExecutableStore>());
    }
}
