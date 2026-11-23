using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Services.Alterations.Handlers;

/// <summary>
/// Trusted <c>AcceptRecordedCompletion/1</c> implementation: the third operator action on a faulted activity waiting for
/// intervention (spec 1221 FR-014, FR-017). It applies only to a step whose body succeeded but whose commit a
/// <c>Fault</c> lost update refused, and applies that recorded completion without running the body again: the same
/// result and outcome selection, and every recorded variable write as an override that keeps the value it overwrote.
/// </summary>
/// <remarks>
/// It shares the retry's preflight rules (a running run, a faulted source waiting for intervention under a running
/// parent) and supersession, and revalidates the pinned contract and every target variable before anything applies.
/// The successor is the same logical invocation and is committed completed; its completion is reported to the parent
/// once. A repeated request finds the source superseded and is refused, so downstream work is never scheduled twice.
/// A step whose body faulted, or whose completion also produced durable outputs, a workflow dispatch, a published
/// stimulus or a parent or ancestor notification, has no recorded completion and is refused (<c>NoRecordedCompletion</c>).
/// </remarks>
public sealed class AcceptRecordedCompletionAlterationHandler(RetryFaultedActivityAlterationHandler retry) : IWorkflowAlterationPreflightHandler
{
    /// <summary>The stable alteration kind.</summary>
    public const string Kind = "AcceptRecordedCompletion";

    /// <summary>The schema version of the payload this handler accepts (the same payload as a retry).</summary>
    public const int SchemaVersion = 1;

    private readonly RetryFaultedActivityAlterationHandler _retry = retry ?? throw new ArgumentNullException(nameof(retry));

    public ValueTask<WorkflowAlterationPreflightResult> PreflightAsync(
        WorkflowAlterationPreflightContext context,
        CancellationToken cancellationToken = default) =>
        _retry.PreflightAsync(context, ActivitySuccessorStaging.SuccessorKind.Accept, cancellationToken);
}
