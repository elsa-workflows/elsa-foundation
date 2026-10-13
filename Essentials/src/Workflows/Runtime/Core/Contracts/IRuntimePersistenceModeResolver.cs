using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Declares <see cref="IRuntimePersistenceModeResolver"/> as a single-implementation replacement contract.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class RuntimePersistenceModeResolverReplacementContractAttribute : Attribute;

/// <summary>
/// Resolves the persistence mode of a workflow execution. The runtime reads the workflow's chosen mode off the pinned
/// executable and applies it over the host default; it never reads a Design store.
/// </summary>
/// <remarks>
/// This is a <b>replacement contract</b>, declared by
/// <see cref="RuntimePersistenceModeResolverReplacementContractAttribute"/>: at most one implementation per container.
/// The runtime registers a scoped default with <c>TryAdd</c>, so a host replaces it with <c>services.Replace(...)</c> or
/// by registering its own first. A host that composes more than one does not start: the persistence modes' startup check
/// fails shell activation, naming every registration. A replacement must give every drain of a run the same mode: the
/// mode recorded on the run once it has a record, and before that the mode of the start the scheduler queue holds.
/// </remarks>
[RuntimePersistenceModeResolverReplacementContract]
public interface IRuntimePersistenceModeResolver
{
    /// <summary>
    /// Resolves the mode for the run the <paramref name="envelope"/> targets, whichever command kind it carries: the
    /// mode the run recorded at its first commit, or before that the mode its start command carries, read off the start
    /// the scheduler queue holds (the start's first delivery) rather than off a start delivered again. Also reports
    /// whether the run already has a durable record. Throws when the run has a record or a queued start but its mode
    /// cannot be determined, rather than drain the run in a mode it did not choose.
    /// </summary>
    ValueTask<ResolvedPersistenceMode> ResolveAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a run's mode from its workflow: the workflow's chosen mode over the host default. The start dispatcher
    /// uses it to decide the mode a run is started in.
    /// </summary>
    PersistenceMode Resolve(WorkflowExecutable executable);
}
