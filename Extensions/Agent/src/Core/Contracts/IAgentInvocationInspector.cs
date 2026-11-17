using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Core.Contracts;

/// <summary>
/// Replacement contract: one inspector per shell. <c>AgentRuntime</c> registers the default only when none is
/// registered, and its startup check refuses a second registration. Reads the observable evidence of one native
/// agent invocation in the caller's ordinary persistence scope. It never decrypts protected turn material, opens an
/// inference client or changes journal state; callers authorize the read first.
/// </summary>
public interface IAgentInvocationInspector
{
    /// <summary>
    /// Returns the evidence for the exact activity execution, or <see langword="null"/> when it is not an agent
    /// invocation in this scope or its committed artifact is unavailable.
    /// </summary>
    /// <exception cref="Exceptions.AgentJournalException">A journal storage, record or scope failure, retaining its cause.</exception>
    ValueTask<AgentInvocationEvidence?> InspectAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default);
}
