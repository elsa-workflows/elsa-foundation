using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Groundwork.Store;
using Groundwork.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

public sealed class RuntimeBookmarkEfRegistrationInteropTests
{
    private const string SharedRecoveryContinuationSigningKey =
        "ef-runtime-sibling-shared-signing-key-32-bytes";

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

    private static readonly RuntimeActivityExecutionEntityFrameworkCoreOptions ActivityEfOptions = new()
    {
        Provider = "Sqlite",
        ConnectionString = "Data Source=:memory:",
        HierarchyCursorSigningKey = "ef-runtime-activity-switch-hierarchy-key-32-bytes",
        RecoveryContinuationSigningKey = SharedRecoveryContinuationSigningKey
    };

    private static readonly RuntimeWorkflowExecutionEntityFrameworkCoreOptions WorkflowEfOptions = new()
    {
        Provider = "Sqlite",
        ConnectionString = "Data Source=:memory:",
        RecoveryContinuationSigningKey = SharedRecoveryContinuationSigningKey
    };

    private static readonly RuntimeWorkflowAlterationEntityFrameworkCoreOptions AlterationEfOptions = new()
    {
        Provider = "Sqlite",
        ConnectionString = "Data Source=:memory:",
        RecoveryContinuationSigningKey = SharedRecoveryContinuationSigningKey
    };

    private static readonly RuntimeWorkflowTestScopeEntityFrameworkCoreOptions ScopeEfOptions = new()
    {
        Provider = "Sqlite",
        ConnectionString = "Data Source=:memory:",
        RecoveryContinuationSigningKey = SharedRecoveryContinuationSigningKey
    };

    [Fact]
    public void Workflow_execution_backend_switches_both_directions_without_duplicate_markers()
    {
        var groundworkFirst = new ServiceCollection().AddWorkflowRuntime();
        groundworkFirst.AddGroundworkV2RuntimeStores();
        groundworkFirst.AddRuntimeWorkflowExecutionEntityFrameworkCore(WorkflowEfOptions);
        Assert.Equal(WorkflowExecutionStateStoreBackend.EntityFramework, WorkflowExecutionStateStoreBackend.Find(groundworkFirst)!.Name);
        Assert.Single(groundworkFirst, descriptor => descriptor.ServiceType == typeof(IWorkflowExecutionStateStore));
        Assert.DoesNotContain(groundworkFirst, descriptor => descriptor.ServiceType == typeof(GroundworkV2WorkflowExecutionStateStore));

        var efFirst = new ServiceCollection().AddWorkflowRuntime();
        efFirst.AddRuntimeWorkflowExecutionEntityFrameworkCore(WorkflowEfOptions);
        efFirst.AddGroundworkV2RuntimeStores();
        Assert.Equal(WorkflowExecutionStateStoreBackend.Groundwork, WorkflowExecutionStateStoreBackend.Find(efFirst)!.Name);
        Assert.Single(efFirst, descriptor => descriptor.ServiceType == typeof(IWorkflowExecutionStateStore));
        Assert.DoesNotContain(efFirst, descriptor => descriptor.ServiceType == typeof(EfWorkflowExecutionStateStore));
        Assert.Single(efFirst, descriptor => descriptor.ServiceType == typeof(GroundworkV2WorkflowExecutionStateStore));
    }

