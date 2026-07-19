using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Workflows.Publishing.Api.Services;

/// <summary>
/// Compiles an authored reusable-activity output binding into the ordinary Runtime durable-output capture
/// contract. Only an explicit workflow-scope <c>Variable</c> reference is accepted: container targets cannot be
/// resolved to one concrete execution during publication, and treating arbitrary authored values as durable ids
/// would guess at target semantics.
/// </summary>
public sealed class RuntimeOutputCaptureCompiler(
    IRuntimeDurableValueStorageDriverRegistry storageDrivers,
    ValueConversionPlanResolver? conversionPlanResolver = null,
    IWellKnownTypeRegistry? wellKnownTypeRegistry = null)
{
    private const string VariableExpressionType = "Variable";

    private readonly ValueConversionPlanResolver resolvedConversionPlanResolver = conversionPlanResolver ?? new(wellKnownTypeRegistry: wellKnownTypeRegistry);

    public IReadOnlyDictionary<string, RuntimeOutputCapture> CompileBoundaryOutputs(
        string nodeId,
        IReadOnlyCollection<ActivityOutputContract> definitions,
        IEnumerable<ArgumentState> authoredOutputs,
        IEnumerable<VariableDefinition> workflowVariables)
    {
        var definitionsByReferenceKey = definitions.ToDictionary(x => x.ReferenceKey, StringComparer.Ordinal);
        var authored = authoredOutputs.ToArray();
        var duplicate = authored.GroupBy(x => x.ReferenceKey, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Activity node '{nodeId}' declares output '{duplicate.Key}' more than once.");

        foreach (var output in authored)
            if (!definitionsByReferenceKey.ContainsKey(output.ReferenceKey))
                throw new ArgumentException($"Activity node '{nodeId}' output '{output.ReferenceKey}' does not match the published activity contract.");

        var variables = workflowVariables.ToArray();
        var duplicateVariable = variables.GroupBy(x => x.ReferenceKey, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1);
        if (duplicateVariable is not null)
            throw new ArgumentException($"Workflow declares variable reference key '{duplicateVariable.Key}' more than once.");
        var variablesByReferenceKey = variables.ToDictionary(x => x.ReferenceKey, StringComparer.Ordinal);
        var authoredByReferenceKey = authored.ToDictionary(x => x.ReferenceKey, StringComparer.Ordinal);
        var captures = new Dictionary<string, RuntimeOutputCapture>(StringComparer.Ordinal);

        foreach (var definition in definitions.OrderBy(x => x.ReferenceKey, StringComparer.Ordinal))
        {
            if (!authoredByReferenceKey.TryGetValue(definition.ReferenceKey, out var output))
            {
                if (definition.IsRequired)
                    throw new ArgumentException($"Activity node '{nodeId}' is missing required output target '{definition.ReferenceKey}'.");
                continue;
            }

            if (!StringComparer.OrdinalIgnoreCase.Equals(output.Value.ExpressionType, VariableExpressionType) ||
                !VariableReference.TryParse(output.Value.Value, out var target) || target is null)
            {
                throw new ArgumentException(
                    $"Activity node '{nodeId}' output '{definition.ReferenceKey}' must target a declared workflow variable using a Variable reference.");
            }

            if (!target.IsWorkflowScope)
            {
                throw new ArgumentException(
                    $"Activity node '{nodeId}' output '{definition.ReferenceKey}' targets non-workflow scope '{target.DeclaringScopeId}', which cannot be resolved to one durable execution target during publication.");
            }

            if (!variablesByReferenceKey.TryGetValue(target.ReferenceKey, out var variable))
            {
                throw new ArgumentException(
                    $"Activity node '{nodeId}' output '{definition.ReferenceKey}' targets unknown workflow variable '{target.ReferenceKey}'.");
            }

            storageDrivers.GetRequired(definition.StorageDriverKey);
            var sourceType = new ValueTypeDescriptor(definition.Type.Alias, definition.Type.CollectionKind);
            var targetType = new ValueTypeDescriptor(variable.Type.Alias, variable.Type.CollectionKind);
            var conversionPlan = resolvedConversionPlanResolver.Resolve(
                sourceType,
                definition.SourceRepresentation ?? ValueRepresentationDefaults.Infer(sourceType),
                targetType);
            var type = new RuntimeValueTypeDescriptor(
                variable.Type.Alias,
                definition.StorageDriverKey,
                System.Text.Json.JsonSerializer.SerializeToElement(variable.Type));
            captures.Add(definition.Name, new RuntimeOutputCapture(
                definition.Name,
                $"{RuntimeWorkflowStateSeed.VariableValueIdPrefix}{variable.Name}",
                type,
                DurableValueLifecycle.Instance,
                DurableValueStorage.Custom,
                captureOnSuccessfulCompletion: true,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["referenceKey"] = definition.ReferenceKey,
                    ["targetVariableReferenceKey"] = variable.ReferenceKey,
                    [RuntimeMetadataKeys.VariableName] = variable.Name,
                    [RuntimeMetadataKeys.StorageDriverKey] = definition.StorageDriverKey
                },
                definition.StorageDriverKey,
                conversionPlan));
        }

        return captures;
    }

}
