using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfActivityExecutionStoresTests
{
    [Fact]
    public async Task State_store_round_trips_scoped_rows_and_provider_count_with_bounded_pages()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.State.SaveAsync(State("wf", "b", 2));
        await fixture.State.SaveAsync(State("wf", "a", 1));
        await fixture.State.SaveAsync(State("wf", "c", 3, "parent"));
        await fixture.State.SaveAsync(State("other", "a", 1));

        Assert.Equal(3, await fixture.State.CountAsync("wf"));
        var first = await fixture.State.ListPageAsync(new ActivityExecutionStatePageQuery("wf", 1));
        Assert.Equal("a", first.Items[0].Execution.ActivityExecutionId);
        Assert.NotNull(first.NextContinuationToken);
        var second = await fixture.State.ListPageAsync(new ActivityExecutionStatePageQuery("wf", 1, first.NextContinuationToken));
        Assert.Equal("b", second.Items[0].Execution.ActivityExecutionId);
        var third = await fixture.State.ListPageAsync(new ActivityExecutionStatePageQuery("wf", 1, second.NextContinuationToken));
        Assert.Equal("c", third.Items[0].Execution.ActivityExecutionId);
        Assert.Null(third.NextContinuationToken);
        Assert.Single((await fixture.State.ListByParentPageAsync(new ActivityExecutionStateParentPageQuery("wf", "parent"))).Items);

        await using var otherScope = await fixture.ReopenAsync("tenant-b");
        Assert.Null(await otherScope.State.FindAsync("wf", "a"));
    }

    [Fact]
    public async Task Inspection_store_round_trips_projection_and_summary_cursor()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Inspection.SaveAsync(Projection("wf", "b", 2, "b"));
        await fixture.Inspection.SaveAsync(Projection("wf", "a", 1, "a"));

        var first = await fixture.Inspection.ListSummariesPageAsync(new ActivityExecutionInspectionSummaryPageQuery("wf", 1));
        Assert.Equal(2, first.TotalCount);
        Assert.Equal("a", first.Items[0].ActivityExecutionId);
        Assert.NotNull(first.NextContinuationToken);
        var second = await fixture.Inspection.ListSummariesPageAsync(new ActivityExecutionInspectionSummaryPageQuery("wf", 1, first.NextContinuationToken));
        Assert.Equal("b", second.Items[0].ActivityExecutionId);
        Assert.NotNull(await fixture.Inspection.FindAsync("wf", "a"));
    }

    [Fact]
    public async Task Activity_execution_rows_reject_stale_revision_writes()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.State.SaveAsync(State("wf", "activity", 1));
        var stale = await fixture.Context.ActivityExecutionStates.AsNoTracking().SingleAsync();

        await fixture.State.SaveAsync(State("wf", "activity", 2));
        fixture.Context.ChangeTracker.Clear();
        fixture.Context.ActivityExecutionStates.Update(stale);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => fixture.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task Hierarchy_store_keeps_watermark_cursor_and_boundary_scope_safe()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf", "root", 1, "root", null, true));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf", "child-a", 2, "root", "root"));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf", "child-b", 3, "root", "root"));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf", "other-root", 4, "other-root", null, true));

        var first = await fixture.Hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
            "wf", "root", null, 1, new HashSet<ActivityExecutionHierarchyInclude>(), "profile", "tenant:tenant-a"));
        Assert.NotNull(first);
        Assert.Equal(4, first!.CommittedThroughSequence);
        Assert.Single(first.Items);
        Assert.Equal("child-a", first.Items[0].ActivityExecutionId);
        Assert.NotNull(first.NextCursor);
        var second = await fixture.Hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
            "wf", "root", first.NextCursor, 1, new HashSet<ActivityExecutionHierarchyInclude>(), "profile", "tenant:tenant-a"));
        Assert.Equal("child-b", second!.Items[0].ActivityExecutionId);
        Assert.Null(second.NextCursor);

        var boundary = await fixture.Hierarchy.FindBoundaryAsync("wf", "root");
        Assert.NotNull(boundary);
        Assert.Equal(2, boundary!.CommittedDescendantCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
            "wf", "root", null, 1, new HashSet<ActivityExecutionHierarchyInclude>(), "profile", "tenant:tenant-b")).AsTask());
    }

    [Fact]
    public void Activity_execution_registration_replaces_only_runtime_defaults_and_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddRuntimeActivityExecutionEntityFrameworkCore(new RuntimeActivityExecutionEntityFrameworkCoreOptions
        {
            Provider = "sqlite",
            ConnectionString = "Data Source=:memory:",
            RecoveryContinuationSigningKey = "ef-r07-r09-registration-signing-key-32-bytes"
        });
        services.AddRuntimeActivityExecutionEntityFrameworkCore(new RuntimeActivityExecutionEntityFrameworkCoreOptions
        {
            Provider = "SQLite",
            ConnectionString = "Data Source=:memory:",
            RecoveryContinuationSigningKey = "ef-r07-r09-registration-signing-key-32-bytes"
        });

        Assert.Equal(RuntimeActivityExecutionStoreBackend.EntityFramework, RuntimeActivityExecutionStoreBackend.Find(services)!.Name);
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        Assert.IsType<EfActivityExecutionStateStore>(scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>());
        Assert.IsType<EfActivityExecutionInspectionStore>(scope.ServiceProvider.GetRequiredService<IActivityExecutionInspectionStore>());
        Assert.IsType<EfActivityExecutionHierarchyStore>(scope.ServiceProvider.GetRequiredService<IActivityExecutionHierarchyStore>());
    }

    [Fact]
    public void Activity_execution_context_can_be_shared_with_bookmarks_and_artifacts_when_options_match()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        var options = new RuntimeActivityExecutionEntityFrameworkCoreOptions { Provider = "Sqlite", ConnectionString = "Data Source=shared-runtime.db" };
        services.AddRuntimeActivityExecutionEntityFrameworkCore(options);
        services.AddRuntimeBookmarksEntityFrameworkCore(new RuntimeBookmarksEntityFrameworkCoreOptions { Provider = "sqlite", ConnectionString = options.ConnectionString });
        services.AddRuntimeArtifactsEntityFrameworkCore(new RuntimeArtifactsEntityFrameworkCoreOptions { Provider = "SQLite", ConnectionString = options.ConnectionString });

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(BookmarkStateSqliteDbContext));
        Assert.Equal(RuntimeActivityExecutionStoreBackend.EntityFramework, RuntimeActivityExecutionStoreBackend.Find(services)!.Name);
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        Assert.IsType<EfBookmarkStateStore>(scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>());
        Assert.IsType<EfWorkflowExecutableStore>(scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>());
    }

    private static ActivityExecutionState State(string workflow, string id, long sequence, string? parent = null, string? scope = null) =>
        new(
            new ActivityExecution(id, workflow, $"node-{id}", $"authored-{id}", "Test.Activity", "1"),
            ActivityExecutionStatus.Completed,
            null,
            sequence,
            DateTimeOffset.UnixEpoch.AddSeconds(sequence),
            DateTimeOffset.UnixEpoch.AddSeconds(sequence),
            DateTimeOffset.UnixEpoch.AddSeconds(sequence),
            parent,
            parent,
            null,
            null,
            ActivitySchedulingProvenance.From(workflow, parent, parent, null, null, null, scope, "test"),
            null,
            [],
            [],
            0,
            0,
            new Dictionary<string, string>(),
            ExecutionScopeId: scope);

    private static ActivityExecutionInspectionProjection Projection(string workflow, string id, long sequence, string scope, string? parent = null, bool boundary = false) =>
        new(
            id,
            workflow,
            $"node-{id}",
            $"authored-{id}",
            "Test.Activity",
            "1",
            ActivityExecutionStatus.Completed,
            null,
            sequence,
            DateTimeOffset.UnixEpoch.AddSeconds(sequence),
            DateTimeOffset.UnixEpoch.AddSeconds(sequence),
            DateTimeOffset.UnixEpoch.AddSeconds(sequence),
            "checkpoint-1",
            "checkpoint-1",
            DateTimeOffset.UnixEpoch.AddSeconds(sequence),
            ActivitySchedulingProvenance.From(workflow, parent, parent, null, null, null, scope, "test"),
            ["Done"],
            [],
            [],
            [],
            boundary ? new Dictionary<string, string>
            {
                ["activity.definitionId"] = "definition",
                ["activity.definitionVersionId"] = "version",
                ["activity.version"] = "1",
                ["activity.templateHash"] = "template"
            } : new Dictionary<string, string>(),
            scope);

    private static ActivityExecutionHierarchyRecord HierarchyProjection(string workflow, string id, long sequence, string scope, string? parent, bool boundary = false) =>
        ActivityExecutionHierarchyProjector.FromInspection(Projection(workflow, id, sequence, scope, parent, boundary));

    private sealed class Accessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly BookmarkStateDbContext context;
        private readonly Accessor accessor;
        public EfActivityExecutionStateStore State { get; }
        public EfActivityExecutionInspectionStore Inspection { get; }
        public EfActivityExecutionHierarchyStore Hierarchy { get; }
        public BookmarkStateDbContext Context => context;

        private Fixture(SqliteConnection connection, BookmarkStateDbContext context, Accessor accessor)
        {
            this.connection = connection;
            this.context = context;
            this.accessor = accessor;
            State = new EfActivityExecutionStateStore(context, accessor, new HmacRuntimeRecoveryContinuationCodec(Microsoft.Extensions.Options.Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "ef-r07-r09-test-signing-key-32-bytes" })));
            Inspection = new EfActivityExecutionInspectionStore(context, accessor, new HmacRuntimeRecoveryContinuationCodec(Microsoft.Extensions.Options.Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "ef-r07-r09-test-signing-key-32-bytes" })));
            Hierarchy = new EfActivityExecutionHierarchyStore(context, accessor, new Elsa.Workflows.Runtime.Core.Services.HmacActivityExecutionHierarchyCursorCodec(Microsoft.Extensions.Options.Options.Create(new Elsa.Workflows.Runtime.Core.Services.ActivityExecutionHierarchyCursorOptions { SigningKey = "ef-r07-r09-test-hierarchy-signing-key-32-bytes" })));
        }

        public static async Task<Fixture> CreateAsync(string scope)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new Fixture(connection, context, new Accessor(scope));
        }

        public async Task<Fixture> ReopenAsync(string scope)
        {
            await context.DisposeAsync();
            var reopened = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
            return new Fixture(connection, reopened, new Accessor(scope));
        }

        public async ValueTask DisposeAsync()
        {
            await context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
