using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfRuntimeHistoryStore
{
    // Exact immutable receipt reconciliation after the independent owner's commit was attempted.
    // Null means no confirmed receipt, distinct from a recorded observation whose content is purged.
    internal ValueTask<RuntimeHistoryObservationResult?> FindObservationReceiptAsync(RuntimeHistoryObservation observation, CancellationToken token)
    {
        var item = observation.Event; var run = item.WorkflowExecutionId; var bound = Capture(run, token);
        return Read<RuntimeHistoryObservationResult?>(bound, run, "find-observation-receipt", async policy =>
        {
            if (!policy.IsEquivalentTo(observation.Policy)) throw Invalid(run, "the observation policy disagrees with its retained native owner");
            var head = await Head(bound.Scope, run, policy, token);
            var id = EfRuntimeHistoryProjectionMapping.Id("event", bound.Scope, run, item.HistoryEventId);
            var row = await context.Set<RuntimeHistoryEventEntity>().AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id, token);
            if (row is null) return null;
            var receipt = mapping.ReadReceipt(row, bound.Scope, run, head);
            var fingerprint = EfRuntimeHistoryObservation.Fingerprint(item);
            if (receipt.HistoryEventId != item.HistoryEventId || receipt.CandidateFingerprint != fingerprint)
                throw Invalid(run, "an immutable observation receipt has conflicting content");
            if (receipt.IsPurged) return new(true, null);
            var retained = mapping.Read(row, bound.Scope, run, head);
            if (retained.PhysicalCommitId != "owner-observation:" + item.HistoryEventId || EfRuntimeHistoryObservation.Fingerprint(retained) != fingerprint)
                throw Invalid(run, "a retained observation has conflicting source content or physical ownership");
            return new(true, retained);
        }, static () => null, token);
    }
}
