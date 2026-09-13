using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

public sealed class RuntimeBookmarkEfRegistrationInteropTests
{
    private static readonly RuntimeBookmarksEntityFrameworkCoreOptions EfOptions = new()
    {
        Provider = "Sqlite",
        ConnectionString = "Data Source=:memory:"
    };

    [Fact]
    public void Groundwork_then_ef_withdraws_only_the_groundwork_bookmark_backend()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddGroundworkV2RuntimeStores();

        services.AddRuntimeBookmarksEntityFrameworkCore(EfOptions);

        Assert.Equal(BookmarkStateStoreBackend.EntityFramework, BookmarkStateStoreBackend.Find(services)!.Name);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(GroundworkV2BookmarkStateStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(EfBookmarkStateStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IBookmarkStateStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IBookmarkStimulusIndex));
    }

    [Fact]
    public void Ef_then_groundwork_withdraws_only_the_ef_bookmark_backend()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddRuntimeBookmarksEntityFrameworkCore(EfOptions);

        services.AddGroundworkV2RuntimeStores();

        Assert.Equal(BookmarkStateStoreBackend.Groundwork, BookmarkStateStoreBackend.Find(services)!.Name);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(EfBookmarkStateStore));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(BookmarkStateDbContext));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(GroundworkV2BookmarkStateStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IBookmarkStateStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IBookmarkStimulusIndex));
    }

    [Fact]
    public void Backend_switch_refuses_to_delete_an_unowned_stimulus_index()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddRuntimeBookmarksEntityFrameworkCore(EfOptions);
        services.AddSingleton<IBookmarkStimulusIndex, UnownedStimulusIndex>();

        Assert.Throws<InvalidOperationException>(() => services.AddGroundworkV2RuntimeStores());
        Assert.Contains(services, descriptor => descriptor.ImplementationType == typeof(UnownedStimulusIndex));
    }

    private sealed class UnownedStimulusIndex : IBookmarkStimulusIndex
    {
        public ValueTask<RuntimeStorePage<BookmarkState>> ListByStimulusPageAsync(
            BookmarkStimulusPageQuery query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<RuntimeStorePage<BookmarkState>> ListByStimulusTypePageAsync(
            BookmarkStimulusTypePageQuery query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
