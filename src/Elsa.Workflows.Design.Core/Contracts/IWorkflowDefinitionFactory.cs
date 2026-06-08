namespace Elsa.Workflows.Design.Core.Contracts;

/// <summary>
/// Constructs new <see cref="IWorkflowDefinition"/> instances (generating the id). The persistence
/// layer turns the result into its concrete entity via the entity's <c>From</c> method.
/// </summary>
public interface IWorkflowDefinitionFactory
{
    IWorkflowDefinition Create(string name, string? description = null, string? id = null);
}
