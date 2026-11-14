using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>One whole-result read path for bindings and expression policy/value materialization.</summary>
internal static class RuntimeActivityResultReader
{
    internal static RuntimeActivityResultRead? Read(RuntimeActivityResultReference reference, RuntimeInputBindingResolutionContext context) =>
        ReadCore(reference.ProducerExecutableNodeId, reference.ProjectionKey, reference.ProducerScopeId, context,
            consumer => new CausalActivityResultResolver().Resolve(reference, consumer, context.RuntimeView)?.Completion.Result);

    internal static RuntimeActivityResultRead? Read(ActivityResultExpressionParameterBinding reference, RuntimeInputBindingResolutionContext context) =>
        ReadCore(reference.ProducerNodeId, reference.ProjectionKey, null, context,
            consumer => new CausalActivityResultResolver().Resolve(reference, consumer, context.RuntimeView)?.Completion.Result);

    private static RuntimeActivityResultRead? ReadCore(string producerId, string projectionKey, string? producerScopeId,
        RuntimeInputBindingResolutionContext context, Func<ActivityExecutionState, ValueEnvelope?> readActual)
    {
        var consumer = context.ConsumerInvocation
            ?? throw new InvalidOperationException("An activity result read requires its consumer invocation.");
        var workflow = context.WorkflowState;
        var test = workflow is null ? null : RuntimeTestContextOwner.RequireContext(workflow);
        if (test is null) return Actual();

        // This is a committed context snapshot, not an ambient overlay. No fixture becomes a producer
        // invocation or a causal edge, and no unowned consumer may inherit another leaf's fixtures.
        if (workflow!.WorkflowExecutionId != context.WorkflowExecutionId ||
            consumer.Execution.WorkflowExecutionId != context.WorkflowExecutionId || consumer.InvocationId != context.ActivityExecutionId ||
            !test.OwnedNodeIds.Contains(consumer.Execution.ExecutableNodeId, StringComparer.Ordinal) ||
            consumer.ParentActivityExecutionId is not null || workflow.Status != WorkflowExecutionStatus.Running ||
            workflow.RootVariableFrame?.Status != VariableFrameStatus.Active ||
            test.Frames.Any(x => x.Frame.Status != VariableFrameStatus.Active) || context.Executable is not { } executable ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(workflow.PinnedExecutable, executable.Identity))
            throw Refused();
        // Actual owned results remain authoritative when a broader native profile is qualified.
        if (test.OwnedNodeIds.Contains(producerId, StringComparer.Ordinal)) return Actual();
        var fixtures = test.Results.Where(x => x.ProducerNodeId == producerId &&
            (producerScopeId is null || x.ProducerScopeId == producerScopeId)).ToArray();
        if (fixtures.Length != 1 || !executable.NodesById.TryGetValue(producerId, out var producer) ||
            producer.ActivityContract?.Result is not { } contract)
            throw Refused(); // Even an optional outside reference needs explicit setup.
        var result = fixtures[0].Result;
        if (string.IsNullOrWhiteSpace(fixtures[0].ProducerScopeId) || !RuntimeTestContextMaterializer.SameType(result.Type, contract.Type) ||
            result.Presence is ValuePresence.Absent or ValuePresence.Withheld ||
            contract.IsRequired && result.Presence == ValuePresence.ExplicitNull ||
            result.Policy.Lifecycle == DurableValueLifecycle.None || result.ExternalReference is not null || result.TransientResource is not null ||
            !result.Policy.Satisfies(ValuePolicyCombiner.ToProtectionPolicy(contract.Policy)) ||
            result.HoldsValueRequiringEncryption())
            throw Refused();
        return new(RuntimeTestResultProjections.Read(result, contract, projectionKey), true);

        RuntimeActivityResultRead? Actual() => readActual(consumer) is { } value ? new(value, false) : null;
    }

    private static InvalidOperationException Refused() =>
        new("The outside result fixture is unavailable for this admitted native consumer.");
}

internal sealed record RuntimeActivityResultRead(ValueEnvelope Value, bool IsFixture);
