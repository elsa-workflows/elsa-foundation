using System.Text.Json;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.Mappings;
using Elsa3.Mapping.Services;
using Elsa3.Models;

namespace Elsa3.Mapping.Conversion;

/// <summary>
/// Converts one Elsa 3 definition: clone → normalize → collect the memory-reference graph and lower value flow → map
/// to Elsa 4 state (spec FR-015, research R-8). Lowering runs once the activity diagnostics are clear, so an unresolved
/// node never yields value-flow noise on top of its own error. Diagnostics are ordered by (JSON path, code).
/// </summary>
internal sealed class Elsa3DefinitionConverter(
    Elsa3ActivityNormalizer normalizer,
    Elsa3MemoryReferenceGraph memoryReferenceGraph,
    Elsa3ValueFlowLowerer valueFlowLowerer,
    Elsa3WorkflowDefinitionToState stateMapper,
    Elsa3ActivityToState activityMapper) : IElsa3DefinitionConverter, IElsa3NodeConverter
{
    public async ValueTask<Elsa3MigrationResult<Elsa3ConvertedDefinition>> ConvertAsync(
        Elsa3ConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Definition.Root is null)
            return Elsa3MigrationResult<Elsa3ConvertedDefinition>.Failure(
                MappingFailed(request, new InvalidOperationException("The definition has no root activity.")));

        try
        {
            var clone = Clone(request.Definition);
            var plan = await normalizer.NormalizeAsync(request, clone, cancellationToken);
            var boundaryOutcomes = request.IsReusable ? Elsa3OutcomeKey.Assemble(plan) : [];
            await LowerAsync(plan, cancellationToken);
            if (plan.HasErrors)
                return Elsa3MigrationResult<Elsa3ConvertedDefinition>.Failure(Ordered(plan.Diagnostics));

            var state = stateMapper.Map(plan);
            // A reusable workflow is a schema-2 graph: Done stays its natural completion (D-5).
            IReadOnlyList<Elsa3GraphOutcomeMapping> outcomeMappings = request.IsReusable
                ? [new Elsa3GraphOutcomeMapping(Elsa3OutcomeKey.DoneKey, Elsa3OutcomeKey.DoneKey)]
                : [];
            return Elsa3MigrationResult<Elsa3ConvertedDefinition>.Success(
                new Elsa3ConvertedDefinition(state, plan.OutputMappings.ToArray(), boundaryOutcomes, outcomeMappings),
                Ordered(plan.Diagnostics));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Elsa3MigrationResult<Elsa3ConvertedDefinition>.Failure(MappingFailed(request, exception));
        }
    }

    public async ValueTask<Elsa3MigrationResult<ActivityNode>> ConvertNodeAsync(
        Elsa3NodeConversionContext context,
        Elsa3Activity node,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);
        try
        {
            var definition = Clone(context.Request.Definition);
            definition.Root = JsonSerializer.SerializeToElement(node).Deserialize<Elsa3Activity>()
                              ?? throw new JsonException("Elsa 3 activity clone produced no value.");
            var plan = await normalizer.NormalizeAsync(context.Request, definition, cancellationToken);
            if (plan.HasErrors)
                return Elsa3MigrationResult<ActivityNode>.Failure(Ordered(plan.Diagnostics));

            var mapped = activityMapper.Map(plan, definition.Root);
            return mapped is null
                ? Elsa3MigrationResult<ActivityNode>.Failure(MappingFailed(context.Request, new InvalidOperationException("The node produced no Elsa 4 activity.")))
                : Elsa3MigrationResult<ActivityNode>.Success(mapped, Ordered(plan.Diagnostics));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Elsa3MigrationResult<ActivityNode>.Failure(MappingFailed(context.Request, exception));
        }
    }

    private async ValueTask LowerAsync(Elsa3ConversionPlan plan, CancellationToken cancellationToken)
    {
        if (plan.HasErrors || plan.Definition.Root is null)
            return;

        var jsonPaths = plan.JsonPaths;
        var inventory = await memoryReferenceGraph.CollectAsync(
            plan.Definition.Root, node => ContractOf(plan, node), jsonPaths, cancellationToken, plan.SourceName);
        plan.Diagnostics.AddRange(valueFlowLowerer.Lower(
            plan.Definition,
            inventory,
            plan.Request.DiagnosticMetadata,
            jsonPaths,
            plan.SourceName,
            (node, property) => plan.Find(node)?.ScriptProperty is { } script && StringComparer.Ordinal.Equals(script, property),
            plan.Nodes.Values.SelectMany(node => node.Variables).Select(variable => variable.ReferenceKey).ToHashSet(StringComparer.Ordinal),
            plan.MemoryAliases));
    }

    /// <summary>The node's declared contract, plus its Elsa 4 loop variables as outputs so lowering can resolve them.</summary>
    private static Elsa3NodeContract? ContractOf(Elsa3ConversionPlan plan, Elsa3Activity node)
    {
        if (plan.Find(node) is not { Resolved: { } resolved } nodePlan)
            return null;
        if (nodePlan.LoopVariables.Count == 0)
            return new Elsa3NodeContract(resolved.Inputs, resolved.Outputs);

        var loopOutputs = nodePlan.LoopVariables.Select(item => new Elsa.Activities.Design.Core.Models.OutputDefinition(
            item.Value, item.Key, new Elsa.Primitives.Models.TypeReference("Object"), null, item.Value, null, true));
        return new Elsa3NodeContract(resolved.Inputs, resolved.Outputs.Concat(loopOutputs).ToArray());
    }

    private static Elsa3WorkflowDefinition Clone(Elsa3WorkflowDefinition definition) =>
        // Never mutate the caller's Elsa 3 DTO. The clone is an importer-only transition object; successful mapping
        // publishes only ordinary Elsa 4 authored state.
        JsonSerializer.SerializeToElement(definition).Deserialize<Elsa3WorkflowDefinition>()
        ?? throw new JsonException("Elsa 3 workflow definition clone produced no value.");

    private static IReadOnlyList<Elsa3MigrationDiagnostic> Ordered(IEnumerable<Elsa3MigrationDiagnostic> diagnostics) =>
        diagnostics
            .OrderBy(diagnostic => diagnostic.Path ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ToArray();

    /// <summary>The last-resort wrapper for an unexpected exception; input never reaches it (research R-3).</summary>
    private static Elsa3MigrationDiagnostic MappingFailed(Elsa3ConversionRequest request, Exception exception)
    {
        var definitionId = string.IsNullOrWhiteSpace(request.Definition.DefinitionId) ? "<unspecified>" : request.Definition.DefinitionId;
        var metadata = request.DiagnosticMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(request.Definition.DefinitionId))
            metadata["DefinitionId"] = request.Definition.DefinitionId;
        return new Elsa3MigrationDiagnostic(
            Elsa3MigrationDiagnosticSeverity.Error,
            Elsa3MigrationDiagnosticCodes.DefinitionMappingFailed,
            $"Elsa 3 workflow definition '{definitionId}' could not be mapped: {exception.Message}",
            guidance: "Review the Elsa 3 definition and installed Elsa 4 activity catalog before retrying import.",
            metadata: metadata);
    }
}
