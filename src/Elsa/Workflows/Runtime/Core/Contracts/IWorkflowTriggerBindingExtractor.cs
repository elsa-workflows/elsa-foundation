using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Extracts the durable trigger index for a published workflow executable (W7, E3-1): it walks the
/// executable's nodes, finds the ones the compiler marked as start-triggers, resolves each one's stimulus
/// identity through the registered <see cref="IActivityTriggerStimulusProvider"/> set, and yields one
/// <see cref="WorkflowTriggerBinding"/> per trigger. Pure and side-effect free — persisting the result is the
/// indexer's job.
/// </summary>
public interface IWorkflowTriggerBindingExtractor
{
    /// <summary>
    /// Returns the trigger bindings for the given published executable. Throws when a node is marked as a
    /// trigger but no provider can describe its stimulus, so an unroutable trigger fails the publish instead of
    /// being silently dropped.
    /// </summary>
    IReadOnlyCollection<WorkflowTriggerBinding> Extract(WorkflowExecutable executable);
}
