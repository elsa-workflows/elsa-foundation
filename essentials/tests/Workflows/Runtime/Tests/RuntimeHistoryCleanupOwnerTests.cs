using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeHistoryCleanupOwnerTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch.AddDays(1);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Original_cleanup_inventory_is_selected_without_deleting_or_copying_bookmark_payload(bool captured)
    {
        var executions = new InMemoryWorkflowExecutionStateStore();
        var policy = captured ? new RuntimeHistoryCapturePolicy("runtime-history-v3", 3, true, ["BookmarkRemoved"]) : null;
        await executions.SaveAsync(Workflow(policy));
        var bookmarks = new InMemoryBookmarkStateStore();
        var selected = Bookmark("owned", "activity");
        await bookmarks.SaveAsync(selected);
        await bookmarks.SaveAsync(Bookmark("other", "sibling"));
        var cleanup = new ActivityScopeCleanupStore(bookmarks, new InMemoryDurableTimerStore(), new InMemoryWorkflowSchedulerWorkQueue(), executions);
        var inventory = await cleanup.CaptureAsync("run", "activity", new HashSet<string> { "activity" });
        Assert.Equal(new[] { "owned" }, inventory.BookmarkIds);
        Assert.NotNull(await bookmarks.FindAsync("run", "owned"));
        Assert.NotNull(await bookmarks.FindAsync("run", "other"));
        if (!captured) Assert.Null(inventory.HistoryBookmarks);
        else
        {
            var summary = Assert.Single(inventory.HistoryBookmarks!);
            Assert.Equal(ActivityExecutionBookmarkSummary.From(selected), summary);
            Assert.DoesNotContain("private", JsonSerializer.Serialize(summary));
        }
    }

    [Fact]
    public async Task Legacy_cleanup_constructor_keeps_its_behavior_and_never_claims_history_inventory_support()
    {
        var bookmarks = new InMemoryBookmarkStateStore();
        await bookmarks.SaveAsync(Bookmark("owned", "activity"));
        IActivityScopeCleanupStore cleanup = new ActivityScopeCleanupStore(bookmarks, new InMemoryDurableTimerStore(), new InMemoryWorkflowSchedulerWorkQueue());
        Assert.False(cleanup.SupportsHistoryBookmarks);
        var inventory = await cleanup.CaptureAsync("run", "activity", new HashSet<string> { "activity" });
        Assert.Null(inventory.HistoryBookmarks);
        await cleanup.ApplyAsync(inventory);
        Assert.Null(await bookmarks.FindAsync("run", "owned"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ordinary_composition_only_adopts_the_inventory_reader_when_internal_history_is_composed(bool composed)
    {
        var services = Services();
        if (composed) RuntimeHistoryRegistration.AddServices(services);
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        Assert.Equal(composed, scope.ServiceProvider.GetRequiredService<IActivityScopeCleanupStore>().SupportsHistoryBookmarks);
    }

    [Fact]
    public void Selected_legacy_cleanup_adapter_refuses_required_profile_before_admission()
    {
        var services = Services();
        RuntimeHistoryRegistration.AddServices(services);
        services.AddScoped<IActivityScopeCleanupStore>(provider => new ActivityScopeCleanupStore(provider.GetRequiredService<IBookmarkStateStore>(),
            provider.GetRequiredService<IDurableTimerStore>(), provider.GetRequiredService<IWorkflowSchedulerWorkQueue>()));
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var unavailable = Assert.Throws<RuntimeSchedulerWorkUnavailableException>(() => RuntimeHistoryRegistration.ResolvePolicy(scope.ServiceProvider, "run"));
        Assert.Equal("runtime.history.capture-unavailable", unavailable.Code);
        Assert.Empty(scope.ServiceProvider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_workflow_or_activity_scope_cancel_retains_wait_disposition_after_native_cleanup(bool workflowWide)
    {
        var services = Services();
        RuntimeHistoryRegistration.AddServices(services);
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var selected = scope.ServiceProvider;
        var policy = RuntimeHistoryRegistration.ResolvePolicy(selected, "run")!;
        var executions = selected.GetRequiredService<IWorkflowExecutionStateStore>();
        var activities = selected.GetRequiredService<IActivityExecutionStateStore>();
        var bookmarks = selected.GetRequiredService<IBookmarkStateStore>();
        await executions.SaveAsync(Workflow(policy));
        await activities.SaveAsync(Activity());
        await bookmarks.SaveAsync(Bookmark("wait", "activity"));
        var incidents = selected.GetRequiredService<IIncidentStateStore>();
        if (!workflowWide)
            await incidents.TryAddAsync(new("incident", "run", "activity", "node", IncidentSeverity.Error, IncidentStatus.Open,
                null, "failure", "private incident detail", At, null));
        var command = workflowWide ? WorkflowExecutionCommandKind.Cancel : WorkflowExecutionCommandKind.CancelActivityScope;
        var item = new RuntimeSchedulerWorkItem("work", "run", "command", command, "envelope", "idempotency", At, At,
            payload: workflowWide ? null : JsonSerializer.SerializeToElement(new CancelActivityScopeCommand("activity", "activity", "owner-request")));
        var committer = selected.GetRequiredService<RuntimeCheckpointCommitter>();
        var accumulator = selected.GetRequiredService<IRuntimeActivityExecutionInspectionAccumulator>();
        var clock = selected.GetRequiredService<TimeProvider>();
        IWorkflowSchedulerWorkHandler handler = workflowWide
            ? new WorkflowCancelSchedulerWorkHandler(executions, activities, committer, accumulator, clock, selected.GetRequiredService<IActivityScopeCleanupStore>())
            : new WorkflowCancelActivityScopeSchedulerWorkHandler(activities, selected.GetRequiredService<ActivitySubtreeCancellationPlanner>(), committer, accumulator, clock);
        await handler.HandleAsync(item);
        await handler.HandleAsync(item); // Existing native terminal guards prevent a new cancellation commit.
        Assert.Equal(ActivityExecutionStatus.Cancelled, (await activities.FindAsync("run", "activity"))!.Status);
        Assert.Equal(workflowWide ? WorkflowExecutionStatus.Cancelled : WorkflowExecutionStatus.Running, (await executions.FindAsync("run"))!.Status);
        Assert.Null(await bookmarks.FindAsync("run", "wait"));
        var record = Assert.Single(selected.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
        var inventory = Assert.Single(record.Commit.StateChanges.ActivityScopeCleanups);
        Assert.Equal("wait", Assert.Single(inventory.HistoryBookmarks!).Identity.BookmarkId);
        var reader = selected.GetRequiredService<IRuntimeHistoryStore>();
        var retained = await reader.FindBookmarkAsync("run", "wait");
        Assert.Equal(RuntimeHistoryBookmarkStatus.Removed, retained!.Status);
        Assert.Null(retained.CreatedHistoryEventId);
        Assert.Equal(RuntimeHistoryCreationEvidence.CreationMetadata, retained.PriorState);
        var page = await reader.ReadPageAsync(new("run"));
        Assert.Contains(page.Items, e => e.Name == "ControlApplied");
        Assert.Contains(page.Items, e => e.Name == "ActivityCancelled");
        Assert.Contains(page.Items, e => e.Name == "BookmarkRemoved");
        if (workflowWide) Assert.Contains(page.Items, e => e.Name == "WorkflowCancelled");
        else Assert.Contains(page.Items, e => e.Name == "IncidentSuppressed");
        Assert.All(page.Items, e => Assert.DoesNotContain("private", string.Join(" ", e.Metadata.Values)));
        Assert.All(record.Commit.StateChanges.ActivityExecutionInspections, change =>
            Assert.Equal("work", change.State.CheckpointValueCapture!.SchedulerWorkItemId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cleanup_policy_failure_preserves_hard_corruption_versus_provider_unavailability(bool malformed)
    {
        Exception failure = new InvalidOperationException("native context", malformed ? new InvalidDataException("corrupt") : new IOException("unavailable"));
        var cleanup = new ActivityScopeCleanupStore(new InMemoryBookmarkStateStore(), new InMemoryDurableTimerStore(),
            new InMemoryWorkflowSchedulerWorkQueue(), new FailedRead(failure));
        var caught = await Record.ExceptionAsync(async () => await cleanup.CaptureAsync("run", "activity", new HashSet<string> { "activity" }));
        if (malformed) Assert.Same(failure, Assert.IsType<RuntimeHistoryIntegrityException>(caught).InnerException);
        else Assert.Same(failure, Assert.IsType<RuntimeSchedulerWorkUnavailableException>(caught).InnerException);
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(At));
        services.AddSingleton<IPersistenceAccessContextAccessor>(new RuntimeHistoryTestAccess());
        services.AddSingleton<IWorkflowExecutableRootWriteLeaseManager>(PassThroughWorkflowExecutableRootWriteLeaseManager.Instance);
        services.AddWorkflowRuntime();
        services.Configure<RuntimeRecoveryContinuationOptions>(options => options.SigningKey = "cleanup-history-tests-signing-key-at-least-32-bytes");
        return services;
    }
    private static BookmarkState Bookmark(string id, string activity) => new(id, "run", activity, "node", "resume", "task", "hash",
        JsonSerializer.SerializeToElement("private bookmark payload"), new Dictionary<string, string> { ["private"] = "private metadata" }, At, null);
    private static WorkflowExecutionState Workflow(RuntimeHistoryCapturePolicy? policy) => new("run", new("artifact", "definition", "version", "1", "hash"),
        WorkflowExecutionStatus.Running, null, At, At, At, null, null, null, null, new Dictionary<string, string>()) { HistoryCapturePolicy = policy };
    private static ActivityExecutionState Activity() => new(new("activity", "run", "node", "authored", "test", "1"), ActivityExecutionStatus.Suspended,
        null, 1, At, At, null, null, null, null, null, ActivitySchedulingProvenance.From("run", null, null, null, null, null, "activity", "test"),
        0, [], [], 0, 0, new Dictionary<string, string>(), ExecutionScopeId: "activity");
    private sealed class FailedRead(Exception failure) : IWorkflowExecutionStateStore
    {
        public ValueTask<WorkflowExecutionState?> FindAsync(string id, CancellationToken cancellationToken = default) => throw failure;
        public ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowExecutionStatePage> QueryPageAsync(WorkflowExecutionStatePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
