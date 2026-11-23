using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests.VariableLifecycle;

public sealed class StepWorkflowStatePendingProtectionTests
{
    [Fact]
    public async Task Native_pending_reads_preserve_policy_without_establishing_a_concurrency_base()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness("actexec-root");
        var type = new Elsa.Primitives.Models.ValueTypeDescriptor("String");
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true);
        var secret = new RuntimeVariableDeclaration("secret-key", "Secret", type, policy,
            new RuntimeInputBinding("secret-key", type, policy, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement("old"), policy)));
        var node = StepVariableTestGraph.Leaf("node-root", typeof(PendingProtectedStateWriter), new Dictionary<string, RuntimeInputBinding>());
        var run = await harness.RunAsync(StepVariableTestGraph.Single(node, secret));
        var failed = run.State("node-root");
        Assert.Equal(ActivityExecutionStatus.Faulted, failed.Status);
        Assert.Empty(failed.SessionReads ?? []);
        Assert.Null(run.WorkflowState!.CorrelationId);
        Assert.Equal("old", run.WorkflowState.RootVariableFrame!.Values["secret-key"].InlineValue!.Value.GetString());
    }
}

[AccessesWorkflowState, AccessesUndeclaredVariables]
public sealed class PendingProtectedStateWriter : Activity<ActivityUnit>
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        context.Variables.Set("Secret", "new");
        context.WorkflowState.CorrelationId = context.Variables.Get<string>("Secret");
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }
}
