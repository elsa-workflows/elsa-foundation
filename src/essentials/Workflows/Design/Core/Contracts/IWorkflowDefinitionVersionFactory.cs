using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Contracts;

/// <summary>
/// Constructs new <see cref="IWorkflowDefinitionVersion"/> instances (generating the id). The
/// persistence layer turns the result into its concrete entity via the entity's <c>From</c> method.
/// </summary>
public interface IWorkflowDefinitionVersionFactory
{
    IWorkflowDefinitionVersion Create(
        IWorkflowDefinition definition,
        string version,
        WorkflowDefinitionState state,
        DateTimeOffset? sourceCreatedAt = null,
        string? id = null);
}
