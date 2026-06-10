using Elsa.Workflows.Design.Core.Contracts;
using Elsa3.Mapping.Contracts;
using Elsa3.Mapping.Mappings;
using Elsa3.Models;

namespace Elsa3.Mapping.Services;

public sealed class Elsa3WorkflowDefinitionImporter(Elsa3WorkflowDefinitionToWorkflowDefinitionVersion mapper) : IElsa3WorkflowDefinitionImporter
{
    public async ValueTask<Elsa3MigrationResult<IWorkflowDefinitionVersion>> ImportAsync(
        Elsa3WorkflowDefinitionImportInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        try
        {
            var version = await mapper.Map(input.Definition, cancellationToken);
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
        Elsa3MigrationCompatibility.RejectUnsupportedInputKind<IWorkflowDefinitionVersion>(inputKind, sourceName);

    public Elsa3MigrationResult<IWorkflowDefinitionVersion> RejectWorkflowInstanceState(string? sourceName = null) =>
        Elsa3MigrationCompatibility.RejectLiveInstanceResume<IWorkflowDefinitionVersion>(sourceName);

    private static Dictionary<string, string> BuildDiagnosticMetadata(Elsa3WorkflowDefinitionImportInput input)
    {
        var metadata = input.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

        metadata["InputKind"] = input.InputKind.ToString();

        if (input.SourceName is not null)
            metadata["SourceName"] = input.SourceName;

        return metadata;
    }
}
