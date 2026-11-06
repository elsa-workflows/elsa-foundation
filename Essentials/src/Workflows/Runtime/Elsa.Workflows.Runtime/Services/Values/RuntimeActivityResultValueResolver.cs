using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>One typed result lookup for bindings, expression policies and expression values.</summary>
internal static class RuntimeActivityResultValueResolver
{
    internal static ValueEnvelope? Resolve(RuntimeActivityResultReference reference, RuntimeInputBindingResolutionContext context) =>
        Resolve(reference.ProducerExecutableNodeId, reference.ProducerScopeId, context,
            () => new CausalActivityResultResolver().Resolve(reference, RequireConsumer(context), context.RuntimeView)?.Completion.Result);

    internal static ValueEnvelope? Resolve(ActivityResultExpressionParameterBinding reference, RuntimeInputBindingResolutionContext context) =>
        Resolve(reference.ProducerNodeId, null, context,
            () => new CausalActivityResultResolver().Resolve(reference, RequireConsumer(context), context.RuntimeView)?.Completion.Result);

    private static ValueEnvelope? Resolve(string producerId, string? scopeId, RuntimeInputBindingResolutionContext context,
        Func<ValueEnvelope?> normal)
    {
        if (context.TestContext is not { } setup || setup.OwnedNodeIds.Contains(producerId, StringComparer.Ordinal))
            return normal();
        var candidates = setup.Results.Where(x => x.ProducerNodeId == producerId &&
            (scopeId is null || x.ProducerScopeId == scopeId)).ToArray();
        if (candidates.Length != 1)
            throw new InvalidOperationException("The selected activity requires one explicit prior-result fixture; missing and ambiguous fixtures are unavailable.");
        var fixture = candidates[0].Result;
        var producer = context.Executable?.NodesById.GetValueOrDefault(producerId)?.ActivityContract
            ?? throw new InvalidOperationException("The prior-result fixture's pinned producer contract is unavailable.");
        var policy = ValuePolicyCombiner.RequireMinimum(fixture.Policy, producer.Result.Policy, "Prior-result test fixture");
        return new(fixture.Type, fixture.Presence, fixture.InlineValue, fixture.ExternalReference, policy, fixture.WithheldValue);
    }

    private static ActivityExecutionState RequireConsumer(RuntimeInputBindingResolutionContext context) =>
        context.ConsumerInvocation ?? throw new InvalidOperationException("Activity result resolution requires an actual consuming invocation.");
}
