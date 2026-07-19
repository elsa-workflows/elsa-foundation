using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Activities.Runtime.Tests;

internal static class CanonicalWorkflowStateTestData
{
    public static IWorkflowExecutionStateStore EnsureRunning(
        IWorkflowExecutionStateStore store,
        string workflowExecutionId = "wfexec-1",
        WorkflowExecutableIdentity? identity = null)
    {
        var state = store.FindAsync(workflowExecutionId).AsTask().GetAwaiter().GetResult();
        if (state?.RootVariableFrame is not null)
            return store;

        identity ??= new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "hash-1");
        state ??= new WorkflowExecutionState(
            workflowExecutionId,
            identity,
            WorkflowExecutionStatus.Running,
            null,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            null,
            null,
            null,
            new Dictionary<string, string>());
        var root = new VariableFrameFactory().CreateRoot(
            workflowExecutionId,
            Elsa.Expressions.Core.Models.VariableReference.WorkflowScopeId,
            new Dictionary<string, ValueEnvelope>());
        store.SaveAsync(state with { RootVariableFrame = root }).AsTask().GetAwaiter().GetResult();
        return store;
    }
}

internal static class RuntimeVariableDeclarationTestData
{
    public static RuntimeVariableDeclaration Create(string key, string name, string alias, object? value)
    {
        var type = new Elsa.Primitives.Models.ValueTypeDescriptor(alias);
        var policy = ValueProtectionPolicy.InstanceInline;
        var envelope = value is null
            ? ValueEnvelope.Null(type, policy)
            : ValueEnvelope.Inline(type, System.Text.Json.JsonSerializer.SerializeToElement(value), policy);
        return new RuntimeVariableDeclaration(
            key,
            name,
            type,
            policy,
            new RuntimeInputBinding(key, type, policy, RuntimeInputBindingSource.Literal, literal: envelope));
    }
}
