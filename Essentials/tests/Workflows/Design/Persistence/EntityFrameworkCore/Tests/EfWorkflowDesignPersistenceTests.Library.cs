using System.Data.Common;
using Elsa.Locking.Core;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfWorkflowDesignPersistenceTests
{
    private static ServiceProvider LibraryHost(string connection, string tenant = "tenant-a", LibraryCommands? commands = null,
        IWorkflowActivationAuthority? authority = null, IDistributedLockProvider? locking = null, DbCommandInterceptor? failure = null,
        bool includeLockProvider = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPayloadSerializer, TestSerializer>();
        services.AddSingleton<IIdentityGenerator, TestIdentity>();
        if (includeLockProvider) services.AddSingleton<IDistributedLockProvider>(locking ?? new TestLockProvider());
        services.AddSingleton<IPersistenceAccessContextAccessor>(new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(tenant))));
        if (authority is not null) services.AddSingleton(authority);
        services.AddWorkflowsDesignEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = connection });
        if (commands is not null) services.AddDbContext<WorkflowsDesignSqliteDbContext>(options => options.AddInterceptors(commands));
        if (failure is not null) services.AddDbContext<WorkflowsDesignSqliteDbContext>(options => options.AddInterceptors(failure));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Library_folders_preserve_identity_and_membership_through_restructure_and_reject_cycles_collisions_and_nonempty_delete()
    {
        await using var database = new TemporarySqliteDatabase("workflow-library-folders");
        await using var host = LibraryHost(database.ConnectionString); using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); await db.Database.EnsureCreatedAsync();
        db.Definitions.Add(new WorkflowDefinition { Id = "finance", TenantId = "tenant-a", Name = "Collect payment" }); await db.SaveChangesAsync();
        var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
        var finance = await library.CreateFolderAsync("Finance", null, "alice");
        var invoices = await library.CreateFolderAsync("Invoices", finance.Id, "alice");
        var operations = await library.CreateFolderAsync("Operations", null, "alice");
        await library.MoveDefinitionsAsync(["FINANCE"], invoices.Id, "alice", "place-finance");
        await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() => library.CreateFolderAsync(" finance ", null, "alice"));
        await Assert.ThrowsAsync<ArgumentException>(() => library.MoveFolderAsync(finance.Id, invoices.Id, finance.Revision, "alice"));
        await library.RenameFolderAsync(finance.Id, "Billing", finance.Revision, "alice");
        await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() => library.RenameFolderAsync(finance.Id, "Stale", finance.Revision, "alice"));
        var current = (await library.GetFolderAsync(finance.Id))!.Folder;
        await library.MoveFolderAsync(finance.Id, operations.Id, current.Revision, "alice");
        var organization = (await library.GetOrganizationAsync("finance"))!;
        Assert.Equal(invoices.Id, organization.FolderId);
        Assert.Equal(new[] { "Operations", "Billing", "Invoices" }, organization.FolderBreadcrumb.Select(folder => folder.Name));
        Assert.Equal(1, (await library.ListDefinitionsAsync(new(FolderId: operations.Id, IncludeDescendants: true), null)).TotalCount);
        Assert.Empty((await library.ListDefinitionsAsync(new(FolderId: operations.Id), null)).Items);
        var definition = await db.Definitions.SingleAsync(); definition.DeletedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() => library.DeleteEmptyFolderAsync(invoices.Id, null, "alice"));
        await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() => library.MoveDefinitionsAsync(["finance"], null, "alice", "move-deleted"));
        Assert.Equal(invoices.Id, (await library.GetOrganizationAsync("finance"))!.FolderId);
        definition = await db.Definitions.SingleAsync(); definition.DeletedAt = null; await db.SaveChangesAsync();
        Assert.Equal(invoices.Id, Assert.Single((await library.ListDefinitionsAsync(new(FolderId: invoices.Id), null)).Items).Organization.FolderId);
        Assert.Empty(await db.Versions.ToArrayAsync()); Assert.Empty(await db.Drafts.ToArrayAsync());
    }

    [Fact]
    public async Task Library_marker_controlled_tags_revisions_deprecation_bulk_retry_and_tenant_boundaries_are_durable()
    {
        await using var database = new TemporarySqliteDatabase("workflow-library-tags");
        string definitionRevision; string tagId; string folderId;
        await using (var host = LibraryHost(database.ConnectionString))
        {
            using var scope = host.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); await db.Database.EnsureCreatedAsync();
            db.Definitions.AddRange(new WorkflowDefinition { Id = "one", Name = "One", TenantId = "tenant-a" }, new WorkflowDefinition { Id = "two", Name = "Two", TenantId = "tenant-a" }); await db.SaveChangesAsync();
            var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
            folderId = (await library.CreateFolderAsync("Finance", null, "alice")).Id;
            await library.MoveDefinitionsAsync(["one"], folderId, "alice", "move-one");
            var marker = await library.CreateTagAsync(new("Critical"), "alice"); tagId = marker.Id;
            await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() => library.CreateTagAsync(new(" critical "), "alice"));
            var regions = await library.CreateTagAsync(new("Region", Mode: "controlled", Cardinality: "multiple", Values: [new("", "EU"), new("", "US")]), "alice");
            var initial = (await library.GetOrganizationAsync("one"))!;
            var assigned = await library.ReplaceTagsAsync("one", initial.Revision,
                [new(marker.Id), new(regions.Id, regions.Values[0].Id), new(regions.Id, regions.Values[1].Id)], "alice", "tag-one");
            await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() => library.ReplaceTagsAsync("one", initial.Revision, [], "alice", "stale-tag-one"));
            Assert.Equal(assigned.Revision, (await library.ReplaceTagsAsync("one", initial.Revision,
                [new(marker.Id), new(regions.Id, regions.Values[0].Id), new(regions.Id, regions.Values[1].Id)], "alice", "tag-one")).Revision);
            var filtered = await library.ListDefinitionsAsync(new(TagFilters: [new(regions.Id, "all", regions.Values.Select(value => value.Id).ToArray())]), null);
            Assert.Equal("one", Assert.Single(filtered.Items).Definition.Id);
            var retired = await library.UpdateTagAsync(marker.Id, new(marker.Name, Deprecated: true, Revision: marker.Revision), "alice");
            await Assert.ThrowsAsync<ArgumentException>(() => library.ReplaceTagsAsync("two", "0", [new(marker.Id)], "alice", "retired-new"));
            var retained = await library.ReplaceTagsAsync("one", assigned.Revision, assigned.Tags, "alice", "retain-retired");
            var bulk = new WorkflowTagBulkEdit([new("one", retained.Revision), new("two", "stale"), new("missing", "0")], [], [new(marker.Id)], "bulk-remove-critical");
            var outcomes = await library.EditTagsAsync(bulk, "alice");
            Assert.Equal(new[] { "success", "conflict", "not-found" }, outcomes.Select(outcome => outcome.Status));
            var auditCount = await db.LibraryAudit.CountAsync(row => row.OperationKey == bulk.OperationKey);
            var repeated = await library.EditTagsAsync(bulk, "alice");
            Assert.Equal(outcomes[0].Revision, repeated[0].Revision);
            Assert.Equal(auditCount, await db.LibraryAudit.CountAsync(row => row.OperationKey == bulk.OperationKey));
            definitionRevision = outcomes[0].Revision!;
            var secondary = await library.CreateTagAsync(new("Secondary"), "alice");
            await library.ReplaceTagsAsync("two", "0", [new(secondary.Id)], "alice", "first-two-tags");
            var initialAudit = await db.LibraryAudit.SingleAsync(row => row.TargetId == "two" && row.Operation == "workflow.tags.replace");
            using var beforeAudit = System.Text.Json.JsonDocument.Parse(initialAudit.BeforeJson);
            Assert.Equal("0", beforeAudit.RootElement.GetProperty("revision").GetString());
            await Assert.ThrowsAsync<ArgumentException>(() => library.UpdateTagAsync(retired.Id,
                new(retired.Name, Mode: "controlled", Values: [new("", "Yes")], Revision: retired.Revision), "alice"));
        }
        await using (var restart = LibraryHost(database.ConnectionString))
        {
            using var scope = restart.CreateScope(); var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
            var organization = (await library.GetOrganizationAsync("one"))!;
            Assert.Equal(definitionRevision, organization.Revision); Assert.Equal(folderId, organization.FolderId);
            Assert.DoesNotContain(organization.Tags, tag => tag.TagId == tagId);
        }
        await using (var other = LibraryHost(database.ConnectionString, "tenant-b"))
        {
            using var scope = other.CreateScope(); var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
            Assert.Null(await library.GetOrganizationAsync("one")); Assert.Empty((await library.ListTagsAsync()).Items);
            await Assert.ThrowsAsync<EntityNotFoundException>(() => library.MoveDefinitionsAsync(["one"], folderId, "bob", "cross-tenant"));
        }
    }

    [Fact]
    public async Task Library_views_and_favorites_are_personal_unless_explicitly_shared_and_cursors_bind_subject_and_query()
    {
        await using var database = new TemporarySqliteDatabase("workflow-library-views"); await using var host = LibraryHost(database.ConnectionString);
        using var scope = host.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); await db.Database.EnsureCreatedAsync();
        db.Definitions.AddRange(new WorkflowDefinition { Id = "one", Name = "A", TenantId = "tenant-a" }, new WorkflowDefinition { Id = "two", Name = "B", TenantId = "tenant-a" }); await db.SaveChangesAsync();
        var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
        var personal = await library.SaveViewAsync(null, new("My workflows", new()), "alice", false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => library.SaveViewAsync(null, new("Shared", new(), Shared: true), "alice", false));
        var shared = await library.SaveViewAsync(null, new("Team workflows", new(), Shared: true), "alice", true);
        Assert.Equal(2, (await library.ListViewsAsync("alice")).Count);
        Assert.Equal(shared.Id, Assert.Single(await library.ListViewsAsync("bob")).Id);
        await Assert.ThrowsAsync<EntityNotFoundException>(() => library.DeleteViewAsync(personal.Id, personal.Revision, "bob", true));
        await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() => library.SaveViewAsync(shared.Id, new("Stale", new(), Shared: true, Revision: "stale"), "alice", true));
        await library.SetFavoriteAsync("one", true, "alice"); await library.SetFavoriteAsync("one", true, "alice");
        Assert.Equal("one", Assert.Single((await library.ListDefinitionsAsync(new(FavoritesOnly: true), "alice")).Items).Definition.Id);
        Assert.Empty((await library.ListDefinitionsAsync(new(FavoritesOnly: true), "bob")).Items);
        var page = await library.ListDefinitionsAsync(new(PageSize: 1), "alice"); Assert.NotNull(page.NextContinuationToken);
        await Assert.ThrowsAsync<ArgumentException>(() => library.ListDefinitionsAsync(new(PageSize: 1, ContinuationToken: page.NextContinuationToken), "bob"));
        await Assert.ThrowsAsync<ArgumentException>(() => library.ListDefinitionsAsync(new(SearchTerm: "A", PageSize: 1, ContinuationToken: page.NextContinuationToken), "alice"));
    }

    [Fact]
    public async Task Library_create_in_folder_is_atomic_and_permanent_delete_cascades_organization_without_deleting_folder_or_tag()
    {
        await using var database = new TemporarySqliteDatabase("workflow-library-create"); await using var host = LibraryHost(database.ConnectionString);
        using var scope = host.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); await db.Database.EnsureCreatedAsync();
        var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>(); var create = scope.ServiceProvider.GetRequiredService<IAddWorkflowDefinitionCommand>();
        var folder = await library.CreateFolderAsync("Finance", null, "alice");
        var created = await create.Execute(new("create-in-folder"), new WorkflowDefinition { Id = "one", Name = "One" },
            new WorkflowDefinitionDraft { Id = "draft-one", WorkflowDefinitionId = "one", State = WorkflowDefinitionState.Empty }, [], [], folder.Id, "alice");
        Assert.Equal(folder.Id, (await library.GetOrganizationAsync(created.DefinitionId))!.FolderId);
        await Assert.ThrowsAsync<EntityNotFoundException>(() => create.Execute(new("bad-folder"), new WorkflowDefinition { Id = "two", Name = "Two" },
            new WorkflowDefinitionDraft { Id = "draft-two", WorkflowDefinitionId = "two", State = WorkflowDefinitionState.Empty }, [], [], "folder-" + Guid.NewGuid().ToString("N"), "alice"));
        Assert.Equal(1, await db.Definitions.CountAsync()); Assert.Equal(1, await db.Drafts.CountAsync());
        var tag = await library.CreateTagAsync(new("Critical"), "alice");
        var organization = (await library.GetOrganizationAsync("one"))!;
        await library.ReplaceTagsAsync("one", organization.Revision, [new(tag.Id)], "alice", "tag-before-delete");
        await library.SetFavoriteAsync("one", true, "alice");
        var definition = await db.Definitions.SingleAsync(); db.Definitions.Remove(definition); await db.SaveChangesAsync();
        Assert.Null(await library.GetOrganizationAsync("one"));
        Assert.Empty((await library.ListDefinitionsAsync(new(FavoritesOnly: true), "alice")).Items);
        Assert.Single((await library.ListTagsAsync()).Items); Assert.NotNull(await library.GetFolderAsync(folder.Id));
        await library.DeleteEmptyFolderAsync(folder.Id, folder.Revision, "alice");
    }

    [Fact]
    public async Task Library_ten_thousand_definitions_have_complete_paging_and_bounded_queries_without_loading_version_payloads()
    {
        await using var database = new TemporarySqliteDatabase("workflow-library-scale"); var commands = new LibraryCommands();
        await using var host = LibraryHost(database.ConnectionString, commands: commands); using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 10000; index++) db.Definitions.Add(new WorkflowDefinition { Id = $"workflow-{index:D5}",
            Name = $"Workflow {index:D5}", TenantId = "tenant-a", CreatedAt = now, LastModifiedAt = now.AddTicks(index) });
        for (var index = 1; index <= 200; index++) db.Versions.Add(new WorkflowDefinitionVersion("workflow-00000", $"1.0.{index}", "{\"largePayload\":\"unused\"}") { Id = $"version-{index}", TenantId = "tenant-a" });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); commands.Reset();
        var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
        var first = await library.ListDefinitionsAsync(new(PageSize: 100), null);
        Assert.Equal(10000, first.TotalCount); Assert.Equal(100, first.Items.Count); Assert.NotNull(first.NextContinuationToken);
        Assert.Equal(200, first.Items[0].VersionCount); Assert.Equal("1.0.200", first.Items[0].LatestVersion);
        Assert.InRange(commands.Sql.Count, 1, 10);
        Assert.DoesNotContain(commands.Sql, command => command.Contains("StateSource", StringComparison.Ordinal) || command.Contains("RecordsJson", StringComparison.Ordinal));
        var next = await library.ListDefinitionsAsync(new(PageSize: 100, ContinuationToken: first.NextContinuationToken), null);
        Assert.Equal("workflow-00100", next.Items[0].Definition.Id);
        Assert.Empty(first.Items.Select(item => item.Definition.Id).Intersect(next.Items.Select(item => item.Definition.Id)));
        var descending = await library.ListDefinitionsAsync(new(Sort: "name-desc", PageSize: 2), null);
        Assert.Equal("workflow-09999", descending.Items[0].Definition.Id);
        var modified = await library.ListDefinitionsAsync(new(Sort: "modified", PageSize: 2), null);
        Assert.Equal("workflow-09999", modified.Items[0].Definition.Id);
        var modifiedNext = await library.ListDefinitionsAsync(new(Sort: "modified", PageSize: 2, ContinuationToken: modified.NextContinuationToken), null);
        Assert.Equal("workflow-09997", modifiedNext.Items[0].Definition.Id);
        Assert.Equal(10, (await library.ListDefinitionsAsync(new(SearchTerm: "Workflow 0010"), null)).TotalCount);
    }

    private sealed class LibraryCommands : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public void Reset() => Sql.Clear();
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Sql.Add(command.CommandText); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<object> result, CancellationToken cancellationToken = default)
        { Sql.Add(command.CommandText); return ValueTask.FromResult(result); }
    }

    [Fact]
    public async Task Library_visibility_scope_filters_before_counts_and_paging_and_invalidates_old_policy_cursors()
    {
        await using var database = new TemporarySqliteDatabase("workflow-library-policy-scope"); await using var host = LibraryHost(database.ConnectionString);
        using var scope = host.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); await db.Database.EnsureCreatedAsync();
        db.Definitions.AddRange(new WorkflowDefinition { Id = "one", Name = "A", TenantId = "tenant-a" },
            new WorkflowDefinition { Id = "two", Name = "B", TenantId = "tenant-a" }, new WorkflowDefinition { Id = "three", Name = "C", TenantId = "tenant-a" },
            new WorkflowDefinition { Id = "four", Name = "D", TenantId = "tenant-a" }); await db.SaveChangesAsync();
        var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
        var policy = new WorkflowLibraryReadScope("policy-1", ["ONE", "two", "three"], ["two"]);
        var first = await library.ListDefinitionsAsync(new(PageSize: 1), "alice", policy);
        Assert.Equal(2, first.TotalCount); Assert.Equal("one", Assert.Single(first.Items).Definition.Id); Assert.NotNull(first.NextContinuationToken);
        var next = await library.ListDefinitionsAsync(new(PageSize: 1, ContinuationToken: first.NextContinuationToken), "alice", policy);
        Assert.Equal(2, next.TotalCount); Assert.Equal("three", Assert.Single(next.Items).Definition.Id);
        await Assert.ThrowsAsync<ArgumentException>(() => library.ListDefinitionsAsync(new(PageSize: 1, ContinuationToken: first.NextContinuationToken), "alice", policy with { Revision = "policy-2" }));
        await Assert.ThrowsAsync<ArgumentException>(() => library.ListDefinitionsAsync(new(PageSize: 1, ContinuationToken: first.NextContinuationToken), "alice", policy with { DeniedDefinitionIds = [] }));
        Assert.Equal(0, (await library.ListDefinitionsAsync(new(), "alice", new WorkflowLibraryReadScope("empty", []))).TotalCount);
        Assert.Equal("two", Assert.Single((await library.ListDefinitionsAsync(new(), "alice", new WorkflowLibraryReadScope("denied", DeniedDefinitionIds: ["one", "three", "four"]))).Items).Definition.Id);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => library.ListDefinitionsAsync(new(), "alice", new WorkflowLibraryReadScope("", [])));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => library.ListDefinitionsAsync(new(), "alice", new WorkflowLibraryReadScope("invalid", [null!])));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => library.ListDefinitionsAsync(new(), "alice", new WorkflowLibraryReadScope("overflow", Enumerable.Repeat("one", 10001).ToArray())));
    }

    [Fact]
    public async Task Library_read_surfaces_translate_native_provider_failures_and_preserve_the_cause()
    {
        await using var database = new TemporarySqliteDatabase("workflow-library-read-failures"); var failure = new LibraryReadFailure();
        await using var host = LibraryHost(database.ConnectionString, failure: failure); using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); await db.Database.EnsureCreatedAsync();
        db.Definitions.Add(new WorkflowDefinition { Id = "one", Name = "One", TenantId = "tenant-a" }); await db.SaveChangesAsync();
        var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
        var folder = await library.CreateFolderAsync("Finance", null, "alice"); var tag = await library.CreateTagAsync(new("Critical"), "alice");
        failure.Enabled = true;
        Func<Task>[] reads = [() => library.ListFoldersAsync(null), () => library.GetFolderAsync(folder.Id), () => library.ListTagsAsync(),
            () => library.GetTagAsync(tag.Id), () => library.ListViewsAsync("alice"), () => library.GetOrganizationAsync("one"),
            () => library.ListDefinitionsAsync(new(), "alice"), () => library.GetVersionLabelsAsync(["version"])];
        foreach (var read in reads)
        {
            var exception = await Assert.ThrowsAsync<Elsa.Workflows.Design.Persistence.Core.Exceptions.DesignPersistenceException>(read);
            Assert.Equal(Elsa.Workflows.Design.Persistence.Core.Exceptions.DesignPersistenceFailureKind.Provider, exception.FailureKind);
            Assert.Same(failure.Exception, exception.InnerException);
        }
    }

    private sealed class LibraryReadFailure : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public Microsoft.Data.Sqlite.SqliteException Exception { get; } = new("The native provider is unavailable.", 14);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) => Enabled
            ? ValueTask.FromException<InterceptionResult<DbDataReader>>(Exception) : ValueTask.FromResult(result);
    }

    [Fact]
    public async Task Library_backend_reconfiguration_retains_unrelated_observers_and_rebinds_the_one_library_observer()
    {
        await using var firstDatabase = new TemporarySqliteDatabase("workflow-library-first-provider");
        await using var secondDatabase = new TemporarySqliteDatabase("workflow-library-reconfigured-provider");
        var services = new ServiceCollection();
        services.AddSingleton<IPayloadSerializer, TestSerializer>(); services.AddSingleton<IDistributedLockProvider, TestLockProvider>();
        services.AddSingleton<IPersistenceAccessContextAccessor>(new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        services.AddScoped<IWorkflowTriggerIndexObserver, UnrelatedLibraryObserver>();
        services.AddWorkflowsDesignEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = firstDatabase.ConnectionString });
        string firstFolder;
        await using (var firstHost = services.BuildServiceProvider())
        {
            using var scope = firstHost.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); await db.Database.EnsureCreatedAsync();
            firstFolder = (await scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>().CreateFolderAsync("First database", null, "alice")).Id;
        }
        services.AddWorkflowsDesignEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = secondDatabase.ConnectionString });
        await using var secondHost = services.BuildServiceProvider(); using var secondScope = secondHost.CreateScope();
        var current = secondScope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); await current.Database.EnsureCreatedAsync();
        Assert.Equal(secondDatabase.ConnectionString, current.Database.GetConnectionString());
        var observers = secondScope.ServiceProvider.GetServices<IWorkflowTriggerIndexObserver>().ToArray();
        Assert.Equal(2, observers.Length); Assert.Single(observers.OfType<UnrelatedLibraryObserver>());
        Assert.Single(observers.OfType<WorkflowLibraryPublicationObserver>());
        var library = secondScope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
        Assert.Null(await library.GetFolderAsync(firstFolder));
        Assert.Equal("Second database", (await library.CreateFolderAsync("Second database", null, "alice")).Name);
        Assert.Same(library, secondScope.ServiceProvider.GetRequiredService<Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores.EfWorkflowLibraryStore>());
    }

    private sealed class UnrelatedLibraryObserver : IWorkflowTriggerIndexObserver
    {
        public ValueTask OnTriggersIndexedAsync(WorkflowTriggerIndexSnapshot snapshot, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    [Fact]
    public void Replacing_Ef_with_a_custom_backend_removes_its_observer_and_preserves_other_contributions()
    {
        var services = new ServiceCollection();
        services.AddScoped<IWorkflowTriggerIndexObserver, UnrelatedLibraryObserver>();
        services.AddWorkflowsDesignEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = "Data Source=:memory:" });
        var backend = DesignPersistenceBackend.Find(services)!;
        backend.RemoveOwnedDescriptors(services);
        services.AddSingleton<object>(new object());
        DesignPersistenceBackend.Register(services, new("custom", [services.Single(item => item.ServiceType == typeof(object))]));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var observer = Assert.Single(scope.ServiceProvider.GetServices<IWorkflowTriggerIndexObserver>());
        Assert.IsType<UnrelatedLibraryObserver>(observer);
        Assert.Null(scope.ServiceProvider.GetService<IWorkflowLibraryStore>());
        Assert.Equal("custom", DesignPersistenceBackend.Find(services)!.Name);
    }

    [Fact]
    public async Task Legacy_custom_backend_without_optional_library_starts_and_reads_existing_definitions()
    {
        await using var database = new TemporarySqliteDatabase("workflow-library-legacy-backend");
        var services = new ServiceCollection();
        services.AddSingleton<IPayloadSerializer, TestSerializer>();
        services.AddSingleton<IPersistenceAccessContextAccessor>(new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        services.AddWorkflowsDesignEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = database.ConnectionString });
        var original = DesignPersistenceBackend.Find(services)!;
        // A legacy backend can delegate the original design ports to EF without
        // implementing the optional organization port or its Runtime contribution.
        var delegated = services.Where(original.Owns).Where(descriptor =>
            descriptor.ServiceType != typeof(IWorkflowLibraryStore) &&
            descriptor.ServiceType != typeof(Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores.EfWorkflowLibraryStore) &&
            descriptor.ServiceType != typeof(IWorkflowTriggerIndexObserver)).ToArray();
        original.RemoveOwnedDescriptors(services);
        foreach (var descriptor in delegated) ((IServiceCollection)services).Add(descriptor);
        var custom = new DesignPersistenceBackend("legacy-custom", delegated);
        DesignPersistenceBackend.Register(services, custom);
        custom.EnsureOwnsRegisteredContracts(services);

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var startup = Assert.Single(scope.ServiceProvider.GetServices<Elsa.Tasks.Core.IStartupTask>(), task =>
            task is Elsa.Workflows.Design.Persistence.Core.Services.ValidateDesignPersistenceReplacementContractsStartupTask);
        await startup.ExecuteAsync(CancellationToken.None);
        Assert.Null(scope.ServiceProvider.GetService<IWorkflowLibraryStore>());
        Assert.Empty(scope.ServiceProvider.GetServices<IWorkflowTriggerIndexObserver>());
        var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Definitions.Add(new WorkflowDefinition { Id = "legacy-definition", TenantId = "tenant-a", Name = "Existing workflow" });
        await db.SaveChangesAsync();
        var definitions = await scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>()
            .ListAsync(new Elsa.Workflows.Design.Persistence.Core.Filters.WorkflowDefinitionFilter());
        Assert.Equal("legacy-definition", Assert.Single(definitions).Id);
    }

    [Fact]
    public async Task Optional_library_port_still_rejects_multiple_owners()
    {
        var services = new ServiceCollection();
        services.AddWorkflowsDesignEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = "Data Source=:memory:" });
        services.AddScoped<IWorkflowLibraryStore>(_ => throw new NotSupportedException());
        var startup = new Elsa.Workflows.Design.Persistence.Core.Services.ValidateDesignPersistenceReplacementContractsStartupTask(services);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => startup.ExecuteAsync(CancellationToken.None));
        Assert.Contains($"{nameof(IWorkflowLibraryStore)} (2 registrations)", error.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => DesignPersistenceBackend.Find(services)!.EnsureOwnsRegisteredContracts(services));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Library_concurrent_opposing_folder_moves_cannot_commit_a_cycle_even_without_a_serializing_lock(bool includeLockProvider)
    {
        await using var database = new TemporarySqliteDatabase("workflow-library-concurrent-hierarchy");
        string firstId; string secondId;
        await using (var host = LibraryHost(database.ConnectionString))
        {
            using var scope = host.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); await db.Database.EnsureCreatedAsync();
            var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
            firstId = (await library.CreateFolderAsync("First", null, "alice")).Id;
            secondId = (await library.CreateFolderAsync("Second", null, "alice")).Id;
        }
        using var barrier = new Barrier(2);
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        await using var firstDb = new WorkflowsDesignSqliteDbContext(new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>().UseSqlite(database.ConnectionString).Options);
        await using var secondDb = new WorkflowsDesignSqliteDbContext(new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>().UseSqlite(database.ConnectionString).Options);
        var first = new Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores.EfWorkflowLibraryStore(firstDb, access,
            new PreTransactionBarrierAtomicWriter(new Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores.EfDesignAtomicWriter(firstDb, access), barrier), includeLockProvider ? new TestLockProvider() : null);
        var second = new Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores.EfWorkflowLibraryStore(secondDb, access,
            new PreTransactionBarrierAtomicWriter(new Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores.EfDesignAtomicWriter(secondDb, access), barrier), includeLockProvider ? new TestLockProvider() : null);
        var outcomes = await Task.WhenAll(Task.Run(() => Record.ExceptionAsync(() => first.MoveFolderAsync(firstId, secondId, null, "alice"))),
            Task.Run(() => Record.ExceptionAsync(() => second.MoveFolderAsync(secondId, firstId, null, "bob"))));
        Assert.Single(outcomes, exception => exception is null);
        Assert.IsAssignableFrom<ArgumentException>(Assert.Single(outcomes, exception => exception is not null));
        await using var verifyHost = LibraryHost(database.ConnectionString); using var verifyScope = verifyHost.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
        var firstPath = (await verify.GetFolderAsync(firstId))!; var secondPath = (await verify.GetFolderAsync(secondId))!;
        Assert.True(firstPath.Ancestors.Count == 0 || secondPath.Ancestors.Count == 0);
        Assert.InRange(firstPath.Ancestors.Count, 0, 1); Assert.InRange(secondPath.Ancestors.Count, 0, 1);
    }

    [Fact]
    public async Task Library_rejects_null_bulk_revisions_filter_elements_and_malformed_cursors_without_provider_work()
    {
        await using var database = new TemporarySqliteDatabase("workflow-library-validation"); var commands = new LibraryCommands();
        await using var host = LibraryHost(database.ConnectionString, commands: commands); using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); await db.Database.EnsureCreatedAsync();
        db.Definitions.AddRange(new WorkflowDefinition { Id = "one", Name = "A", TenantId = "tenant-a" }, new WorkflowDefinition { Id = "two", Name = "B", TenantId = "tenant-a" }); await db.SaveChangesAsync();
        var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
        var page = await library.ListDefinitionsAsync(new(PageSize: 1), "alice");
        commands.Reset();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => library.EditTagsAsync(new([new("one", null!)], [], [], "missing-revision"), "alice"));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => library.EditTagsAsync(new([null!], [], [], "missing-target"), "alice"));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => library.ListDefinitionsAsync(new(TagFilters: [null!]), "alice"));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => library.ListDefinitionsAsync(new(TagFilters: [new("tag-" + Guid.NewGuid().ToString("N"), "any", [null!])]), "alice"));
        var raw = page.NextContinuationToken!.Replace('-', '+').Replace('_', '/'); raw = raw.PadRight((raw.Length + 3) / 4 * 4, '=');
        var cursor = System.Text.Json.Nodes.JsonNode.Parse(Convert.FromBase64String(raw))!;
        cursor["key"] = null;
        var malformed = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(cursor.ToJsonString())).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await Assert.ThrowsAnyAsync<ArgumentException>(() => library.ListDefinitionsAsync(new(PageSize: 1, ContinuationToken: malformed), "alice"));
        Assert.Empty(commands.Sql);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => library.CreateFolderAsync(new string('\uFDFA', 256), null, "alice"));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => library.CreateTagAsync(new(new string('\uFDFA', 256)), "alice"));
        Assert.Empty((await library.ListFoldersAsync(null)).Items); Assert.Empty((await library.ListTagsAsync()).Items);
    }

    [Fact]
    public async Task Library_requires_one_catalog_scope_even_for_privileged_inventory_readers()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("organization-inventory")));
        var library = new Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores.EfWorkflowLibraryStore(db, access,
            new Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores.EfDesignAtomicWriter(db, access), new TestLockProvider());
        await Assert.ThrowsAsync<InvalidOperationException>(() => library.ListDefinitionsAsync(new(), null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => library.ListFoldersAsync(null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => library.ListTagsAsync());
    }

    [Fact]
    public async Task Library_draft_projection_uses_modified_then_created_then_exact_ordinal_identity()
    {
        await using var database = new TemporarySqliteDatabase("workflow-library-draft-order"); await using var host = LibraryHost(database.ConnectionString);
        using var scope = host.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        db.Definitions.Add(new WorkflowDefinition { Id = "one", Name = "One", TenantId = "tenant-a" });
        db.Drafts.AddRange(new WorkflowDefinitionDraft { Id = "z", TenantId = "tenant-a", WorkflowDefinitionId = "one", StateSource = "{}", LastModifiedAt = now, CreatedAt = now.AddHours(-1) },
            new WorkflowDefinitionDraft { Id = "a", TenantId = "tenant-a", WorkflowDefinitionId = "one", StateSource = "{}", LastModifiedAt = now, CreatedAt = now });
        await db.SaveChangesAsync(); var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
        Assert.Equal("a", Assert.Single((await library.ListDefinitionsAsync(new(), null)).Items).DraftId);
        db.Drafts.AddRange(new WorkflowDefinitionDraft { Id = "z ", TenantId = "tenant-a", WorkflowDefinitionId = "one", StateSource = "{}", LastModifiedAt = now, CreatedAt = now },
            new WorkflowDefinitionDraft { Id = "Z", TenantId = "tenant-a", WorkflowDefinitionId = "one", StateSource = "{}", LastModifiedAt = now, CreatedAt = now });
        await db.SaveChangesAsync();
        Assert.Equal("z ", Assert.Single((await library.ListDefinitionsAsync(new(), null)).Items).DraftId);
    }

    [Fact]
    public async Task Library_triggerless_publication_refresh_reads_current_authority_under_the_fence_and_preserves_manual_organization()
    {
        await using var database = new TemporarySqliteDatabase("workflow-library-publication");
        var locking = new LibraryFenceLock(); var authority = new LibraryAuthority(() => locking.Held);
        await using var host = LibraryHost(database.ConnectionString, authority: authority, locking: locking);
        using var scope = host.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); await db.Database.EnsureCreatedAsync();
        db.Definitions.Add(new WorkflowDefinition { Id = "finance", Name = "Collect payment", TenantId = "tenant-a", LastModifiedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync();
        var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
        var folder = await library.CreateFolderAsync("Finance", null, "alice"); await library.MoveDefinitionsAsync(["finance"], folder.Id, "alice", "place-finance");
        var tag = await library.CreateTagAsync(new("Critical"), "alice"); var organization = (await library.GetOrganizationAsync("finance"))!;
        organization = await library.ReplaceTagsAsync("finance", organization.Revision, [new(tag.Id)], "alice", "tag-finance");
        var modified = (await db.Definitions.SingleAsync()).LastModifiedAt;
        var observer = Assert.Single(scope.ServiceProvider.GetServices<IWorkflowTriggerIndexObserver>());
        await observer.OnTriggersIndexedAsync(new("artifact", []) { DefinitionId = "finance" });
        Assert.Equal(0, authority.Reads);
        await observer.OnTriggersIndexedAsync(new("artifact", []) { RequiresProjectionRefresh = true });
        Assert.Equal(0, authority.Reads);
        authority.Published = true;
        var notification = new WorkflowTriggerIndexSnapshot("triggerless-artifact", []) { DefinitionId = "finance", RequiresProjectionRefresh = true };
        await observer.OnTriggersIndexedAsync(notification);
        var query = new WorkflowLibraryQuery(FolderId: folder.Id, IncludeDescendants: true, TagFilters: [new(tag.Id)], PublicationState: "published");
        Assert.Equal("finance", Assert.Single((await library.ListDefinitionsAsync(query, null)).Items).Definition.Id);
        var after = (await library.GetOrganizationAsync("finance"))!;
        Assert.Equal(organization.Revision, after.Revision); Assert.Equal(organization.Tags, after.Tags);
        Assert.Equal(modified, (await db.Definitions.SingleAsync()).LastModifiedAt);
        authority.Published = false;
        await observer.OnTriggersIndexedAsync(notification);
        // Even a delayed notification for the old publication re-reads the current serving authority.
        await observer.OnTriggersIndexedAsync(notification);
        Assert.Equal(0, (await library.ListDefinitionsAsync(query, null)).TotalCount);
        Assert.Equal(3, authority.Reads); Assert.False(locking.Held);
        Assert.Equal(organization.Revision, (await library.GetOrganizationAsync("finance"))!.Revision);
    }

    private sealed class LibraryFenceLock : IDistributedLockProvider
    {
        public bool Held { get; private set; }
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        { Assert.False(Held); Held = true; return new Handle(this); }
        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TryAcquireLock(name, timeout, cancellationToken));
        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TryAcquireLock(name, timeout, cancellationToken)!);
        private sealed class Handle(LibraryFenceLock owner) : IDistributedSynchronizationHandle
        {
            private bool disposed;
            public CancellationToken HandleLostToken => CancellationToken.None;
            public void Dispose() { if (!disposed) { disposed = true; owner.Held = false; } }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    [Fact]
    public async Task Library_first_publication_preserves_the_virtual_initial_manual_revision()
    {
        await using var database = new TemporarySqliteDatabase("workflow-library-initial-publication-revision");
        var locking = new LibraryFenceLock(); var authority = new LibraryAuthority(() => locking.Held) { Published = true };
        await using var host = LibraryHost(database.ConnectionString, authority: authority, locking: locking);
        using var scope = host.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); await db.Database.EnsureCreatedAsync();
        db.Definitions.Add(new WorkflowDefinition { Id = "unorganized", Name = "Unorganized", TenantId = "tenant-a" }); await db.SaveChangesAsync();
        var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
        var initial = (await library.GetOrganizationAsync("unorganized"))!; Assert.Equal("0", initial.Revision);
        var observer = Assert.Single(scope.ServiceProvider.GetServices<IWorkflowTriggerIndexObserver>());
        await observer.OnTriggersIndexedAsync(new("triggerless-artifact", []) { DefinitionId = "unorganized", RequiresProjectionRefresh = true });
        var published = (await library.GetOrganizationAsync("unorganized"))!;
        Assert.Equal(initial.Revision, published.Revision); Assert.Null(published.FolderId); Assert.Empty(published.Tags);
        var manual = await library.ReplaceTagsAsync("unorganized", initial.Revision, [], "alice", "initial-manual-edit");
        Assert.NotEqual("0", manual.Revision);
    }

    [Fact]
    public async Task Library_publication_and_create_in_folder_work_without_a_distributed_lock_provider()
    {
        await using var database = new TemporarySqliteDatabase("workflow-library-minimal-publication");
        var authority = new LibraryAuthority(() => true) { Published = true };
        await using var host = LibraryHost(database.ConnectionString, authority: authority, includeLockProvider: false);
        using var scope = host.CreateScope();
        Assert.Null(scope.ServiceProvider.GetService<IDistributedLockProvider>());
        var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Definitions.Add(new WorkflowDefinition { Id = "minimal", Name = "Minimal host", TenantId = "tenant-a" });
        await db.SaveChangesAsync();
        var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
        var observer = Assert.Single(scope.ServiceProvider.GetServices<IWorkflowTriggerIndexObserver>());
        await observer.OnTriggersIndexedAsync(new("triggerless", []) { DefinitionId = "minimal", RequiresProjectionRefresh = true });
        Assert.Equal("minimal", Assert.Single((await library.ListDefinitionsAsync(new(PublicationState: "published"), null)).Items).Definition.Id);
        var organization = (await library.GetOrganizationAsync("minimal"))!;
        Assert.Equal("0", organization.Revision);

        var folder = await library.CreateFolderAsync("Finance", null, "alice");
        var definition = new WorkflowDefinition { Id = "created-in-folder", Name = "Created in folder" };
        var draft = new WorkflowDefinitionDraft { Id = "created-draft", WorkflowDefinitionId = definition.Id };
        await scope.ServiceProvider.GetRequiredService<IAddWorkflowDefinitionCommand>().Execute(new("minimal-create"),
            definition, draft, [], [], folder.Id, "alice");
        Assert.Equal(folder.Id, (await library.GetOrganizationAsync(definition.Id))!.FolderId);

        authority.Published = false;
        await observer.OnTriggersIndexedAsync(new("triggerless", []) { DefinitionId = "minimal", RequiresProjectionRefresh = true });
        Assert.Empty((await library.ListDefinitionsAsync(new(PublicationState: "published"), null)).Items);
        Assert.Equal(organization.Revision, (await library.GetOrganizationAsync("minimal"))!.Revision);
    }

    private sealed class LibraryAuthority(Func<bool> holdingFence) : IWorkflowActivationAuthority
    {
        public bool Published { get; set; }
        public int Reads { get; private set; }
        public ValueTask<IReadOnlyCollection<WorkflowActivationSlot>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default)
        {
            Assert.True(holdingFence()); Reads++;
            return ValueTask.FromResult<IReadOnlyCollection<WorkflowActivationSlot>>([new("slot", definitionId, "default", Published ? "activation" : null,
                WorkflowActivationSource.Publishing, Reads, DateTimeOffset.UtcNow)]);
        }
        public ValueTask<WorkflowActivationSlot?> FindAsync(string definitionId, string slotName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowActivationTransition> TryDeactivateAsync(string definitionId, string slotName, WorkflowActivationSource source, long expectedRevision,
            DateTimeOffset updatedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
