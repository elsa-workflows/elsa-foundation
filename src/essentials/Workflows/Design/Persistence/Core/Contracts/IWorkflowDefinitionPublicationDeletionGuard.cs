namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// The publication check specifically. Permanent deletion may only proceed in a host that composes one, because
/// the design lane cannot see publication state itself: publication documents are durable and another node can
/// hold a live publication against the same design catalog, so a host without this guard would be deleting rows
/// it cannot prove are unreferenced. Absence is therefore a refusal, not a permission.
/// </summary>
/// <remarks>
/// This is a distinct contract rather than "any registered <see cref="IWorkflowDefinitionPermanentDeletionGuard"/>"
/// because the two mean different things. The base contract is an open-ended veto list any vertical may extend;
/// this one names the single check permanent deletion depends on. Keying the refusal on the base list being
/// non-empty would silently grant permission the day an unrelated vertical contributes its own veto.
/// </remarks>
public interface IWorkflowDefinitionPublicationDeletionGuard : IWorkflowDefinitionPermanentDeletionGuard;
