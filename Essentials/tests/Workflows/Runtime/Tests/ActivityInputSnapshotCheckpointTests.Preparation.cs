using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class ActivityInputSnapshotCheckpointTests
{
    [Fact]
    public async Task Pinned_input_contract_cannot_be_mutated_after_preparation()
    {
        var executable = NewExecutable(("message", DurableLiteral("message", "hello")));
        var node = executable.RootActivity;
        var context = new RuntimeInputBindingResolutionContext("wf", "invocation");
        var first = await new RuntimeActivityInputMaterializer().MaterializeSnapshotAsync(node, "invocation", context, Now);
        var exposed = Assert.IsAssignableFrom<IDictionary<string, ActivityInputContract>>(node.ActivityContract!.Inputs);

        Assert.Throws<NotSupportedException>(() => exposed.Remove("message"));

        var second = await new RuntimeActivityInputMaterializer().MaterializeSnapshotAsync(node, "invocation", context, Now);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
    }

    [Fact]
    public async Task Empty_default_snapshot_does_not_read_execution_history_or_values()
    {
        var executable = NewExecutable();
        var state = NewScheduledState();
        await SaveWorkflowStateAsync(executable.Identity);
        await _activityStateStore.SaveAsync(state);
        var activities = new CountingActivities(_activityStateStore);
        var values = new CountingValues(_durableValueStateStore);
        var materializer = new RuntimeActivityInputSnapshotMaterializer(activities, values, _workflowStateStore);

        var actual = await materializer.MaterializeAsync(executable, executable.RootActivity, state, _serviceProvider, Now, default);
        var expected = await new RuntimeActivityInputMaterializer().MaterializeSnapshotAsync(
            executable.RootActivity, state.InvocationId,
            new RuntimeInputBindingResolutionContext("wfexec-1", state.InvocationId, state, executable: executable), Now);

        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
        Assert.Equal(0, activities.Reads);
        Assert.Equal(0, values.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Custom_materializer_receives_live_context_even_when_contract_has_no_inputs(bool hasInput)
    {
        var executable = hasInput ? NewExecutable(("message", DurableLiteral("message", "hello"))) : NewExecutable();
        var state = NewScheduledState();
        await SaveWorkflowStateAsync(executable.Identity);
        await _activityStateStore.SaveAsync(state);
        var custom = new ObservingMaterializer();
        using var services = new ServiceCollection().AddSingleton<IRuntimeActivityInputMaterializer>(custom).BuildServiceProvider();
        var activities = new CountingActivities(_activityStateStore);
        var values = new CountingValues(_durableValueStateStore);
        var materializer = new RuntimeActivityInputSnapshotMaterializer(activities, values, _workflowStateStore);

        await materializer.MaterializeAsync(executable, executable.RootActivity, state, services, Now, default);

        Assert.NotNull(custom.Context);
        Assert.Same(executable, custom.Context.Executable);
        Assert.Equal(state.InvocationId, Assert.Single(custom.Context.RuntimeView).InvocationId);
        Assert.True(activities.Reads > 0);
        Assert.True(values.Reads > 0);
    }

    [Fact]
    public async Task Empty_snapshot_observes_cancellation()
    {
        var executable = NewExecutable();
        var materializer = new RuntimeActivityInputSnapshotMaterializer(_activityStateStore, _durableValueStateStore, _workflowStateStore);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => materializer.MaterializeAsync(
            executable, executable.RootActivity, NewScheduledState(), _serviceProvider, Now, cancellation.Token).AsTask());
    }

    [Fact]
    public async Task Prepared_input_plan_resolves_each_workflows_current_values()
    {
        var binding = new RuntimeInputBinding("message", StringType, ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.WorkflowRequest, workflowRequest: new RuntimeWorkflowRequestReference("message"));
        var node = NewExecutable(("message", binding)).RootActivity;
        foreach (var text in new[] { "first-workflow", "second-workflow" })
        {
            var context = new RuntimeInputBindingResolutionContext(text, "invocation",
                workflowInputEnvelopes: new Dictionary<string, ValueEnvelope> { ["message"] = DurableLiteral("message", text).Literal! });
            var snapshot = await new RuntimeActivityInputMaterializer().MaterializeSnapshotAsync(node, "invocation", context, Now);
            Assert.Equal(text, snapshot.Values["message"].InlineValue!.Value.GetString());
        }
    }

    [Fact]
    public async Task Preparation_reuses_fingerprint_but_not_invocation_snapshot_across_materializers()
    {
        var executable = NewExecutable(("message", DurableLiteral("message", "hello")));
        var node = executable.RootActivity;
        var snapshots = await Task.WhenAll(Enumerable.Range(0, 32).Select(i => Task.Run(async () =>
            await new RuntimeActivityInputMaterializer().MaterializeSnapshotAsync(
                node, $"invocation-{i}", new RuntimeInputBindingResolutionContext($"workflow-{i}", $"invocation-{i}"), Now.AddSeconds(i)))));

        Assert.Equal(32, snapshots.Select(x => x.InvocationId).Distinct().Count());
        Assert.Equal(32, snapshots.Select(x => x.MaterializedAt).Distinct().Count());
        foreach (var snapshot in snapshots)
        {
            Assert.Equal("hello", snapshot.Values["message"].InlineValue!.Value.GetString());
            Assert.Same(snapshots[0].BindingFingerprint, snapshot.BindingFingerprint);
        }

        // A different materialized node can have the same node ID, but different immutable content.
        var other = NewExecutable(("message", DurableLiteral("message", "different")));
        var otherSnapshot = await new RuntimeActivityInputMaterializer().MaterializeSnapshotAsync(
            other.RootActivity, "other", new RuntimeInputBindingResolutionContext("other-workflow", "other"), Now);
        Assert.NotEqual(snapshots[0].BindingFingerprint, otherSnapshot.BindingFingerprint);
        Assert.Equal("different", otherSnapshot.Values["message"].InlineValue!.Value.GetString());
    }

    private sealed class ObservingMaterializer : IRuntimeActivityInputMaterializer
    {
        public RuntimeInputBindingResolutionContext? Context { get; private set; }

        public ValueTask<ActivityInputSnapshot> MaterializeSnapshotAsync(ExecutableNode node, string invocationId,
            RuntimeInputBindingResolutionContext resolutionContext, DateTimeOffset materializedAt, CancellationToken cancellationToken = default)
        {
            Context = resolutionContext;
            return new RuntimeActivityInputMaterializer().MaterializeSnapshotAsync(node, invocationId, resolutionContext, materializedAt, cancellationToken);
        }
    }

    private sealed class CountingActivities(IActivityExecutionStateStore inner) : IActivityExecutionStateStore
    {
        public int Reads { get; private set; }
        public ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken ct = default) => inner.SaveAsync(state, ct);
        public ValueTask<ActivityExecutionState?> FindAsync(string workflow, string activity, CancellationToken ct = default)
        {
            Reads++;
            return inner.FindAsync(workflow, activity, ct);
        }
        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListPageAsync(ActivityExecutionStatePageQuery query, CancellationToken ct = default)
        {
            Reads++;
            return inner.ListPageAsync(query, ct);
        }
        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListByParentPageAsync(ActivityExecutionStateParentPageQuery query, CancellationToken ct = default)
        {
            Reads++;
            return inner.ListByParentPageAsync(query, ct);
        }
    }

    private sealed class CountingValues(IDurableValueStateStore inner) : IDurableValueStateStore
    {
        public int Reads { get; private set; }
        public ValueTask<DurableValueState> SaveAsync(DurableValueState state, CancellationToken ct = default) => inner.SaveAsync(state, ct);
        public ValueTask<bool> DeleteAsync(string workflow, string value, CancellationToken ct = default) => inner.DeleteAsync(workflow, value, ct);
        public ValueTask<DurableValueState?> FindAsync(string workflow, string value, CancellationToken ct = default)
        {
            Reads++;
            return inner.FindAsync(workflow, value, ct);
        }
        public ValueTask<RuntimeStorePage<DurableValueState>> ListPageAsync(DurableValueStatePageQuery query, CancellationToken ct = default)
        {
            Reads++;
            return inner.ListPageAsync(query, ct);
        }
    }
}
