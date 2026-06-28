using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Per-execution isolation of container-scoped variable values (#210). Values belong to one
/// concrete container activity execution: repeated/retried/parallel executions do not collide,
/// resume restores the original execution's values, and completed scopes stop being live while
/// retaining their captured values as inspection evidence.
/// </summary>
public sealed class ContainerScopedVariableIsolationTests
{
    private const string ContainerNodeId = "container-node";

    private static VariableReference Ref() => new("var-counter", ContainerNodeId);

    private static VariableScope ContainerExecution(string executionId, IReadOnlyDictionary<string, object?>? initialValues = null) =>
        new(
            ContainerNodeId,
            new Dictionary<string, IVariable> { ["var-counter"] = new Variable("Counter", 0) },
            parent: null,
            executionId: executionId,
            initialValues: initialValues);

    [Fact]
    public void Repeated_executions_of_the_same_container_declaration_do_not_share_values()
    {
        var firstExecution = ContainerExecution("actexec-1");
        var secondExecution = ContainerExecution("actexec-2");

        Assert.True(firstExecution.TrySetValue(Ref(), 11));

        // The second concrete execution of the same authored container keeps its own value.
        Assert.True(secondExecution.TryGetValue(Ref(), out var secondValue));
        Assert.Equal(0, secondValue);
        Assert.True(firstExecution.TryGetValue(Ref(), out var firstValue));
        Assert.Equal(11, firstValue);
    }

    [Fact]
    public void Resume_restores_the_original_container_executions_values()
    {
        var original = ContainerExecution("actexec-1");
        original.TrySetValue(Ref(), 7);

        // Suspend: capture the scope's values; resume: rebuild the same concrete execution from them.
        var snapshot = original.SnapshotValues();
        var resumed = ContainerExecution("actexec-1", snapshot);

        Assert.True(resumed.TryGetValue(Ref(), out var value));
        Assert.Equal(7, value);
    }

    [Fact]
    public void Completed_scope_is_no_longer_live_but_retains_values_for_inspection()
    {
        var execution = ContainerExecution("actexec-1");
        execution.TrySetValue(Ref(), 5);

        execution.Complete();

        Assert.True(execution.IsCompleted);
        Assert.False(execution.TryGetValue(Ref(), out _));   // not live for later expressions
        Assert.False(execution.TrySetValue(Ref(), 6));        // cannot mutate a completed scope
        Assert.Equal(5, execution.SnapshotValues()["var-counter"]); // retained as inspection evidence
    }
}
