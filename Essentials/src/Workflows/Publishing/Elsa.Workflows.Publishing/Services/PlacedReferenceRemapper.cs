using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Rewrites the template-local identities that a closed-scope graph body's references carry into the identities of
/// one placement (spec 265, ADR 0027). A body node refers to another body node, to a body container's variables, or
/// to the graph's own variables by its template-local identity; once the placer has given every body node its placed
/// identity, this rewrites those references to match.
/// </summary>
/// <remarks>
/// Only the node's own input bindings, output captures and intrinsic variable are touched; its descendants are left
/// alone, because a nested graph's body was already remapped by its own placement frame. An identity that is not in
/// the map is left unchanged: publication validation refuses such references for closed-scope templates, and a
/// legacy template keeps failing where it did before (VF-COER-001).
/// </remarks>
internal static class PlacedReferenceRemapper
{
    /// <summary>The scope id Studio authored for graph variables before spec 265; read as the template root.</summary>
    public const string LegacyGraphScopeId = "activity-graph";

    public static ExecutableNode Remap(
        ExecutableNode node,
        IReadOnlyDictionary<string, string> placedNodeIds,
        string graphScopeId)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(placedNodeIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(graphScopeId);

        var changed = false;
        var inputBindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal);
        foreach (var (inputName, binding) in node.InputBindings)
        {
            var remapped = RemapBinding(binding, placedNodeIds, graphScopeId);
            changed |= !ReferenceEquals(remapped, binding);
            inputBindings[inputName] = remapped;
        }

        var intrinsicVariable = node.IntrinsicVariable;
        if (intrinsicVariable is not null)
        {
            var scopeId = RemapScope(intrinsicVariable.DeclaringScopeId, placedNodeIds, graphScopeId);
            if (!StringComparer.Ordinal.Equals(scopeId, intrinsicVariable.DeclaringScopeId))
            {
                intrinsicVariable = new RuntimeVariableReference(intrinsicVariable.VariableKey, scopeId);
                changed = true;
            }
        }

        if (!changed)
            return node;

        return new ExecutableNode(
            node.ExecutableNodeId,
            node.AuthoredActivityId,
            node.ActivityType,
            node.ActivityTypeVersion,
            node.Descriptor,
            inputBindings,
            node.OutputCaptures,
            node.Metadata,
            node.ExecutionPlacement,
            node.ChildSlots,
            node.Structure,
            node.ActivityContract,
            node.IntrinsicKind,
            intrinsicVariable);
    }

    private static RuntimeInputBinding RemapBinding(
        RuntimeInputBinding binding,
        IReadOnlyDictionary<string, string> placedNodeIds,
        string graphScopeId)
    {
        switch (binding.Source)
        {
            case RuntimeInputBindingSource.VariableRead when binding.Variable is { } variable:
            {
                var scopeId = RemapScope(variable.DeclaringScopeId, placedNodeIds, graphScopeId);
                return StringComparer.Ordinal.Equals(scopeId, variable.DeclaringScopeId)
                    ? binding
                    : Rebuild(binding, variable: new RuntimeVariableReference(variable.VariableKey, scopeId));
            }
            case RuntimeInputBindingSource.ActivityResult when binding.ActivityResult is { } result:
            {
                var producerId = placedNodeIds.GetValueOrDefault(result.ProducerExecutableNodeId, result.ProducerExecutableNodeId);
                var producerScopeId = placedNodeIds.GetValueOrDefault(result.ProducerScopeId, result.ProducerScopeId);
                if (StringComparer.Ordinal.Equals(producerId, result.ProducerExecutableNodeId) &&
                    StringComparer.Ordinal.Equals(producerScopeId, result.ProducerScopeId))
                    return binding;
                return Rebuild(binding, activityResult: new RuntimeActivityResultReference(
                    producerId,
                    result.ProjectionKey,
                    producerScopeId,
                    result.IsOptional));
            }
            case RuntimeInputBindingSource.Expression when binding.Expression is { } expression:
            {
                var remapped = RemapExpression(expression, placedNodeIds, graphScopeId);
                return ReferenceEquals(remapped, expression) ? binding : Rebuild(binding, expression: remapped);
            }
            default:
                return binding;
        }
    }

    private static RuntimeExpressionBinding RemapExpression(
        RuntimeExpressionBinding expression,
        IReadOnlyDictionary<string, string> placedNodeIds,
        string graphScopeId)
    {
        var changed = false;
        var parameters = new Dictionary<string, ExpressionParameterBinding>(StringComparer.Ordinal);
        foreach (var (name, parameter) in expression.Parameters)
        {
            ExpressionParameterBinding remapped = parameter switch
            {
                VariableExpressionParameterBinding variable when RemapScope(variable.DeclaringScopeNodeId, placedNodeIds, graphScopeId) is var scopeId &&
                                                                 !StringComparer.Ordinal.Equals(scopeId, variable.DeclaringScopeNodeId)
                    => new VariableExpressionParameterBinding(scopeId, variable.VariableKey),
                ActivityResultExpressionParameterBinding result when placedNodeIds.TryGetValue(result.ProducerNodeId, out var producerId)
                    => new ActivityResultExpressionParameterBinding(producerId, result.ProjectionKey, result.IsOptional),
                _ => parameter
            };
            changed |= !ReferenceEquals(remapped, parameter);
            parameters[name] = remapped;
        }

        if (!changed)
            return expression;

        return new RuntimeExpressionBinding(
            expression.Language,
            expression.Expression,
            expression.ResultType,
            expression.Metadata,
            parameters,
            expression.Options,
            expression.CapabilityProfile,
            expression.ParameterConversionPlans);
    }

    private static string RemapScope(string scopeId, IReadOnlyDictionary<string, string> placedNodeIds, string graphScopeId)
    {
        if (StringComparer.Ordinal.Equals(scopeId, VariableReference.WorkflowScopeId) ||
            StringComparer.Ordinal.Equals(scopeId, LegacyGraphScopeId))
            return graphScopeId;

        return placedNodeIds.GetValueOrDefault(scopeId, scopeId);
    }

    // Every field but the rewritten source payload is carried over, so secret, conversion and metadata survive.
    private static RuntimeInputBinding Rebuild(
        RuntimeInputBinding binding,
        RuntimeVariableReference? variable = null,
        RuntimeActivityResultReference? activityResult = null,
        RuntimeExpressionBinding? expression = null) => new(
        binding.InputName,
        binding.TargetType,
        binding.EffectivePolicy,
        binding.Source,
        binding.Literal,
        binding.WorkflowRequest,
        variable ?? binding.Variable,
        activityResult ?? binding.ActivityResult,
        expression ?? binding.Expression,
        binding.Metadata,
        binding.ConversionPlan,
        binding.ConversionRequest,
        binding.Secret);
}
