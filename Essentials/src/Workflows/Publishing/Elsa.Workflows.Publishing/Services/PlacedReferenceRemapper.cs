using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using System.Text.Json;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Rewrites the template-local identities that a closed-scope graph body's references carry into the identities of
/// one placement. A body node refers to another body node, to a body container's variables, or
/// to the graph's own variables by its template-local identity; once the placer has given every body node its placed
/// identity, this rewrites those references to match.
/// </summary>
/// <remarks>
/// Only the node's own input bindings, canonical variable defaults and intrinsic variable are touched; its descendants are left
/// alone, because a nested graph's body was already remapped by its own placement frame. An identity that is not in
/// the map is left unchanged: publication validation refuses such references for closed-scope templates, and a
/// legacy template keeps failing where it did before (VF-COER-001).
/// </remarks>
internal static class PlacedReferenceRemapper
{
    /// <summary>The scope id Studio authored for graph variables before closed scopes; read as the template root.</summary>
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

        var structure = RemapVariableDefaults(node.ExecutableNodeId, node.Structure, placedNodeIds, graphScopeId);
        changed |= !ReferenceEquals(structure, node.Structure);

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
            structure,
            node.ActivityContract,
            node.IntrinsicKind,
            intrinsicVariable);
    }

    private static ExecutableActivityStructure? RemapVariableDefaults(string nodeId, ExecutableActivityStructure? structure,
        IReadOnlyDictionary<string, string> placedNodeIds, string graphScopeId)
    {
        if (structure?.Payload is not { ValueKind: JsonValueKind.Object } payload ||
            !payload.TryGetProperty("variables", out var variables) || variables.ValueKind != JsonValueKind.Array ||
            variables.EnumerateArray().Any(variable => variable.ValueKind != JsonValueKind.Object || variable.TryGetProperty("referenceKey", out _) ||
                !variable.TryGetProperty("variableKey", out var key) || key.ValueKind != JsonValueKind.String ||
                !variable.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String ||
                !variable.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.Object ||
                !variable.TryGetProperty("policy", out var policy) || policy.ValueKind != JsonValueKind.Object))
            return structure; // Authored activity-owned arrays and opaque values retain their existing representation.
        try
        {
            var changed = false;
            var remapped = variables.EnumerateArray().Select(record =>
            {
                var variable = record.Deserialize<RuntimeVariableDeclaration>(DescriptorPayloadSerializer.Options)!;
                if (variable.InitialBinding is not { } initial) return record.Clone();
                var next = RemapBinding(initial, placedNodeIds, graphScopeId);
                if (ReferenceEquals(initial, next)) return record.Clone();
                changed = true;
                var properties = record.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
                properties["initialBinding"] = JsonSerializer.SerializeToElement(next, DescriptorPayloadSerializer.Options);
                return JsonSerializer.SerializeToElement(properties, DescriptorPayloadSerializer.Options);
            }).ToArray();
            if (!changed) return structure;
            var properties = payload.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
            properties["variables"] = JsonSerializer.SerializeToElement(remapped, DescriptorPayloadSerializer.Options);
            return new(structure.Kind, structure.SchemaVersion, JsonSerializer.SerializeToElement(properties, DescriptorPayloadSerializer.Options));
        }
        catch (JsonException exception)
        { throw new WorkflowExecutableCompilationException(null, null, $"Invalid canonical variable declarations on placed node '{nodeId}'.", exception); }
    }

    private static RuntimeInputBinding RemapBinding(
        RuntimeInputBinding binding,
        IReadOnlyDictionary<string, string> placedNodeIds,
        string graphScopeId)
    {
        // A request member read is not remapped yet; result and variable member reads are.
        if (binding.Selection is not null && binding.Source is not (RuntimeInputBindingSource.ActivityResultMember or RuntimeInputBindingSource.VariableMember))
            throw PublishedMemberReadDiagnostics.Unsupported(null, binding);

        switch (binding.Source)
        {
            case RuntimeInputBindingSource.VariableMember when binding.Variable is { } memberVariable:
            {
                // The scope moves with the placement as a whole-value read's does; the selection is carried unchanged.
                var scopeId = RemapScope(memberVariable.DeclaringScopeId, placedNodeIds, graphScopeId);
                return StringComparer.Ordinal.Equals(scopeId, memberVariable.DeclaringScopeId)
                    ? binding
                    : new RuntimeInputBinding(
                        binding.InputName,
                        binding.TargetType,
                        binding.EffectivePolicy,
                        binding.Source,
                        binding.Selection,
                        variable: new RuntimeVariableReference(memberVariable.VariableKey, scopeId),
                        metadata: binding.Metadata,
                        conversionPlan: binding.ConversionPlan,
                        conversionRequest: binding.ConversionRequest);
            }
            case RuntimeInputBindingSource.ActivityResultMember when binding.ActivityResult is { } memberResult:
            {
                // The producer moves with the placement; the selection is carried unchanged.
                var producerId = placedNodeIds.GetValueOrDefault(memberResult.ProducerExecutableNodeId, memberResult.ProducerExecutableNodeId);
                return StringComparer.Ordinal.Equals(producerId, memberResult.ProducerExecutableNodeId)
                    ? binding
                    : new RuntimeInputBinding(
                        binding.InputName,
                        binding.TargetType,
                        binding.EffectivePolicy,
                        binding.Source,
                        binding.Selection,
                        activityResult: new RuntimeActivityResultReference(producerId, memberResult.ProjectionKey, producerScopeId: null, memberResult.IsOptional),
                        metadata: binding.Metadata,
                        conversionPlan: binding.ConversionPlan,
                        conversionRequest: binding.ConversionRequest);
            }
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
                var producerScopeId = result.ProducerScopeId is { } declaredScope
                    ? placedNodeIds.GetValueOrDefault(declaredScope, declaredScope)
                    : null;
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
