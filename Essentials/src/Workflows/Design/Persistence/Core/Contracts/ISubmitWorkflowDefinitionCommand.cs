using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

public interface ISubmitWorkflowDefinitionCommand
{
    /// <summary>
    /// Creates a definition, draft and initial immutable version atomically. The composed draft validation gate
    /// must accept the state before it is stored; warnings do not block. A validation refusal leaves no authored
    /// rows or successful operation marker, and a replay of a committed submission returns its original result.
    /// </summary>
    /// <exception cref="Exceptions.DraftHasValidationErrorsException">The draft has blocking validation errors.</exception>
    Task<SubmittedWorkflowDefinition> Execute(
        DesignOperationKey operationKey,
        string name,
        string? description,
        WorkflowDefinitionState state,
        CancellationToken cancellationToken = default);
}
