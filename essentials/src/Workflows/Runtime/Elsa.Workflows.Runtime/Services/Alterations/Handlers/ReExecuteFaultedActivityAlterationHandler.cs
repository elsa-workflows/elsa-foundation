using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Services.Alterations.Handlers;

/// <summary>
/// Trusted <c>ReExecuteFaultedActivity/1</c> implementation: the operator re-execution of one faulted activity waiting
/// for intervention (spec 1221 FR-014, ADR 0092 decision 8 as amended 2026-11-16). It is the retry's sibling, with the
/// same preflight rules, incident resolution and supersession, but its successor is a new logical invocation: inputs are
/// materialized again from current committed state with a new read set, so a variable an operator corrected with
/// <c>ModifyVariable</c> reaches the step, and its external effects repeat on purpose. A retry
/// (<c>RetryFaultedActivity/1</c>) keeps the invocation's identity, operation keys and committed inputs instead.
/// </summary>
public sealed class ReExecuteFaultedActivityAlterationHandler(RetryFaultedActivityAlterationHandler retry) : IWorkflowAlterationPreflightHandler
{
    /// <summary>The stable alteration kind.</summary>
    public const string Kind = "ReExecuteFaultedActivity";

    /// <summary>The schema version of the payload this handler accepts (the same payload as a retry).</summary>
    public const int SchemaVersion = 1;

    private readonly RetryFaultedActivityAlterationHandler _retry = retry ?? throw new ArgumentNullException(nameof(retry));

    public ValueTask<WorkflowAlterationPreflightResult> PreflightAsync(
        WorkflowAlterationPreflightContext context,
        CancellationToken cancellationToken = default) =>
        _retry.PreflightAsync(context, ActivitySuccessorStaging.SuccessorKind.ReExecute, cancellationToken);
}
