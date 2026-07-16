using System.Text.Json;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa3.Mapping.Contracts;
using Elsa3.Mapping.Mappings;
using Elsa3.Models;

namespace Elsa3.Mapping.Services;

public sealed class Elsa3WorkflowDefinitionImporter : IElsa3WorkflowDefinitionImporter
{
    private readonly Elsa3WorkflowDefinitionToWorkflowDefinitionVersion _mapper;
    private readonly Elsa3MemoryReferenceGraph _memoryReferenceGraph;
    private readonly Elsa3ValueFlowLowerer _valueFlowLowerer;

    public Elsa3WorkflowDefinitionImporter(
        Elsa3WorkflowDefinitionToWorkflowDefinitionVersion mapper,
        Elsa3MemoryReferenceGraph memoryReferenceGraph,
        Elsa3ValueFlowLowerer valueFlowLowerer)
    {
        _mapper = mapper;
        _memoryReferenceGraph = memoryReferenceGraph;
        _valueFlowLowerer = valueFlowLowerer;
    }

    public async ValueTask<Elsa3MigrationResult<IWorkflowDefinitionVersion>> ImportAsync(
        Elsa3WorkflowDefinitionImportInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        try
        {
            // Never mutate the caller's Elsa 3 DTO. The clone is an importer-only transition object;
            // successful mapping publishes only ordinary Elsa 4 authored state.
            var definition = JsonSerializer.SerializeToElement(input.Definition)
                                 .Deserialize<Elsa3WorkflowDefinition>()
                             ?? throw new JsonException("Elsa 3 workflow definition clone produced no value.");
            var inventory = await _memoryReferenceGraph.CollectAsync(definition.Root, cancellationToken);
            var diagnostics = _valueFlowLowerer.Lower(definition, inventory, input);
            if (diagnostics.Any(diagnostic => diagnostic.IsError))
                return Elsa3MigrationResult<IWorkflowDefinitionVersion>.Failure(diagnostics);

            var version = await _mapper.Map(definition, cancellationToken);
            return Elsa3MigrationResult<IWorkflowDefinitionVersion>.Success(version);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var definitionId = string.IsNullOrWhiteSpace(input.Definition.DefinitionId)
                ? "<unspecified>"
                : input.Definition.DefinitionId;
            var metadata = BuildDiagnosticMetadata(input);

            if (!string.IsNullOrWhiteSpace(input.Definition.DefinitionId))
                metadata["DefinitionId"] = input.Definition.DefinitionId;

            return Elsa3MigrationResult<IWorkflowDefinitionVersion>.Failure(new Elsa3MigrationDiagnostic(
                Elsa3MigrationDiagnosticSeverity.Error,
                Elsa3MigrationDiagnosticCodes.DefinitionMappingFailed,
                $"Elsa 3 workflow definition '{definitionId}' could not be mapped: {exception.Message}",
                guidance: "Review the Elsa 3 definition and installed Elsa 4 activity catalog before retrying import.",
                metadata: metadata));
        }
    }

    public Elsa3MigrationResult<IWorkflowDefinitionVersion> RejectUnsupportedInputKind(Elsa3MigrationInputKind inputKind, string? sourceName = null) =>
        Elsa3MigrationCompatibility.IsLiveInstanceStateInput(inputKind)
            ? RejectWorkflowInstanceState(sourceName)
            : Elsa3MigrationCompatibility.RejectUnsupportedInputKind<IWorkflowDefinitionVersion>(inputKind, sourceName);

    public Elsa3MigrationResult<IWorkflowDefinitionVersion> RejectWorkflowInstanceState(string? sourceName = null)
    {
        if (sourceName is not null && string.IsNullOrWhiteSpace(sourceName))
            throw new ArgumentException("Migration source name cannot be blank.", nameof(sourceName));

        var metadata = new Dictionary<string, string>
        {
            ["InputKind"] = Elsa3MigrationInputKind.WorkflowInstanceState.ToString(),
        };
        if (sourceName is not null)
            metadata["SourceName"] = sourceName;

        return Elsa3MigrationResult<IWorkflowDefinitionVersion>.Failure(new Elsa3MigrationDiagnostic(
            Elsa3MigrationDiagnosticSeverity.Error,
            Elsa3ImportDiagnosticCodes.LiveInstanceState,
            "Elsa 3 live workflow-instance state cannot be supplied to authored definition import.",
            guidance: "Drain Elsa 3 instances or use a separately governed live-state migration tool; definition import creates native Elsa 4 authored state only.",
            metadata: metadata));
    }

    private static Dictionary<string, string> BuildDiagnosticMetadata(Elsa3WorkflowDefinitionImportInput input)
    {
        var metadata = input.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

        metadata["InputKind"] = input.InputKind.ToString();

        if (input.SourceName is not null)
            metadata["SourceName"] = input.SourceName;

        return metadata;
    }
}
