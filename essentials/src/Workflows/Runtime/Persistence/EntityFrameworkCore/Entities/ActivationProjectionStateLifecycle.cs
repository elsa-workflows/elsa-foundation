using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

/// <summary>
/// How the EF projection stores read an activation's lifecycle off a <see cref="WorkflowTriggerBindingProjectionStateEntity"/>
/// or a <see cref="RecurringTriggerScheduleProjectionStateEntity"/>, neither of which has a lifecycle column (#2193).
/// </summary>
/// <remarks>
/// <para>
/// <c>PrepareActivationAsync</c> creates a state at <see cref="CreationRevision"/>, and only <c>ActivateAsync</c> advances
/// it, switching it on or, when its activation is replaced, off. Switching off requires the state to serve, so an
/// inactive state past the creation revision has served and been replaced.
/// </para>
/// <para>
/// The revision is also the row's concurrency token, so it only ever moves forward. A state that has served is never
/// put back to the creation revision: preparing its activation again is refused until its projection is deleted
/// (<see cref="EnsurePreparable"/>). Anything else that writes a projection state, such as a schema rewriter for its
/// family, must leave the revision alone, or a prepared state reads as replaced.
/// </para>
/// </remarks>
public static class ActivationProjectionStateLifecycle
{
    /// <summary>The revision a projection state is created at.</summary>
    public const long CreationRevision = 1;

    /// <summary>Where the activation whose projection state is <paramref name="isActive"/> at <paramref name="revision"/> stands.</summary>
    public static WorkflowActivationProjectionState Read(bool isActive, long revision) =>
        isActive ? WorkflowActivationProjectionState.Active
        : revision > CreationRevision ? WorkflowActivationProjectionState.Replaced
        : WorkflowActivationProjectionState.Prepared;

    /// <summary>
    /// Refuses to prepare an activation whose projection has served. A retry of an activation whose compensation could
    /// not delete its projection would otherwise keep a state that reads as replaced and switch rows that served before
    /// back on (#2193); the retry fails loudly instead, and its own discard deletes the projection.
    /// </summary>
    public static void EnsurePreparable(bool isActive, long revision, string projection, string activationId)
    {
        if (Read(isActive, revision) != WorkflowActivationProjectionState.Prepared)
            throw new InvalidOperationException(
                $"Activation '{activationId}' cannot be prepared again: its {projection} projection serves or has served, and is still stored. Delete it (DeleteByActivationAsync) first.");
    }
}
