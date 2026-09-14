using System.Text.Json;
using System.Text.Json.Nodes;
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
    public async Task Hierarchy_store_continues_equal_sequence_boundaries_and_attempts_by_activity_id()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-equal", "root", 0, "root", null, true));

        const int attemptCount = 501;
        for (var attemptNumber = 1; attemptNumber <= attemptCount; attemptNumber++)
        {
            var activityExecutionId = $"attempt-{attemptNumber:D4}";
            var projection = Projection("wf-equal", activityExecutionId, 1, "root", "root") with
            {
                Attempt = new(
                    attemptNumber,
                    "attempt-0001",
                    attemptNumber == 1 ? null : $"attempt-{attemptNumber - 1:D4}")
            };
            await fixture.Hierarchy.SaveAsync(ActivityExecutionHierarchyProjector.FromInspection(projection));
        }

        var boundary = await fixture.Hierarchy.FindBoundaryAsync("wf-equal", "root");
        Assert.NotNull(boundary);
        Assert.Equal(attemptCount, boundary!.CommittedDescendantCount);

        var navigation = await fixture.Hierarchy.FindAttemptNavigationAsync("wf-equal", "attempt-0250");
        Assert.NotNull(navigation);
        Assert.Equal("attempt-0251", navigation!.NextAttemptActivityExecutionId);
        Assert.Equal(attemptCount, navigation.TotalAttempts);
    }

    [Fact]
    public async Task Hierarchy_store_rejects_malformed_persisted_item_json_and_projection_drift()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-malformed", "root", 1, "root", null, true));

        var row = await fixture.Context.ActivityExecutionHierarchies.SingleAsync();
        var payload = JsonNode.Parse(row.ContentJson)!.AsObject();
        payload["item"]!.AsObject().Remove("executableNodeId");
        row.ContentJson = payload.ToJsonString();
        fixture.Context.ChangeTracker.Clear();
        fixture.Context.ActivityExecutionHierarchies.Update(row);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Hierarchy.FindBoundaryAsync("wf-malformed", "root").AsTask());

        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-drift", "root", 1, "root", null, true));
        var drifted = await fixture.Context.ActivityExecutionHierarchies.SingleAsync(row => row.WorkflowExecutionIdHash == Elsa.Persistence.EntityFramework.EfRelationalIdentity.Hash("wf-drift"));
        drifted.ActivityExecutionIdOrderKey = "drifted";
        fixture.Context.ChangeTracker.Clear();
        fixture.Context.ActivityExecutionHierarchies.Update(drifted);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Hierarchy.FindBoundaryAsync("wf-drift", "root").AsTask());
    }

    [Theory]
    [InlineData("outcomeNames")]
    [InlineData("metadata")]
    public async Task Hierarchy_store_rejects_null_required_collections_in_persisted_item_json(string propertyName)
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-malformed-collections", "root", 1, "root", null, true));

        var row = await fixture.Context.ActivityExecutionHierarchies.SingleAsync();
        var payload = JsonNode.Parse(row.ContentJson)!.AsObject();
        payload["item"]!.AsObject()[propertyName] = null;
        row.ContentJson = payload.ToJsonString();
        fixture.Context.ChangeTracker.Clear();
        fixture.Context.ActivityExecutionHierarchies.Update(row);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Hierarchy.FindBoundaryAsync("wf-malformed-collections", "root").AsTask());
    }

    [Fact]
    public async Task Hierarchy_store_rejects_parent_cycles_and_anomalous_watermark_cursors()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-cycle", "root", 1, "root", null, true));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-cycle", "child-a", 2, "root", "child-b"));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-cycle", "child-b", 3, "root", "child-a"));

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
            "wf-cycle", "root", null, 2, new HashSet<ActivityExecutionHierarchyInclude>(), "profile", "tenant:tenant-a")).AsTask());

        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-watermark", "root", 1, "root", null, true));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-watermark", "child-a", 2, "root", "root"));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-watermark", "child-b", 3, "root", "root"));
        var first = await fixture.Hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
            "wf-watermark", "root", null, 1, new HashSet<ActivityExecutionHierarchyInclude>(), "profile", "tenant:tenant-a"));
        var cursor = fixture.HierarchyCursorCodec.Decode(first!.NextCursor!);
        var anomalous = fixture.HierarchyCursorCodec.Encode(cursor with { CommittedThroughSequence = cursor.CommittedThroughSequence + 1 });
        var exception = await Assert.ThrowsAsync<ActivityExecutionHierarchyCursorException>(() => fixture.Hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
            "wf-watermark", "root", anomalous, 1, new HashSet<ActivityExecutionHierarchyInclude>(), "profile", "tenant:tenant-a")).AsTask());
        Assert.Equal(ActivityExecutionHierarchyCursorFailure.Expired, exception.Failure);
    }

    [Fact]
    public async Task Hierarchy_store_rejects_missing_parent_during_depth_resolution()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-missing-parent", "root", 1, "root", null, true));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-missing-parent", "child", 2, "root", "missing-parent"));

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
            "wf-missing-parent", "root", null, 1, new HashSet<ActivityExecutionHierarchyInclude>(), "profile", "tenant:tenant-a")).AsTask());
    }

    [Fact]
    public async Task Hierarchy_store_rejects_parent_from_a_different_execution_scope_during_depth_resolution()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-foreign-parent", "root", 1, "root", null, true));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-foreign-parent", "foreign-parent", 2, "foreign-root", null));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-foreign-parent", "child", 3, "root", "foreign-parent"));

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
            "wf-foreign-parent", "root", null, 1, new HashSet<ActivityExecutionHierarchyInclude>(), "profile", "tenant:tenant-a")).AsTask());
    }

    [Fact]
    public async Task Hierarchy_store_rejects_ancestor_chain_that_does_not_reach_requested_root()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-orphan-chain", "root", 1, "root", null, true));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-orphan-chain", "orphan", 2, "root", null));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-orphan-chain", "child", 3, "root", "orphan"));

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
            "wf-orphan-chain", "root", null, 1, new HashSet<ActivityExecutionHierarchyInclude>(), "profile", "tenant:tenant-a")).AsTask());
    }

    [Fact]
    public async Task Hierarchy_store_rejects_cursor_binding_and_provider_continuation_mismatch()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-cursor", "root", 1, "root", null, true));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-cursor", "child-a", 2, "root", "root"));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-cursor", "child-b", 3, "root", "root"));

        var first = await fixture.Hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
            "wf-cursor", "root", null, 1, new HashSet<ActivityExecutionHierarchyInclude>(), "profile", "tenant:tenant-a"));
        var bound = fixture.HierarchyCursorCodec.Decode(first!.NextCursor!);
        var bindingException = await Assert.ThrowsAsync<ActivityExecutionHierarchyCursorException>(() => fixture.Hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
            "wf-cursor", "root", fixture.HierarchyCursorCodec.Encode(bound with { AuthorizationProfile = "other-profile" }), 1,
            new HashSet<ActivityExecutionHierarchyInclude>(), "profile", "tenant:tenant-a")).AsTask());
        Assert.Equal(ActivityExecutionHierarchyCursorFailure.BindingMismatch, bindingException.Failure);

        var providerException = await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
            "wf-cursor", "root", fixture.HierarchyCursorCodec.Encode(bound with { ProviderContinuation = "provider-token" }), 1,
            new HashSet<ActivityExecutionHierarchyInclude>(), "profile", "tenant:tenant-a")).AsTask());
        Assert.Contains("provider continuation", providerException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Direct_state_and_inspection_writes_reject_provenance_scope_mismatch()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        var state = State("wf-provenance", "activity", 1, scope: "scope-a") with
        {
            Provenance = State("wf-provenance", "activity", 1, scope: "scope-b").Provenance
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.State.SaveAsync(state).AsTask());

        var projection = Projection("wf-provenance", "activity", 1, "scope-a") with
        {
            Provenance = Projection("wf-provenance", "activity", 1, "scope-b").Provenance
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Inspection.SaveAsync(projection).AsTask());

        var stateAttempt = State("wf-provenance", "attempted", 1, scope: "scope-a") with
        {
            Attempt = new(2, "attempt-1", "attempt-0"),
            Provenance = State("wf-provenance", "attempted", 1, scope: "scope-a").Provenance with
            {
                Attempt = new(1, "attempt-1", null)
            }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.State.SaveAsync(stateAttempt).AsTask());

        var projectionAttempt = Projection("wf-provenance", "inspected", 1, "scope-a") with
        {
            Attempt = new(2, "attempt-1", "attempt-0"),
            Provenance = Projection("wf-provenance", "inspected", 1, "scope-a").Provenance with
            {
                Attempt = new(1, "attempt-1", null)
            }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Inspection.SaveAsync(projectionAttempt).AsTask());
    }

    [Fact]
    public async Task Inspection_store_preserves_merge_and_retraction_semantics()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        var bookmark = new ActivityExecutionBookmarkSummary(
            new("bookmark", "wf-merge", "activity", "resume"), "stimulus", "hash", DateTimeOffset.UnixEpoch, null);
        var initial = Projection("wf-merge", "activity", 1, "activity") with
        {
            Bookmarks = [bookmark],
            Metadata = new Dictionary<string, string> { ["initial"] = "yes" }
        };
        await fixture.Inspection.SaveAsync(initial);

        var superseded = State("wf-merge", "activity", 2, scope: "activity") with
        {
            Status = ActivityExecutionStatus.Superseded,
            SupersededByActivityExecutionId = "successor",
            SupersededAt = DateTimeOffset.UnixEpoch.AddSeconds(3),
            Metadata = new Dictionary<string, string> { ["updated"] = "yes" }
        };
        var accumulator = new RuntimeActivityExecutionInspectionAccumulator(fixture.Inspection);
        var merged = await accumulator.BuildProjectionAsync(
            superseded,
            "checkpoint-2",
            DateTimeOffset.UnixEpoch.AddSeconds(4),
            outcomeNames: ["Retried"],
            metadata: new Dictionary<string, string> { ["merge"] = "yes" });
        await fixture.Inspection.SaveAsync(merged);

        var stored = await fixture.Inspection.FindAsync("wf-merge", "activity");
        Assert.NotNull(stored);
        Assert.Equal(ActivityExecutionStatus.Superseded, stored!.Status);
        Assert.Equal("successor", stored.SupersededByActivityExecutionId);
        Assert.Equal("checkpoint-2", stored.LastCheckpointId);
        Assert.Equal("yes", stored.Metadata["initial"]);
        Assert.Equal("yes", stored.Metadata["updated"]);
        Assert.Equal("yes", stored.Metadata["merge"]);
        Assert.Equal("bookmark", Assert.Single(stored.Bookmarks).Identity.BookmarkId);
        Assert.Equal("Retried", Assert.Single(stored.OutcomeNames));
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

    [Fact]
    public void Activity_execution_ef_owns_only_ef_surfaces_in_every_registration_order()
    {
        var orders = new Action<ServiceCollection>[]
        {
            services =>
            {
                services.AddRuntimeActivityExecutionEntityFrameworkCore(ActivityEfOptions());
                services.AddWorkflowRuntime();
            },
            services =>
            {
                services.AddWorkflowRuntime();
                services.AddRuntimeActivityExecutionEntityFrameworkCore(ActivityEfOptions());
            },
            services =>
            {
                services.AddRuntimeActivityExecutionEntityFrameworkCore(ActivityEfOptions());
                services.AddRuntimeActivityExecutionEntityFrameworkCore(ActivityEfOptions());
            }
        };

        foreach (var configure in orders)
        {
            var services = new ServiceCollection();
            configure(services);

            var backend = Assert.IsType<RuntimeActivityExecutionStoreBackend>(Assert.Single(services, descriptor => descriptor.ImplementationInstance is RuntimeActivityExecutionStoreBackend).ImplementationInstance);
            Assert.Equal(RuntimeActivityExecutionStoreBackend.EntityFramework, backend.Name);
            backend.EnsureOwnsRegisteredContracts(services);
            Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(InMemoryActivityExecutionInspectionStore));
            Assert.DoesNotContain(services, descriptor => descriptor.ImplementationType == typeof(InMemoryActivityExecutionInspectionStore));
            Assert.DoesNotContain(services, descriptor => descriptor.ImplementationInstance?.GetType() == typeof(InMemoryActivityExecutionInspectionStore));
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(EfActivityExecutionInspectionStore));
        }
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

    private static RuntimeActivityExecutionEntityFrameworkCoreOptions ActivityEfOptions() => new()
    {
        Provider = "Sqlite",
        ConnectionString = "Data Source=:memory:",
        RecoveryContinuationSigningKey = "ef-r07-r09-registration-signing-key-32-bytes"
    };

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
        public IActivityExecutionHierarchyCursorCodec HierarchyCursorCodec { get; }

        private Fixture(SqliteConnection connection, BookmarkStateDbContext context, Accessor accessor)
        {
            this.connection = connection;
            this.context = context;
            this.accessor = accessor;
            State = new EfActivityExecutionStateStore(context, accessor, new HmacRuntimeRecoveryContinuationCodec(Microsoft.Extensions.Options.Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "ef-r07-r09-test-signing-key-32-bytes" })));
            Inspection = new EfActivityExecutionInspectionStore(context, accessor, new HmacRuntimeRecoveryContinuationCodec(Microsoft.Extensions.Options.Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "ef-r07-r09-test-signing-key-32-bytes" })));
            HierarchyCursorCodec = new Elsa.Workflows.Runtime.Core.Services.HmacActivityExecutionHierarchyCursorCodec(Microsoft.Extensions.Options.Options.Create(new Elsa.Workflows.Runtime.Core.Services.ActivityExecutionHierarchyCursorOptions { SigningKey = "ef-r07-r09-test-hierarchy-signing-key-32-bytes" }));
            Hierarchy = new EfActivityExecutionHierarchyStore(context, accessor, HierarchyCursorCodec);
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
