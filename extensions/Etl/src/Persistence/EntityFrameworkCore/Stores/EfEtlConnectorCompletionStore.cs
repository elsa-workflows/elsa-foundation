using System.Data;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfEtlRuntimeStore
{
    public async ValueTask<EtlRunCompletionOutcome> RequestConnectorRunCompletionAsync(EtlScope scope, string runId,
        CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        var key = RunKey(scope, runId);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var run = await FindRunRowAsync(scope, runId, cancellationToken);
        if (run is null) return EtlRunCompletionOutcome.NotFound;
        // A finite run reports NotContinuous whether or not it has settled, so the answer does not race its settlement.
        var connector = await ConnectorRowAsync(key, cancellationToken);
        if (connector is null || EtlConnectorRecordCodec.Pipeline(EtlRuntimeRecordCodec.ReadInvocation(run.InvocationJson)).Continuous is null)
            return EtlRunCompletionOutcome.NotContinuous;
        if (run.Status >= (int)EtlRunStatus.Succeeded) return EtlRunCompletionOutcome.Terminal;
        if (connector.CompletionRequestedAtUtcTicks is not null) return EtlRunCompletionOutcome.AlreadyRequested;

        var now = NowTicks();
        connector.CompletionRequestedAtUtcTicks = now;
        connector.Revision = checked(connector.Revision + 1);
        var waiting = await db.ConnectorPartitions.Where(row => row.ScopeKey == key.ScopeKey && row.RunKey == key.RunKey &&
            !row.Completed && row.OwedBatchId == null && row.NotBeforeUtcTicks != null).ToListAsync(cancellationToken);
        foreach (var partition in waiting)
        {
            // Only an idle shard has a cleared journal; a retry wait keeps its schedule.
            var journal = await Requests(partition).Where(request => request.PageSequence == partition.CurrentRequestSequence)
                .Select(request => request.FetchJson).SingleAsync(cancellationToken);
            if (journal is not null) continue;
            partition.NotBeforeUtcTicks = null;
            partition.Revision = checked(partition.Revision + 1);
        }
        run.UpdatedAtUtcTicks = now;
        run.Revision = checked(run.Revision + 1);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return EtlRunCompletionOutcome.Accepted;
    }
}
