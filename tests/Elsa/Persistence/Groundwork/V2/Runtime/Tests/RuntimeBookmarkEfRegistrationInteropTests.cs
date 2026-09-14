using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Groundwork.Store;
using Groundwork.Sqlite;
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

    private static readonly RuntimeArtifactsEntityFrameworkCoreOptions ArtifactEfOptions = new()
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
    public void Groundwork_registration_remains_reorderable_with_runtime_defaults()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddGroundworkV2RuntimeStores();

        services.AddWorkflowRuntime();
        services.AddGroundworkV2RuntimeStores();

        Assert.Equal(BookmarkStateStoreBackend.Groundwork, BookmarkStateStoreBackend.Find(services)!.Name);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(GroundworkV2BookmarkStateStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IBookmarkStateStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IBookmarkStimulusIndex));
    }

    [Fact]
    public void Groundwork_refuses_to_replace_an_explicit_unowned_state_store()
    {
        var services = new ServiceCollection();
        var explicitStore = ServiceDescriptor.Singleton<IBookmarkStateStore, UnownedStateStore>();
        ((IServiceCollection)services).Add(explicitStore);

        Assert.Throws<InvalidOperationException>(() => services.AddGroundworkV2RuntimeStores());

        Assert.Contains(services, descriptor => ReferenceEquals(descriptor, explicitStore));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(GroundworkV2BookmarkStateStore));
    }

    [Fact]
    public void Groundwork_then_ef_refuses_an_unowned_provider_context_before_removing_groundwork()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddGroundworkV2RuntimeStores();
        var hostOwnedContext = ServiceDescriptor.Scoped<BookmarkStateSqliteDbContext>(_ =>
            throw new NotSupportedException());
        ((IServiceCollection)services).Add(hostOwnedContext);

        Assert.Throws<InvalidOperationException>(() =>
            services.AddRuntimeBookmarksEntityFrameworkCore(EfOptions));

        Assert.Equal(BookmarkStateStoreBackend.Groundwork, BookmarkStateStoreBackend.Find(services)!.Name);
        Assert.Contains(services, descriptor => ReferenceEquals(descriptor, hostOwnedContext));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(GroundworkV2BookmarkStateStore));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(EfBookmarkStateStore));
    }

    [Fact]
    public void Groundwork_then_ef_refuses_a_replaced_concrete_backend_without_removing_it()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddGroundworkV2RuntimeStores();
        var ownedImplementation = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(GroundworkV2BookmarkStateStore));
        services.Remove(ownedImplementation);
        var hostOwnedImplementation = ServiceDescriptor.Scoped<GroundworkV2BookmarkStateStore>(_ =>
            throw new NotSupportedException());
        ((IServiceCollection)services).Add(hostOwnedImplementation);

        Assert.Throws<InvalidOperationException>(() =>
            services.AddRuntimeBookmarksEntityFrameworkCore(EfOptions));

        Assert.Equal(BookmarkStateStoreBackend.Groundwork, BookmarkStateStoreBackend.Find(services)!.Name);
        Assert.Contains(services, descriptor => ReferenceEquals(descriptor, hostOwnedImplementation));
        Assert.DoesNotContain(services, descriptor => ReferenceEquals(descriptor, ownedImplementation));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(EfBookmarkStateStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IBookmarkStateStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IBookmarkStimulusIndex));
    }

    [Fact]
    public async Task Groundwork_to_ef_withdraws_only_selected_target_units_and_back_redeclares_them_before_admission()
    {
        using var runtimeConnection = new SqliteProviderFactory().Create("Data Source=:memory:");
        using var otherConnection = new SqliteProviderFactory().Create("Data Source=:memory:");
        var services = new ServiceCollection()
            .AddWorkflowRuntime()
            .AddGroundworkStorageProviderConnection(runtimeConnection, "runtime")
            .AddGroundworkStorageProviderConnection(otherConnection, "other");
        services.AddGroundworkStorageUnit(
            ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.ActivityExecutionStateDocumentKind),
            "other");
        services.AddGroundworkV2RuntimeStores("runtime");

        services.AddRuntimeBookmarksEntityFrameworkCore(EfOptions);
        services.AddRuntimeArtifactsEntityFrameworkCore(ArtifactEfOptions);

        var registry = Registry(services);
        Assert.DoesNotContain(registry.Registrations, registration =>
            registration.TargetName == "runtime" &&
            (registration.Unit.Id.Value == ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind ||
             ArtifactUnitIds.Contains(registration.Unit.Id.Value, StringComparer.Ordinal)));
        Assert.Contains(registry.Registrations, registration =>
            registration.TargetName == "other" &&
            registration.Unit.Id.Value == ElsaRuntimeV2StorageManifest.ActivityExecutionStateDocumentKind);

        using (var provider = services.BuildServiceProvider())
            await provider.GetRequiredService<IShellInitializer>().InitializeAsync();

        services.AddGroundworkV2RuntimeStores("runtime");
        registry = Registry(services);
        Assert.All(
            ElsaRuntimeV2StorageManifest.CreateUnits().Select(unit => unit.Id.Value),
            unitId => Assert.Equal(unitId, registry.Require(unitId, "runtime").Unit.Id.Value));
        Assert.Contains(registry.Registrations, registration =>
            registration.TargetName == "other" &&
            registration.Unit.Id.Value == ElsaRuntimeV2StorageManifest.ActivityExecutionStateDocumentKind);

        using (var provider = services.BuildServiceProvider())
            await provider.GetRequiredService<IShellInitializer>().InitializeAsync();
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
    public void Ef_then_groundwork_preserves_a_host_owned_provider_context_registration()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddRuntimeBookmarksEntityFrameworkCore(EfOptions);
        var hostOwnedContext = ServiceDescriptor.Scoped<BookmarkStateSqliteDbContext>(_ =>
            throw new NotSupportedException());
        ((IServiceCollection)services).Add(hostOwnedContext);

        services.AddGroundworkV2RuntimeStores();

        Assert.Contains(services, descriptor => ReferenceEquals(descriptor, hostOwnedContext));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(BookmarkStateSqliteDbContext));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(BookmarkStateDbContext));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(EfBookmarkStateStore));
    }

    [Theory]
    [InlineData("bookmarks-first")]
    [InlineData("artifacts-first")]
    public void Combined_runtime_ef_registration_is_idempotent_when_each_sibling_is_repeated(string first)
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();

        if (first == "bookmarks-first")
        {
            services.AddRuntimeBookmarksEntityFrameworkCore(EfOptions);
            services.AddRuntimeArtifactsEntityFrameworkCore(ArtifactEfOptions);
            services.AddRuntimeBookmarksEntityFrameworkCore(EfOptions);
            services.AddRuntimeArtifactsEntityFrameworkCore(ArtifactEfOptions);
        }
        else
        {
            services.AddRuntimeArtifactsEntityFrameworkCore(ArtifactEfOptions);
            services.AddRuntimeBookmarksEntityFrameworkCore(EfOptions);
            services.AddRuntimeArtifactsEntityFrameworkCore(ArtifactEfOptions);
            services.AddRuntimeBookmarksEntityFrameworkCore(EfOptions);
        }

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IBookmarkStateStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IBookmarkStimulusIndex));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IWorkflowExecutableStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(BookmarkStateSqliteDbContext));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(BookmarkStateDbContext));
    }

    [Fact]
    public void Artifact_context_ownership_remains_with_bookmark_ef_when_artifact_backend_withdraws()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddRuntimeArtifactsEntityFrameworkCore(ArtifactEfOptions);
        services.AddRuntimeBookmarksEntityFrameworkCore(EfOptions);

        var artifactBackend = RuntimeArtifactStoreBackend.Find(services);
        Assert.NotNull(artifactBackend);
        artifactBackend!.RemoveOwnedArtifacts(services);

        Assert.Equal(BookmarkStateStoreBackend.EntityFramework, BookmarkStateStoreBackend.Find(services)!.Name);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(BookmarkStateSqliteDbContext));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(BookmarkStateDbContext));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(EfWorkflowExecutableStore));

        services.AddRuntimeArtifactsEntityFrameworkCore(ArtifactEfOptions);
        Assert.Equal(RuntimeArtifactStoreBackend.EntityFramework, RuntimeArtifactStoreBackend.Find(services)!.Name);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(EfWorkflowExecutableStore));
    }

    [Fact]
    public void Bookmark_context_ownership_remains_with_artifact_ef_when_bookmark_backend_withdraws()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddRuntimeArtifactsEntityFrameworkCore(ArtifactEfOptions);
        services.AddRuntimeBookmarksEntityFrameworkCore(EfOptions);

        var bookmarkBackend = BookmarkStateStoreBackend.Find(services);
        Assert.NotNull(bookmarkBackend);
        bookmarkBackend!.RemoveOwnedArtifacts(services);

        Assert.Equal(RuntimeArtifactStoreBackend.EntityFramework, RuntimeArtifactStoreBackend.Find(services)!.Name);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(BookmarkStateSqliteDbContext));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(BookmarkStateDbContext));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(EfWorkflowExecutableStore));

        services.AddRuntimeBookmarksEntityFrameworkCore(EfOptions);
        Assert.Equal(BookmarkStateStoreBackend.EntityFramework, BookmarkStateStoreBackend.Find(services)!.Name);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(EfBookmarkStateStore));
    }

    [Fact]
    public void Combined_ef_backends_withdraw_through_groundwork_and_can_be_restored_in_the_same_order()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddRuntimeArtifactsEntityFrameworkCore(ArtifactEfOptions);
        services.AddRuntimeBookmarksEntityFrameworkCore(EfOptions);

        services.AddGroundworkV2RuntimeStores();

        Assert.Equal(RuntimeArtifactStoreBackend.Groundwork, RuntimeArtifactStoreBackend.Find(services)!.Name);
        Assert.Equal(BookmarkStateStoreBackend.Groundwork, BookmarkStateStoreBackend.Find(services)!.Name);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(BookmarkStateDbContext));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(EfWorkflowExecutableStore));

        services.AddRuntimeArtifactsEntityFrameworkCore(ArtifactEfOptions);
        services.AddRuntimeBookmarksEntityFrameworkCore(EfOptions);

        Assert.Equal(RuntimeArtifactStoreBackend.EntityFramework, RuntimeArtifactStoreBackend.Find(services)!.Name);
        Assert.Equal(BookmarkStateStoreBackend.EntityFramework, BookmarkStateStoreBackend.Find(services)!.Name);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(BookmarkStateSqliteDbContext));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(EfWorkflowExecutableStore));
    }

    [Fact]
    public void Ef_artifacts_then_groundwork_then_ef_artifacts_removes_and_recreates_owned_context()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddRuntimeArtifactsEntityFrameworkCore(ArtifactEfOptions);

        services.AddGroundworkV2RuntimeStores();

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(BookmarkStateSqliteDbContext));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(BookmarkStateDbContext));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(RuntimeArtifactsEntityFrameworkCoreOptions));

        services.AddRuntimeArtifactsEntityFrameworkCore(ArtifactEfOptions);

        Assert.Equal(RuntimeArtifactStoreBackend.EntityFramework, RuntimeArtifactStoreBackend.Find(services)!.Name);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(BookmarkStateSqliteDbContext));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(EfWorkflowExecutableStore));
    }

    [Fact]
    public void Groundwork_then_ef_artifacts_then_groundwork_removes_ef_context_and_options()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddGroundworkV2RuntimeStores();
        services.AddRuntimeArtifactsEntityFrameworkCore(ArtifactEfOptions);

        services.AddGroundworkV2RuntimeStores();

        Assert.Equal(RuntimeArtifactStoreBackend.Groundwork, RuntimeArtifactStoreBackend.Find(services)!.Name);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(BookmarkStateSqliteDbContext));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(BookmarkStateDbContext));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(RuntimeArtifactsEntityFrameworkCoreOptions));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(GroundworkV2WorkflowExecutableStore));
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

    private sealed class UnownedStateStore : IBookmarkStateStore
    {
        public ValueTask<BookmarkState> SaveAsync(BookmarkState state, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<bool> DeleteAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<BookmarkState?> FindAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<RuntimeStorePage<BookmarkState>> ListPageAsync(
            BookmarkStatePageQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static GroundworkStorageUnitRegistry Registry(IServiceCollection services) =>
        Assert.IsType<GroundworkStorageUnitRegistry>(services.Single(descriptor =>
            descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry)).ImplementationInstance);

    private static readonly string[] ArtifactUnitIds =
    [
        ElsaRuntimeV2StorageManifest.WorkflowExecutableDocumentKind,
        ElsaRuntimeV2StorageManifest.WorkflowExecutableCoordinationDocumentKind,
        ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateDocumentKind,
        ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateHashClaimDocumentKind,
        ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDocumentKind
    ];
}
