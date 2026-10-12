using Elsa.Workflows.Design.Core.Contracts;
using Elsa3.Models;
using Elsa3.Activities.Design.Import.Models;

namespace Elsa3.Mapping.Contracts;

public interface IElsa3WorkflowDefinitionImporter
{
    ValueTask<Elsa3MigrationResult<IWorkflowDefinitionVersion>> ImportAsync(
        Elsa3WorkflowDefinitionImportInput input,
        CancellationToken cancellationToken = default);

    ValueTask<ReusableActivityImportPlan> AnalyzeReusableCollectionAsync(
        ReusableActivityImportCollection collection,
        CancellationToken cancellationToken = default);

    ValueTask<ReusableActivityImportApplyResult> ApplyReusableCollectionAsync(
        ReusableActivityImportApplyRequest request,
        CancellationToken cancellationToken = default);

    Elsa3MigrationResult<IWorkflowDefinitionVersion> RejectUnsupportedInputKind(Elsa3MigrationInputKind inputKind, string? sourceName = null);

    Elsa3MigrationResult<IWorkflowDefinitionVersion> RejectWorkflowInstanceState(string? sourceName = null);
}