    [Fact]
    public void Alteration_and_test_scope_backends_switch_both_directions_without_stale_units()
    {
        var groundworkFirst = new ServiceCollection().AddWorkflowRuntime();
        groundworkFirst.AddGroundworkV2RuntimeStores();
        Assert.Equal(RuntimeWorkflowAlterationStoreBackend.Groundwork, RuntimeWorkflowAlterationStoreBackend.Find(groundworkFirst)!.Name);
        Assert.Equal(WorkflowTestScopeStoreBackend.Groundwork, WorkflowTestScopeStoreBackend.Find(groundworkFirst)!.Name);

        groundworkFirst.AddRuntimeWorkflowAlterationEntityFrameworkCore(AlterationEfOptions);
        groundworkFirst.AddRuntimeWorkflowTestScopeEntityFrameworkCore(ScopeEfOptions);

        Assert.Equal(RuntimeWorkflowAlterationStoreBackend.EntityFramework, RuntimeWorkflowAlterationStoreBackend.Find(groundworkFirst)!.Name);
        Assert.Equal(WorkflowTestScopeStoreBackend.EntityFramework, WorkflowTestScopeStoreBackend.Find(groundworkFirst)!.Name);
        Assert.DoesNotContain(groundworkFirst, descriptor => descriptor.ServiceType == typeof(GroundworkV2WorkflowAlterationStore));
        Assert.DoesNotContain(groundworkFirst, descriptor => descriptor.ServiceType == typeof(GroundworkV2WorkflowTestScopeStore));
        Assert.DoesNotContain(groundworkFirst, descriptor => descriptor.ServiceType == typeof(GroundworkV2WorkflowTestScopeCleanupStore));
        Assert.Single(groundworkFirst, descriptor => descriptor.ServiceType == typeof(IWorkflowTestScopeCleanupStore));
        Assert.Single(groundworkFirst, descriptor => descriptor.ServiceType == typeof(EfWorkflowTestScopeCleanupStore));
        Assert.Single(groundworkFirst, descriptor => descriptor.ServiceType == typeof(BookmarkStateSqliteDbContext));
        Assert.Single(groundworkFirst, descriptor => descriptor.ServiceType == typeof(BookmarkStateDbContext));
        Assert.DoesNotContain(groundworkFirst
            .Where(descriptor => descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry))
            .SelectMany(descriptor => ((GroundworkStorageUnitRegistry)descriptor.ImplementationInstance!).Registrations),
            registration => registration.Unit.Id.Value is ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanDocumentKind
                or ElsaRuntimeV2StorageManifest.WorkflowAlterationJobDocumentKind
                or ElsaRuntimeV2StorageManifest.WorkflowTestScopeDocumentKind);

