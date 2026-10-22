using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;

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
        ValueTypeDescriptor Type,
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
                    new ValueTypeDescriptor(definition.Type.Alias, definition.Type.CollectionKind),
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
                    projection.Type,
                    projection.Policy.StorageProfile ?? WellKnownRuntimeDurableValueStorageDrivers.Json,
                    projection.SourceRepresentation))
                .ToArray(),
            authoredOutputs,
            workflowVariables);

    /// <summary>
    /// Compiles the authored outputs of a node inside a closed-scope graph body (spec 265 D-2) into captures that write
    /// the graph's own variables. Inside a graph body the root scope is the graph, so the authored target is a
    /// workflow-scope <c>Variable</c> reference (or Studio's legacy <c>activity-graph</c> scope) naming one of
    /// <paramref name="graphVariables"/>. The capture is pinned to that variable's declared type and storage driver, so
    /// the row it writes is the row the graph boundary itself owns, and it names the placed boundary it writes into.
    /// </summary>
    internal IReadOnlyDictionary<string, RuntimeOutputCapture> CompileGraphVariableOutputs(
        string nodeId,
        IReadOnlyCollection<ActivityResultProjectionContract> projections,
        IEnumerable<ArgumentState> authoredOutputs,
        IReadOnlyCollection<GraphVariableCaptureTarget> graphVariables,
        string graphScopeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphScopeId);
        var projectionsByKey = projections.ToDictionary(x => x.Key, StringComparer.Ordinal);
        var variablesByReferenceKey = graphVariables.ToDictionary(x => x.ReferenceKey, StringComparer.Ordinal);
        var authored = authoredOutputs.ToArray();
        var duplicate = authored.GroupBy(x => x.ReferenceKey, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Activity node '{nodeId}' declares output '{duplicate.Key}' more than once.");

        var captures = new Dictionary<string, RuntimeOutputCapture>(StringComparer.Ordinal);
        foreach (var output in authored.OrderBy(x => x.ReferenceKey, StringComparer.Ordinal))
        {
            if (!projectionsByKey.TryGetValue(output.ReferenceKey, out var projection))
                throw new ArgumentException($"Activity node '{nodeId}' output '{output.ReferenceKey}' does not match the published activity contract.");
            if (!StringComparer.OrdinalIgnoreCase.Equals(output.Value.ExpressionType, VariableExpressionType) ||
                !VariableReference.TryParse(output.Value.Value, out var target) || target is null)
                throw new ArgumentException($"Activity node '{nodeId}' output '{output.ReferenceKey}' must target a declared graph variable using a Variable reference.");
            if (!target.IsWorkflowScope && !StringComparer.Ordinal.Equals(target.DeclaringScopeId, PlacedReferenceRemapper.LegacyGraphScopeId))
                throw new ArgumentException($"Activity node '{nodeId}' output '{output.ReferenceKey}' targets scope '{target.DeclaringScopeId}'; a graph body can capture only into its own variables.");
            if (!variablesByReferenceKey.TryGetValue(target.ReferenceKey, out var variable))
                throw new ArgumentException($"Activity node '{nodeId}' output '{output.ReferenceKey}' targets unknown graph variable '{target.ReferenceKey}'.");

            storageDrivers.GetRequired(variable.StorageDriverKey);
            var sourceType = new ValueTypeDescriptor(projection.Type.Alias, projection.Type.CollectionKind);
            var sourceRepresentation = projection.SourceRepresentation ?? ValueRepresentationDefaults.Infer(sourceType);
            if (sourceRepresentation == ValueRepresentation.TransientResource)
                throw new ArgumentException(
                    $"VF-ACT-005: Activity node '{nodeId}' output '{output.ReferenceKey}' has source representation '{ValueRepresentation.TransientResource}' and cannot be captured into graph variable '{variable.Name}'.");

            var alias = variable.Type.ValueKind == System.Text.Json.JsonValueKind.Object &&
                        variable.Type.TryGetProperty("alias", out var aliasElement)
                ? aliasElement.GetString() ?? "object"
                : "object";
            var conversionPlan = output.Conversion is null
                ? null
                : resolvedConversionPlanResolver.Resolve(
                    sourceType,
                    sourceRepresentation,
                    new ValueTypeDescriptor(alias),
                    AuthoredValueConversionMapper.Mode(output.Conversion),
                    AuthoredValueConversionMapper.Profile(output.Conversion),
                    AuthoredValueConversionMapper.Limits(output.Conversion),
                    AuthoredValueConversionMapper.Options(output.Conversion),
                    new ValueConversionBindingContext(nodeId, output.ReferenceKey, ValueConversionBindingKind.Output));
            captures.Add(output.ReferenceKey, new RuntimeOutputCapture(
                output.ReferenceKey,
                $"{GraphVariableValueIdPrefix}{variable.ReferenceKey}",
                new RuntimeValueTypeDescriptor(alias, variable.StorageDriverKey, variable.Type.Clone()),
                DurableValueLifecycle.Instance,
                DurableValueStorage.Custom,
                captureOnSuccessfulCompletion: true,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["referenceKey"] = output.ReferenceKey,
                    [RuntimeMetadataKeys.TargetVariableReferenceKey] = variable.ReferenceKey,
                    [RuntimeMetadataKeys.VariableName] = variable.Name,
                    [RuntimeMetadataKeys.StorageDriverKey] = variable.StorageDriverKey,
                    [RuntimeMetadataKeys.CaptureTargetScopeId] = graphScopeId
                },
                variable.StorageDriverKey,
                conversionPlan));
        }

        return captures;
    }

    /// <summary>Value id prefix of a capture into a graph variable; the runtime writes the boundary's own row.</summary>
    internal const string GraphVariableValueIdPrefix = "graph-variable:";

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
                // Requiredness governs the value the activity must produce, not whether its caller captures
                // that value. Result projection and reusable boundary completion enforce the source contract.
                continue;

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
            captures.Add(definition.ReferenceKey, new RuntimeOutputCapture(
                definition.ReferenceKey,
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

/// <summary>A graph variable a body node can capture into (spec 265): its declared type JSON and storage driver.</summary>
internal sealed record GraphVariableCaptureTarget(
    string ReferenceKey,
    string Name,
    System.Text.Json.JsonElement Type,
    string StorageDriverKey);
