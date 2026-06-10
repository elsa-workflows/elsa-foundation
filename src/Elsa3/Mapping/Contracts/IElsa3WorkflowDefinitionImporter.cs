using Elsa.Workflows.Design.Core.Contracts;
using Elsa3.Models;

namespace Elsa3.Mapping.Contracts;

public interface IElsa3WorkflowDefinitionImporter
{
    ValueTask<Elsa3MigrationResult<IWorkflowDefinitionVersion>> ImportAsync(
        Elsa3WorkflowDefinitionImportInput input,
        CancellationToken cancellationToken = default);

    Elsa3MigrationResult<IWorkflowDefinitionVersion> RejectWorkflowInstanceState(string? sourceName = null);
}
