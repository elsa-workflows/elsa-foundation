using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// A narrow pre-write validation seam on the trigger indexer (spec 089 follow-up, issue #592 item 2). The
/// indexer invokes every registered validator with the artifact's extracted binding set AFTER extraction and
/// BEFORE it deletes/writes anything, so a validator that throws fails the publish cleanly — the durable index
/// is untouched, exactly like an unroutable trigger failing in the extractor. Validators exist so a stimulus
/// family can enforce publish-time constraints over the index (e.g. HTTP's cross-definition
/// (template, method) uniqueness) without the indexer knowing any stimulus type.
/// </summary>
/// <remarks>
/// <para>
/// This is a contribution (fan-in) seam, the pre-write counterpart of <see cref="IWorkflowTriggerIndexObserver"/>:
/// register implementations with
/// <c>services.TryAddEnumerable(ServiceDescriptor.Scoped&lt;IWorkflowTriggerIndexValidator, MyValidator&gt;())</c>;
/// the indexer resolves them as <c>IEnumerable&lt;IWorkflowTriggerIndexValidator&gt;</c>. There is no default
/// implementation — an unvalidated index is valid.
/// </para>
/// <para>
/// <b>Failure policy:</b> a validator that throws fails the whole publish BEFORE any write, so the store keeps
/// the previous consistent state and no rollback is needed. Constraints that only some stimulus family owns
/// (like HTTP endpoint-routing uniqueness) belong in that family's module — do not enforce cross-definition
/// stimulus uniqueness generically, because for most stimulus types (e.g. two definitions on one Timer cron)
/// shared identity is legitimate fan-out.
/// </para>
/// </remarks>
public interface IWorkflowTriggerIndexValidator
{
    /// <summary>
    /// Called with the artifact's extracted, about-to-be-written binding set before the indexer touches the
    /// store. Throwing propagates and fails the publish with the durable index untouched.
    /// </summary>
    ValueTask ValidateAsync(WorkflowTriggerIndexSnapshot snapshot, CancellationToken cancellationToken = default);
}
