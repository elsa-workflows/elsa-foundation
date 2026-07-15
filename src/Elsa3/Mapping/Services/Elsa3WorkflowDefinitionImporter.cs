using Elsa.Workflows.Design.Core.Contracts;
using Elsa3.Mapping.Contracts;
using Elsa3.Mapping.Mappings;
using Elsa3.Models;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;

namespace Elsa3.Mapping.Services;

public sealed class Elsa3WorkflowDefinitionImporter(
    Elsa3WorkflowDefinitionToWorkflowDefinitionVersion mapper,
    IReusableActivityCollectionImporter? reusableActivities = null) : IElsa3WorkflowDefinitionImporter
{
    public async ValueTask<Elsa3MigrationResult<IWorkflowDefinitionVersion>> ImportAsync(
        Elsa3WorkflowDefinitionImportInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.Definition.Options?.UsableAsActivity == true)
        {
            return Elsa3MigrationResult<IWorkflowDefinitionVersion>.Failure(new Elsa3MigrationDiagnostic(
                Elsa3MigrationDiagnosticSeverity.Error,
                ReusableActivityImportDiagnosticCodes.SelectionInvalid,
                $"Elsa 3 reusable workflow '{input.Definition.DefinitionId}' requires collection-aware plan/apply import.",
                guidance: "Analyze the full reusable workflow collection so exact references, cycles, and direct-start wrappers can be reviewed before application.",
                metadata: BuildDiagnosticMetadata(input)));
        }

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

    public ValueTask<ReusableActivityImportPlan> AnalyzeReusableCollectionAsync(
        ReusableActivityImportCollection collection,
        CancellationToken cancellationToken = default) =>
        RequireReusableImporter().AnalyzeAsync(collection, cancellationToken);

    public ValueTask<ReusableActivityImportApplyResult> ApplyReusableCollectionAsync(
        ReusableActivityImportApplyRequest request,
        CancellationToken cancellationToken = default) =>
        RequireReusableImporter().ApplyAsync(request, cancellationToken);

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

    private IReusableActivityCollectionImporter RequireReusableImporter() =>
        reusableActivities ?? throw new InvalidOperationException(
            "Elsa 3 reusable collection import is not configured. Enable Elsa3ImportJsonActivities and an IReusableActivityImportCommand adapter.");
}
