namespace Elsa.Workflows.Design.Core.Contracts;

/// <summary>
/// Constructs new <see cref="IWorkflowDefinition"/> instances (generating the id). The persistence
/// layer turns the result into its concrete entity via the entity's <c>From</c> method.
/// </summary>
public interface IWorkflowDefinitionFactory
{
    /// <param name="deleted">
    /// When <c>true</c>, the produced read-model carries a non-null <c>DeletedAt</c> so a
    /// source-driven soft-delete (spec 085 FR-008) propagates through the reconciler's create path.
    /// </param>
    IWorkflowDefinition Create(string name, string? description = null, string? id = null, bool deleted = false);
}
