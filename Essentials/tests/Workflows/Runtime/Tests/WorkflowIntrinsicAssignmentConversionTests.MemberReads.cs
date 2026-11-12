using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// A Set intrinsic whose value reads one member (#4078 slice B) of a variable or a request member, inline and
/// externally stored: it writes exactly the selected member, validated against the destination like any assigned
/// value, and refuses an absent member or one that does not fit with its VF-SEL code before writing anything.
/// </summary>
public sealed partial class WorkflowIntrinsicAssignmentConversionTests
{
    [Theory]
    [InlineData("variable", false)]
    [InlineData("variable", true)]
    [InlineData("request", false)]
    [InlineData("request", true)]
    public async Task Set_writes_exactly_the_selected_member(string source, bool external)
    {
        var commit = await ExecuteAsync(MemberNode(source, ["count"]), 7, external);

        var value = commit.StateChanges.WorkflowExecution!.State.RootVariableFrame!.Values["counter"];
        var payload = value.InlineValue ?? await _payloadStore.ReadAsync(value.ExternalReference!);
        Assert.Equal(7, payload.GetInt32());
    }

    [Theory]
    [InlineData("missing", RuntimeReadSelectionException.AbsentMember)]
    [InlineData("count", RuntimeReadSelectionException.Mismatch)]
    public async Task Set_refuses_a_member_it_cannot_assign_with_its_code(string segment, string code)
    {
        var exception = await Assert.ThrowsAsync<RuntimeReadSelectionException>(() =>
            ExecuteAsync(MemberNode("variable", [segment]), "not-a-number", external: false).AsTask());

        Assert.Equal(code, exception.Code);
        Assert.Equal(0, (await _workflowStore.FindAsync(WorkflowExecutionId))!.RootVariableFrame!.Values["counter"].InlineValue!.Value.GetInt32());
    }

    [Fact]
    public async Task Set_refuses_a_member_looked_up_in_a_non_object_in_the_wording_the_activity_path_uses()
    {
        var exception = await Assert.ThrowsAsync<RuntimeReadSelectionException>(() =>
            ExecuteAsync(MemberNode("variable", ["count", "x"]), 7, external: false).AsTask());

        Assert.Equal(RuntimeReadSelectionException.NotSelectable, exception.Code);
        Assert.Contains("is looked up in a number, which has no members", exception.Message, StringComparison.Ordinal);
    }

    private static ExecutableNode MemberNode(string source, string[] segments)
    {
        var selection = new RuntimeReadSelection(1, segments, RuntimeReadSelectionMode.DeferredFit);
        var binding = source == "variable"
            ? new RuntimeInputBinding("value", IntType, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.VariableMember, selection,
                variable: new RuntimeVariableReference("source", "workflow"))
            : new RuntimeInputBinding("value", IntType, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.WorkflowRequestMember, selection,
                workflowRequest: new RuntimeWorkflowRequestReference("source"));
        return new ExecutableNode("set", "set", "elsa.intrinsic.set", "1.0.0", "intrinsic", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding> { ["value"] = binding }, new Dictionary<string, string>(),
            intrinsicKind: WorkflowIntrinsicKind.Set, intrinsicVariable: new RuntimeVariableReference("counter", "workflow"));
    }
}
