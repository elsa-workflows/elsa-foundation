using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class BackgroundActivityCheckpointEnricherTests : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    public BackgroundActivityCheckpointEnricherTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWorkflowRuntime();
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        _provider = services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(WorkflowExecutionStatus.Completed)]
    [InlineData(WorkflowExecutionStatus.Faulted)]
    [InlineData(WorkflowExecutionStatus.Cancelled)]
    public async Task Terminal_checkpoint_cancels_background_work_only_after_durable_admission(WorkflowExecutionStatus status)
    {
        await using var scope = _provider.CreateAsyncScope();
        var enrichers = scope.ServiceProvider.GetServices<IRuntimeCheckpointCommitEnricher>().ToArray();
        Assert.NotEmpty(enrichers);
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>();
        CanonicalWorkflowStateTestData.EnsureRunning(store);
        var state = (await store.FindAsync("wfexec-1"))! with { Status = status };

        foreach (var admitted in new[] { false, true })
        {
            var terminal = state with
            {
                SystemMetadata = admitted
                    ? new Dictionary<string, string> { [RuntimeMetadataKeys.WorkflowHasBackgroundActivityWork] = "true" }
                    : new Dictionary<string, string>()
            };
            // There need not be an activity delta: an unrelated leaf or an operator can terminate the run.
            var commit = new RuntimeCheckpointCommit(
                "commit-terminal",
                new RuntimeCheckpoint("checkpoint-terminal", "Terminal", state.WorkflowExecutionId, DateTimeOffset.UnixEpoch, [], new Dictionary<string, string>()),
                new RuntimeCheckpointStateChangeSet(
                    new RuntimeStateChange<WorkflowExecutionState>(state.WorkflowExecutionId, RuntimeStateChangeOperation.Upsert, terminal, new Dictionary<string, string>()),
                    null, [], [], [], [], []),
                [], new Dictionary<string, string>());
            foreach (var enricher in enrichers)
                commit = await enricher.EnrichAsync(commit);

            if (admitted)
            {
                var cancellation = Assert.Single(commit.StateChanges.BackgroundActivityWork.Cancellations);
                Assert.Equal(state.WorkflowExecutionId, cancellation.WorkflowExecutionId);
                Assert.Null(cancellation.ActivityExecutionIds);
            }
            else
                Assert.True(commit.StateChanges.BackgroundActivityWork.IsEmpty);
        }
    }

    [Fact]
    public async Task Activity_cancellation_targets_only_admitted_background_invocations()
    {
        await using var scope = _provider.CreateAsyncScope();
        var inline = Cancelled("inline", new Dictionary<string, string>());
        var background = Cancelled("background", new Dictionary<string, string> { ["runtime.backgroundActivityWorkId"] = "work-1" });
        var commit = new RuntimeCheckpointCommit(
            "commit-cancel",
            new RuntimeCheckpoint("checkpoint-cancel", "Cancel", "wfexec-1", DateTimeOffset.UnixEpoch, ["inline", "background"], new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(null, null, [inline, background], [], [], [], []),
            [], new Dictionary<string, string>());
        foreach (var enricher in scope.ServiceProvider.GetServices<IRuntimeCheckpointCommitEnricher>())
            commit = await enricher.EnrichAsync(commit);

        var cancellation = Assert.Single(commit.StateChanges.BackgroundActivityWork.Cancellations);
        Assert.Equal(["background"], cancellation.ActivityExecutionIds);

        static RuntimeStateChange<ActivityExecutionState> Cancelled(string id, IReadOnlyDictionary<string, string> metadata)
        {
            var state = new ActivityExecutionState(
                new ActivityExecution(id, "wfexec-1", id, id, "test", "1.0.0"),
                ActivityExecutionStatus.Cancelled, null, 1, DateTimeOffset.UnixEpoch, null, DateTimeOffset.UnixEpoch,
                null, null, null, null, ActivitySchedulingProvenance.Empty, null, [], [], 0, 0, metadata);
            return new RuntimeStateChange<ActivityExecutionState>(id, RuntimeStateChangeOperation.Upsert, state, new Dictionary<string, string>());
        }
    }

    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}