        groundworkFirst.AddGroundworkV2RuntimeStores();
        Assert.Equal(RuntimeWorkflowAlterationStoreBackend.Groundwork, RuntimeWorkflowAlterationStoreBackend.Find(groundworkFirst)!.Name);
        Assert.Equal(WorkflowTestScopeStoreBackend.Groundwork, WorkflowTestScopeStoreBackend.Find(groundworkFirst)!.Name);
        Assert.Single(groundworkFirst, descriptor => descriptor.ServiceType == typeof(GroundworkV2WorkflowAlterationStore));
        Assert.Single(groundworkFirst, descriptor => descriptor.ServiceType == typeof(GroundworkV2WorkflowTestScopeStore));
        Assert.Single(groundworkFirst, descriptor => descriptor.ServiceType == typeof(GroundworkV2WorkflowTestScopeCleanupStore));
        Assert.Single(groundworkFirst, descriptor => descriptor.ServiceType == typeof(IWorkflowTestScopeCleanupStore));
        Assert.DoesNotContain(groundworkFirst, descriptor => descriptor.ServiceType == typeof(EfWorkflowAlterationStore));
        Assert.DoesNotContain(groundworkFirst, descriptor => descriptor.ServiceType == typeof(EfWorkflowTestScopeStore));
        Assert.Equal(ElsaRuntimeV2StorageManifest.CreateUnits().Count, Assert.IsType<GroundworkStorageUnitRegistry>(groundworkFirst.Single(descriptor => descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry)).ImplementationInstance).Registrations.Count);

        groundworkFirst.AddGroundworkV2RuntimeStores();
        Assert.Equal(ElsaRuntimeV2StorageManifest.CreateUnits().Count, Assert.IsType<GroundworkStorageUnitRegistry>(groundworkFirst.Single(descriptor => descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry)).ImplementationInstance).Registrations.Count);

        var efFirst = new ServiceCollection().AddWorkflowRuntime();
        efFirst.AddRuntimeWorkflowAlterationEntityFrameworkCore(AlterationEfOptions);
        efFirst.AddRuntimeWorkflowTestScopeEntityFrameworkCore(ScopeEfOptions);
        efFirst.AddGroundworkV2RuntimeStores();
        Assert.Equal(RuntimeWorkflowAlterationStoreBackend.Groundwork, RuntimeWorkflowAlterationStoreBackend.Find(efFirst)!.Name);
        Assert.Equal(WorkflowTestScopeStoreBackend.Groundwork, WorkflowTestScopeStoreBackend.Find(efFirst)!.Name);
        Assert.DoesNotContain(efFirst, descriptor => descriptor.ServiceType == typeof(EfWorkflowAlterationStore));
        Assert.DoesNotContain(efFirst, descriptor => descriptor.ServiceType == typeof(EfWorkflowTestScopeStore));
        Assert.DoesNotContain(efFirst, descriptor => descriptor.ServiceType == typeof(BookmarkStateSqliteDbContext));
        Assert.DoesNotContain(efFirst, descriptor => descriptor.ServiceType == typeof(BookmarkStateDbContext));
        Assert.Single(efFirst, descriptor => descriptor.ServiceType == typeof(IWorkflowTestScopeCleanupStore));
    }

    [Fact]
    public void Groundwork_backend_conflicts_leave_the_service_and_manifest_snapshots_unchanged()
    {
        var services = new ServiceCollection().AddWorkflowRuntime();
        services.AddGroundworkV2RuntimeStores();
        var foreignAlteration = ServiceDescriptor.Scoped<IWorkflowAlterationStore>(_ =>
            throw new NotSupportedException("foreign alteration store"));
        var foreignScope = ServiceDescriptor.Scoped<IWorkflowTestScopeAdmissionStore>(_ =>
            throw new NotSupportedException("foreign test-scope admission store"));
        services.Add(foreignAlteration);
        services.Add(foreignScope);
        var serviceSnapshot = services.ToArray();
        var registrySnapshot = Registry(services).Registrations;

        Assert.Throws<InvalidOperationException>(() => services.AddGroundworkV2RuntimeStores());

        Assert.Equal(serviceSnapshot, services);
        Assert.Equal(registrySnapshot, Registry(services).Registrations);
    }

    [Theory]
    [InlineData("workflow-artifacts-bookmarks-activity")]
    [InlineData("activity-bookmarks-artifacts-workflow")]
    public void All_runtime_ef_siblings_share_one_context_in_reverse_registration_orders(string order)
    {
        var services = new ServiceCollection().AddWorkflowRuntime();
        if (order.StartsWith("workflow", StringComparison.Ordinal))
        {
            services.AddRuntimeWorkflowExecutionEntityFrameworkCore(WorkflowEfOptions);
            services.AddRuntimeArtifactsEntityFrameworkCore(ArtifactEfOptions);
            services.AddRuntimeBookmarksEntityFrameworkCore(EfOptions);
            services.AddRuntimeActivityExecutionEntityFrameworkCore(ActivityEfOptions);
        }
        else
        {
            services.AddRuntimeActivityExecutionEntityFrameworkCore(ActivityEfOptions);
            services.AddRuntimeBookmarksEntityFrameworkCore(EfOptions);
            services.AddRuntimeArtifactsEntityFrameworkCore(ArtifactEfOptions);
            services.AddRuntimeWorkflowExecutionEntityFrameworkCore(WorkflowEfOptions);
        }

        Assert.Equal(4, services.Count(descriptor => descriptor.ImplementationInstance is
            RuntimeWorkflowExecutionEntityFrameworkCoreOptions or
            RuntimeArtifactsEntityFrameworkCoreOptions or
            RuntimeBookmarksEntityFrameworkCoreOptions or
            RuntimeActivityExecutionEntityFrameworkCoreOptions));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(BookmarkStateSqliteDbContext));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(BookmarkStateDbContext));
        Assert.Equal(WorkflowExecutionStateStoreBackend.EntityFramework, WorkflowExecutionStateStoreBackend.Find(services)!.Name);
        Assert.Equal(RuntimeArtifactStoreBackend.EntityFramework, RuntimeArtifactStoreBackend.Find(services)!.Name);
        Assert.Equal(BookmarkStateStoreBackend.EntityFramework, BookmarkStateStoreBackend.Find(services)!.Name);
        Assert.Equal(RuntimeActivityExecutionStoreBackend.EntityFramework, RuntimeActivityExecutionStoreBackend.Find(services)!.Name);
    }

    [Fact]
    public void Workflow_execution_ef_replaces_an_owned_custom_inmemory_backend_once()
    {
        var services = new ServiceCollection();
        var custom = ServiceDescriptor.Singleton<IWorkflowExecutionStateStore, CustomWorkflowExecutionStateStore>();
        ((IServiceCollection)services).Add(custom);
        WorkflowExecutionStateStoreBackend.Register(services, new WorkflowExecutionStateStoreBackend(
            WorkflowExecutionStateStoreBackend.InMemory, [custom]));
        services.AddRuntimeWorkflowExecutionEntityFrameworkCore(WorkflowEfOptions);
        Assert.Equal(WorkflowExecutionStateStoreBackend.EntityFramework, WorkflowExecutionStateStoreBackend.Find(services)!.Name);
        Assert.DoesNotContain(services, descriptor => ReferenceEquals(descriptor, custom));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IWorkflowExecutionStateStore));
    }

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
    public void Groundwork_and_ef_activity_execution_backend_switch_fails_closed_until_checkpoint_ownership_is_coherent()
    {
        var groundworkServices = new ServiceCollection();
        groundworkServices.AddWorkflowRuntime();
        groundworkServices.AddGroundworkV2RuntimeStores();

        Assert.Throws<InvalidOperationException>(() => groundworkServices.AddRuntimeActivityExecutionEntityFrameworkCore(ActivityEfOptions));
        Assert.Equal(RuntimeActivityExecutionStoreBackend.Groundwork, RuntimeActivityExecutionStoreBackend.Find(groundworkServices)!.Name);
        Assert.Single(groundworkServices, descriptor => descriptor.ServiceType == typeof(GroundworkV2ActivityExecutionStateStore));

        var efServices = new ServiceCollection();
        efServices.AddWorkflowRuntime();
        efServices.AddRuntimeActivityExecutionEntityFrameworkCore(ActivityEfOptions);

        Assert.Throws<InvalidOperationException>(() => efServices.AddGroundworkV2RuntimeStores());
        Assert.Equal(RuntimeActivityExecutionStoreBackend.EntityFramework, RuntimeActivityExecutionStoreBackend.Find(efServices)!.Name);
        Assert.Single(efServices, descriptor => descriptor.ServiceType == typeof(EfActivityExecutionStateStore));
    }

    [Fact]
    public void Activity_execution_backends_are_idempotent_when_registered_without_switching()
    {
        var efServices = new ServiceCollection();
        efServices.AddWorkflowRuntime();
        efServices.AddRuntimeActivityExecutionEntityFrameworkCore(ActivityEfOptions);
        efServices.AddRuntimeActivityExecutionEntityFrameworkCore(ActivityEfOptions);
        Assert.Equal(RuntimeActivityExecutionStoreBackend.EntityFramework, RuntimeActivityExecutionStoreBackend.Find(efServices)!.Name);

        var groundworkServices = new ServiceCollection();
        groundworkServices.AddWorkflowRuntime();
        groundworkServices.AddGroundworkV2RuntimeStores();
        groundworkServices.AddGroundworkV2RuntimeStores();
        Assert.Equal(RuntimeActivityExecutionStoreBackend.Groundwork, RuntimeActivityExecutionStoreBackend.Find(groundworkServices)!.Name);
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
        services.AddRuntimeWorkflowExecutionEntityFrameworkCore(WorkflowEfOptions);

        var registry = Registry(services);
        Assert.DoesNotContain(registry.Registrations, registration =>
            registration.TargetName == "runtime" &&
            (registration.Unit.Id.Value == ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind ||
             registration.Unit.Id.Value == ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind ||
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

    [Theory]
    [InlineData("workflow-artifacts-bookmarks")]
    [InlineData("bookmarks-artifacts-workflow")]
    public void Shared_context_survives_each_ef_sibling_withdrawal_in_reverse_load_orders(string order)
    {
        var services = new ServiceCollection().AddWorkflowRuntime();
        if (order.StartsWith("workflow", StringComparison.Ordinal))
        {
            services.AddRuntimeWorkflowExecutionEntityFrameworkCore(WorkflowEfOptions);
            services.AddRuntimeArtifactsEntityFrameworkCore(ArtifactEfOptions);
            services.AddRuntimeBookmarksEntityFrameworkCore(EfOptions);
        }
        else
        {
            services.AddRuntimeBookmarksEntityFrameworkCore(EfOptions);
            services.AddRuntimeArtifactsEntityFrameworkCore(ArtifactEfOptions);
            services.AddRuntimeWorkflowExecutionEntityFrameworkCore(WorkflowEfOptions);
        }

        var workflowBackend = WorkflowExecutionStateStoreBackend.Find(services)!;
        workflowBackend.PrepareRemoveOwnedArtifacts(services)?.Invoke(services);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(BookmarkStateSqliteDbContext));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(BookmarkStateDbContext));

        RuntimeArtifactStoreBackend.Find(services)!.RemoveOwnedArtifacts(services);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(BookmarkStateSqliteDbContext));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(BookmarkStateDbContext));

        BookmarkStateStoreBackend.Find(services)!.RemoveOwnedArtifacts(services);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(BookmarkStateSqliteDbContext));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(BookmarkStateDbContext));
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

    private sealed class CustomWorkflowExecutionStateStore : IWorkflowExecutionStateStore
    {
        public ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowExecutionStatePage> QueryPageAsync(WorkflowExecutionStatePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> DeleteAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
