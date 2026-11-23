using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests.VariableLifecycle;

public sealed class StepWorkflowStateDiagnosticTests
{
    [Theory]
    [InlineData("cancel")]
    [InlineData("fault")]
    [InlineData("warning")]
    public async Task Protected_output_reads_mask_native_diagnostics_even_after_a_warning_was_reported(string mode)
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness("actexec-root");
        var node = StepVariableTestGraph.Leaf("node-root", typeof(ProtectedStateDiagnostic), new Dictionary<string, RuntimeInputBinding>
        {
            ["Mode"] = new("Mode", new Elsa.Primitives.Models.ValueTypeDescriptor("String"), ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.Literal, literal: ValueEnvelope.Inline(new Elsa.Primitives.Models.ValueTypeDescriptor("String"), JsonSerializer.SerializeToElement(mode), ValueProtectionPolicy.InstanceInline))
        });
        await harness.Services.GetRequiredService<IDurableValueStateStore>().SaveAsync(new DurableValueState(
            "protected-output", harness.ExecutionId, "output:Protected", new RuntimeValueTypeDescriptor("Any", "Single", null),
            DurableValueLifecycle.Instance, DurableValueStorage.Inline, JsonSerializer.SerializeToElement("protected-sentinel"), null,
            null, DateTimeOffset.UnixEpoch, new Dictionary<string, string> { [RuntimeMetadataKeys.IsSensitive] = "true" }));
        var run = await harness.RunAsync(StepVariableTestGraph.Single(node));
        var state = run.State("node-root");
        Assert.DoesNotContain("protected-sentinel", JsonSerializer.Serialize(state), StringComparison.Ordinal);
        Assert.Equal("Root-only warning", Assert.Single(state.Warnings!).Message);
        Assert.Equal(mode switch { "cancel" => ActivityExecutionStatus.Cancelled, "fault" => ActivityExecutionStatus.Faulted, _ => ActivityExecutionStatus.Completed }, state.Status);
    }
}

[AccessesWorkflowState]
public sealed class ProtectedStateDiagnostic : Activity<ActivityUnit>
{
    [ActivityInput] public string Mode { get; set; } = "warning";
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        context.Warnings.Report(new ExecutionWarning("test", "Member protected-sentinel", Details: new Dictionary<string, string> { ["path"] = "protected-sentinel" })
        { ProtectedForm = new ExecutionWarning("test", "Root-only warning") });
        Assert.True(context.WorkflowState.TryGetOutput("Protected", out var value));
        return ValueTask.FromResult(Mode switch
        {
            "cancel" => ActivityTransition.Cancel<ActivityUnit>(value.GetString()!),
            "fault" => ActivityTransition.Fault<ActivityUnit>(new ActivityFault("test", value.GetString()!)),
            _ => ActivityTransition.Complete(ActivityUnit.Value)
        });
    }
}
