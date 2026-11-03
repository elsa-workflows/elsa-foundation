using Elsa.Agent.Specialists.Core.Models;

namespace Elsa.Agent.Specialists.Core.Contracts;

/// <summary>
/// Replacement contract: one inspector per shell. <c>SpecialistsRuntime</c> registers the default only when none is
/// registered, and its startup check refuses a second registration. Reads the observable evidence of one native
/// specialist invocation in the caller's ordinary persistence scope. It never decrypts protected turn material, opens an
/// inference client or changes journal state; callers authorize the read first.
/// </summary>
public interface ISpecialistInvocationInspector
{
    /// <summary>
    /// Returns the evidence for the exact activity execution, or <see langword="null"/> when it is not a specialist
    /// invocation in this scope or its committed artifact is unavailable.
    /// </summary>
    /// <exception cref="Exceptions.SpecialistJournalException">A journal storage, record or scope failure, retaining its cause.</exception>
    ValueTask<SpecialistInvocationEvidence?> InspectAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default);
}
