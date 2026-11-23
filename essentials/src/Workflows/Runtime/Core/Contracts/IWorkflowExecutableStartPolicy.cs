using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Decides whether a validated executable start may create new execution state.
/// </summary>
/// <remarks>
/// This is deliberately a single-implementation replacement contract, not a fan-out policy chain. Runtime
/// composition provides one allow policy by default; a host may replace it with one implementation. Composition
/// must reject multiple registrations instead of ordering them or applying last-registration-wins behavior.
/// Evaluation occurs after executable, authority, input, and nesting validation and before actor lookup or state
/// materialization. Implementations receive immutable start context without raw workflow inputs and must not
/// mutate executable artifacts or already-materialized executions.
/// </remarks>
public interface IWorkflowExecutableStartPolicy
{
    ValueTask<WorkflowExecutableStartDecision> EvaluateAsync(
        WorkflowExecutableStartPolicyContext context,
        CancellationToken cancellationToken = default);
}
