using Elsa.Workflows.Runtime.Core.Exceptions;
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
    /// Evaluates the complete non-persisted trigger preflight outcome for <paramref name="executable"/>.
    /// Existing extractor implementations receive a binding-only compatibility projection; implementations that
    /// can observe provider recognition should override this member to return complete node outcomes.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="executable"/> is <see langword="null"/>.</exception>
    /// <exception cref="WorkflowTriggerPreflightException">A classified trigger cannot be safely materialized.</exception>
    WorkflowTriggerPreflightOutcome Evaluate(WorkflowExecutable executable)
    {
        ArgumentNullException.ThrowIfNull(executable);
        var bindings = Extract(executable);
        var providerId = $"legacy-extractor:{GetType().FullName ?? GetType().Name}";
        var nodeOutcomes = bindings
            .GroupBy(x => x.ExecutableNodeId, StringComparer.Ordinal)
            .Select(group => new WorkflowTriggerNodePreflightOutcome(
                group.Key,
                group.First().StimulusType,
                providerId,
                WorkflowTriggerPreflightStatus.Registered,
                group.ToArray()))
            .ToArray();
        return new WorkflowTriggerPreflightOutcome(executable.Identity, nodeOutcomes);
    }

    /// <summary>
    /// Returns the trigger bindings for the given published executable. Throws when a node is marked as a
    /// trigger but no provider can describe its stimulus, so an unroutable trigger fails the publish instead of
    /// being silently dropped.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="executable"/> is <see langword="null"/>.</exception>
    /// <exception cref="WorkflowTriggerExtractionException">A classified trigger cannot be extracted.</exception>
    /// <exception cref="WorkflowTriggerPreflightException">A classified trigger cannot be safely materialized.</exception>
    IReadOnlyCollection<WorkflowTriggerBinding> Extract(WorkflowExecutable executable);
}
