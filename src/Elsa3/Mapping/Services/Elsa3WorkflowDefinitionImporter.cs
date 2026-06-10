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
            return Elsa3MigrationResult<IWorkflowDefinitionVersion>.Failure(new Elsa3MigrationDiagnostic(
                Elsa3MigrationDiagnosticSeverity.Error,
                Elsa3MigrationDiagnosticCodes.DefinitionMappingFailed,
                $"Elsa 3 workflow definition '{input.Definition.DefinitionId}' could not be mapped: {exception.Message}",
                guidance: "Review the Elsa 3 definition and installed Elsa 4 activity catalog before retrying import.",
                metadata: new Dictionary<string, string>
                {
                    ["InputKind"] = input.InputKind.ToString(),
                    ["DefinitionId"] = input.Definition.DefinitionId
                }));
        }
    }

    public Elsa3MigrationResult<IWorkflowDefinitionVersion> RejectWorkflowInstanceState(string? sourceName = null) =>
        Elsa3MigrationCompatibility.RejectLiveInstanceResume<IWorkflowDefinitionVersion>(sourceName);
}
