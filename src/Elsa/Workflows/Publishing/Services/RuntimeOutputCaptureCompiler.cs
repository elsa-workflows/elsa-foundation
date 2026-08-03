using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Compiles an authored activity output binding into the ordinary Runtime durable-output capture contract. Only
/// an explicit workflow-scope <c>Variable</c> reference is accepted: container targets cannot be resolved to one
/// concrete execution during publication, and treating arbitrary authored values as durable ids would guess at
/// target semantics. The same validation and conversion body serves both reusable-activity boundary outputs
/// (<see cref="CompileBoundaryOutputs"/>) and ordinary leaf-activity result projections
/// (<see cref="CompileResultProjectionOutputs"/>); the two entry points only normalize their contract shape.
/// </summary>
public sealed class RuntimeOutputCaptureCompiler(
    IRuntimeDurableValueStorageDriverRegistry storageDrivers,
    ValueConversionPlanResolver? conversionPlanResolver = null,
    IWellKnownTypeRegistry? wellKnownTypeRegistry = null)
{
    private const string VariableExpressionType = "Variable";

    private readonly ValueConversionPlanResolver resolvedConversionPlanResolver = conversionPlanResolver ?? new(wellKnownTypeRegistry: wellKnownTypeRegistry);

    /// <summary>The minimal output shape the capture body reads, factored so boundary and leaf contracts share it.</summary>
    private sealed record OutputCaptureTarget(
        string ReferenceKey,
        string Name,
        ValueTypeDescriptor Type,
        bool IsRequired,
        string StorageDriverKey,
        ValueRepresentation? SourceRepresentation);

    /// <summary>
    /// Compiles the authored outputs of a reusable-activity boundary node against its published
    /// <see cref="ActivityOutputContract"/> definitions.
    /// </summary>
    public IReadOnlyDictionary<string, RuntimeOutputCapture> CompileBoundaryOutputs(
        string nodeId,
        IReadOnlyCollection<ActivityOutputContract> definitions,
        IEnumerable<ArgumentState> authoredOutputs,
        IEnumerable<VariableDefinition> workflowVariables) =>
        Compile(
            nodeId,
            definitions
                .Select(definition => new OutputCaptureTarget(
                    definition.ReferenceKey,
                    definition.Name,
                    new ValueTypeDescriptor(definition.Type.Alias, definition.Type.CollectionKind),
                    definition.IsRequired,
                    definition.StorageDriverKey,
                    definition.SourceRepresentation))
                .ToArray(),
            authoredOutputs,
            workflowVariables);

    /// <summary>
    /// Compiles the authored outputs of an ordinary leaf activity node against its typed result-projection
    /// contracts. A projection carries no explicit boundary storage driver, so an authored external storage
    /// profile is honored and otherwise the durable value defaults to the JSON driver.
    /// </summary>
    public IReadOnlyDictionary<string, RuntimeOutputCapture> CompileResultProjectionOutputs(
        string nodeId,
        IReadOnlyCollection<ActivityResultProjectionContract> projections,
        IEnumerable<ArgumentState> authoredOutputs,
        IEnumerable<VariableDefinition> workflowVariables) =>
        Compile(
            nodeId,
            projections
                .Select(projection => new OutputCaptureTarget(
                    projection.Key,
                    projection.Key,
                    projection.Type,
                    projection.IsRequired,
                    projection.Policy.StorageProfile ?? WellKnownRuntimeDurableValueStorageDrivers.Json,
                    projection.SourceRepresentation))
                .ToArray(),
            authoredOutputs,
            workflowVariables);

    private IReadOnlyDictionary<string, RuntimeOutputCapture> Compile(
        string nodeId,
        IReadOnlyCollection<OutputCaptureTarget> definitions,
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
            var sourceRepresentation = definition.SourceRepresentation ?? ValueRepresentationDefaults.Infer(sourceType);
            if (sourceRepresentation == ValueRepresentation.TransientResource)
            {
                throw new ArgumentException(
                    $"VF-ACT-005: Activity node '{nodeId}' output '{definition.ReferenceKey}' has source representation '{ValueRepresentation.TransientResource}' " +
                    $"and cannot be captured into durable workflow variable destination storage policy '{DurableValueStorage.Custom}' using driver '{definition.StorageDriverKey}'. " +
                    "Use an execution-local activity-result binding for live resources, or model an explicit DurableReference/resource-handle output before persisting.");
            }

            var targetType = new ValueTypeDescriptor(variable.Type.Alias, variable.Type.CollectionKind);
            var conversionPlan = output.Conversion is null
                ? null
                : resolvedConversionPlanResolver.Resolve(
                    sourceType,
                    sourceRepresentation,
                    targetType,
                    AuthoredValueConversionMapper.Mode(output.Conversion),
                    AuthoredValueConversionMapper.Profile(output.Conversion),
                    AuthoredValueConversionMapper.Limits(output.Conversion),
                    AuthoredValueConversionMapper.Options(output.Conversion),
                    new ValueConversionBindingContext(nodeId, definition.ReferenceKey, ValueConversionBindingKind.Output));
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
                    [RuntimeMetadataKeys.TargetVariableReferenceKey] = variable.ReferenceKey,
                    [RuntimeMetadataKeys.VariableName] = variable.Name,
                    [RuntimeMetadataKeys.StorageDriverKey] = definition.StorageDriverKey
                },
                definition.StorageDriverKey,
                conversionPlan));
        }

        return captures;
    }

}
