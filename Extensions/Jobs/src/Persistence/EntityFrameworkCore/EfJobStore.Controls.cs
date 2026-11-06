using Elsa.Jobs.Core;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Jobs.Persistence.EntityFrameworkCore;

internal sealed partial class EfJobStore
{
    public ValueTask<JobTransition> PauseAsync(JobPauseRequest request, CancellationToken cancellationToken = default)
    {
        JobStoreCodec.Gate(request.Gate); JobStoreCodec.Name(request.HoldId); JobStoreCodec.Operation(request.Operation);
        var digest = JobStoreCodec.Hash(JobStoreCodec.Json(new { kind = "pause", request }));
        return Write("pause", async (db, now, ct) =>
        {
            if (await Replay<JobTransition>(db, request.Scope, request.Operation, digest, ct) is { } replay) return replay with { Status = replay.Status == JobCommandStatus.Applied ? JobCommandStatus.Replayed : replay.Status };
            var gate = (await Gates(db, request.Scope, [request.Gate], ct))[0];
            var key = JobStoreCodec.Key(request.Scope, "hold", gate.Key, request.HoldId);
            var row = await db.Holds.SingleOrDefaultAsync(x => x.Key == key, ct);
            JobTransition result;
            if (row is not null)
            {
                JobStoreCodec.Exact(row, request.Scope, "hold", gate.Key, request.HoldId);
                var prior = JobStoreCodec.ReadRetained<JobHold>(row.HoldJson);
                result = new(prior.ReleasedAt is null && prior.Actor == request.Operation.Actor && prior.Reason == request.Operation.Reason
                    ? JobCommandStatus.Replayed : JobCommandStatus.Conflict, prior.Revision, null, null);
            }
            else
            {
                var hold = new JobHold(request.Gate, request.HoldId, 1, request.Operation.Actor, request.Operation.Reason, JobStoreCodec.At(now), null, null);
                row = JobStoreCodec.New<JobHoldRecord>(request.Scope, "hold", gate.Key, request.HoldId);
                row.GateKey = gate.Key; row.HoldJson = JobStoreCodec.Json(hold); db.Holds.Add(row);
                gate.ActiveHolds = checked(gate.ActiveHolds + 1); Touch([gate]);
                result = new(JobCommandStatus.Applied, hold.Revision, null, null);
            }
            Remember(db, request.Scope, request.Operation, digest, result, JobOperationKind.Pause, now, new(Gate: request.Gate, HoldId: request.HoldId)); return result;
        }, cancellationToken);
    }

    public ValueTask<JobTransition> ReleaseHoldAsync(JobReleaseHoldRequest request, CancellationToken cancellationToken = default)
    {
        JobStoreCodec.Gate(request.Gate); JobStoreCodec.Name(request.HoldId); JobStoreCodec.Operation(request.Operation);
        var digest = JobStoreCodec.Hash(JobStoreCodec.Json(new { kind = "release-hold", request }));
        return Write("release-hold", async (db, now, ct) =>
        {
            if (await Replay<JobTransition>(db, request.Scope, request.Operation, digest, ct) is { } replay) return replay with { Status = replay.Status == JobCommandStatus.Applied ? JobCommandStatus.Replayed : replay.Status };
            var gate = (await Gates(db, request.Scope, [request.Gate], ct))[0];
            var key = JobStoreCodec.Key(request.Scope, "hold", gate.Key, request.HoldId);
            var row = await db.Holds.SingleOrDefaultAsync(x => x.Key == key, ct);
            JobTransition result;
            if (row is null) result = new(JobCommandStatus.NotFound, null, null, null);
            else
            {
                JobStoreCodec.Exact(row, request.Scope, "hold", gate.Key, request.HoldId);
                var hold = JobStoreCodec.ReadRetained<JobHold>(row.HoldJson);
                if (hold.Revision != request.ExpectedRevision) result = new(JobCommandStatus.Conflict, hold.Revision, null, null);
                else if (hold.ReleasedAt is not null) result = new(JobCommandStatus.Replayed, hold.Revision, null, null);
                else
                {
                    hold = hold with { Revision = checked(hold.Revision + 1), ReleasedAt = JobStoreCodec.At(now), ReleasedBy = request.Operation.Actor };
                    row.HoldJson = JobStoreCodec.Json(hold);
                    if (gate.ActiveHolds < 1) throw new InvalidDataException("The Jobs hold projection is inconsistent.");
                    gate.ActiveHolds--; Touch([gate]); result = new(JobCommandStatus.Applied, hold.Revision, null, null);
                }
            }
            Remember(db, request.Scope, request.Operation, digest, result, JobOperationKind.ReleaseHold, now, new(Gate: request.Gate, HoldId: request.HoldId, ExpectedRevision: request.ExpectedRevision)); return result;
        }, cancellationToken);
    }

    public ValueTask<JobTransition> SetCapacityAsync(JobSetCapacityRequest request, CancellationToken cancellationToken = default)
    {
        JobStoreCodec.Gate(request.Gate); JobStoreCodec.Operation(request.Operation);
        if (request.Limit < 0 || request.ExpectedRevision < 0) throw new ArgumentOutOfRangeException(nameof(request));
        var digest = JobStoreCodec.Hash(JobStoreCodec.Json(new { kind = "capacity", request }));
        return Write("capacity", async (db, now, ct) =>
        {
            if (await Replay<JobTransition>(db, request.Scope, request.Operation, digest, ct) is { } replay) return replay with { Status = replay.Status == JobCommandStatus.Applied ? JobCommandStatus.Replayed : replay.Status };
            var gate = (await Gates(db, request.Scope, [request.Gate], ct))[0];
            JobTransition result;
            if (gate.CapacityRevision != request.ExpectedRevision) result = new(JobCommandStatus.Conflict, gate.CapacityRevision, null, null);
            else
            {
                gate.Capacity = request.Limit; gate.CapacityRevision = checked(gate.CapacityRevision + 1); Touch([gate]);
                result = new(JobCommandStatus.Applied, gate.CapacityRevision, null, null);
            }
            Remember(db, request.Scope, request.Operation, digest, result, JobOperationKind.SetCapacity, now, new(Gate: request.Gate, ExpectedRevision: request.ExpectedRevision, CapacityLimit: request.Limit)); return result;
        }, cancellationToken);
    }
}
