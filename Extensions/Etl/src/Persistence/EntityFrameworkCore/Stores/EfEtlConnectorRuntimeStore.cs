using System.Data;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfEtlRuntimeStore
{
    public async ValueTask<EtlConnectorAdmissionResult> AdmitConnectorAsync(EtlConnectorRunAdmission admission, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        EtlInvocation invocation;
        string bindingsJson;
        try
        {
            ArgumentNullException.ThrowIfNull(admission);
            invocation = EtlRuntimeRecordCodec.ValidateInvocation(admission.Invocation);
            bindingsJson = EtlConnectorRecordCodec.ValidateBindings(EtlConnectorRecordCodec.Pipeline(invocation), admission.Bindings, options.MaximumPartitionsPerRun);
        }
        catch (Exception exception) when (exception is ArgumentException or System.Text.Json.JsonException or OverflowException)
        {
            return new(EtlStoreStatus.Invalid, null);
        }
        var key = RunKey(invocation.Scope, invocation.RunId);
        var fingerprint = EtlRuntimeIdentity.InvocationFingerprint(invocation);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var run = await FindRunRowAsync(invocation.Scope, invocation.RunId, cancellationToken);
        if (run is not null)
        {
            var prior = await ConnectorRowAsync(key, cancellationToken);
            if (prior is null) return new(EtlStoreStatus.Conflict, null);
            var status = run.InvocationFingerprint == fingerprint && prior.BindingsJson == bindingsJson ? EtlStoreStatus.Replay : EtlStoreStatus.Conflict;
            return new(status, await ConnectorViewAsync(run, prior, cancellationToken));
        }
        run = EtlRuntimeRecordCodec.RunRecord(invocation, fingerprint, null, NowTicks());
        var connector = new EtlConnectorRunRecord
        {
            ScopeKey = key.ScopeKey, RunKey = key.RunKey, BindingsJson = bindingsJson,
            Revision = 1, SchemaVersion = EtlRuntimeEfModule.SchemaVersion
        };
        db.Runs.Add(run);
        db.ConnectorRuns.Add(connector);
        await db.SaveChangesAsync(cancellationToken);
        var view = await ConnectorViewAsync(run, connector, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new(EtlStoreStatus.Applied, view);
    }

    public async ValueTask<EtlConnectorRunView?> FindConnectorRunAsync(EtlScope scope, string runId, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        var key = RunKey(scope, runId);
        // One SQL snapshot for the base state, connector pins and terminal decision.
        var snapshot = await (from run in RunSnapshots(key.ScopeKey)
            join connector in db.ConnectorRuns.AsNoTracking() on new { run.Run.ScopeKey, run.Run.RunKey } equals new { connector.ScopeKey, connector.RunKey }
            where run.Run.RunKey == key.RunKey
            select new { Snapshot = run, Connector = connector, PageBytes = db.ConnectorPartitions.Where(partition => partition.ScopeKey == connector.ScopeKey && partition.RunKey == connector.RunKey).Sum(partition => (long?)partition.PageBytes) ?? 0,
                OutputBytes = db.ConnectorPartitions.Where(partition => partition.ScopeKey == connector.ScopeKey && partition.RunKey == connector.RunKey).Sum(partition => (long?)partition.RetainedOutputBytes) ?? 0,
                MetadataBytes = db.ConnectorPartitions.Where(partition => partition.ScopeKey == connector.ScopeKey && partition.RunKey == connector.RunKey).Sum(partition => (long?)partition.MetadataBytes) ?? 0 }).SingleOrDefaultAsync(cancellationToken);
        if (snapshot is null) return null;
        ValidateRun(snapshot.Snapshot.Run, scope, runId);
        var view = BuildRunView(snapshot.Snapshot.Run, snapshot.Snapshot.Prepared, snapshot.Snapshot.Terminal);
        var state = ReadConnectorState(view.Invocation, snapshot.Connector, snapshot.PageBytes, snapshot.MetadataBytes, snapshot.OutputBytes);
        return new(view, state.Bindings, state.Prepared, snapshot.Connector.RetainedInputBytes);
    }

    public async ValueTask<EtlPreparationLease?> ClaimConnectorPreparationAsync(EtlScope scope, string workerId, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        EtlRuntimeRecordCodec.ValidateIdentity(workerId, "worker id");
        var scopeKey = EtlRuntimeRecordCodec.Scope(scope).Key;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var now = NowTicks();
        var candidate = await (from run in db.Runs
            join connector in db.ConnectorRuns on new { run.ScopeKey, run.RunKey } equals new { connector.ScopeKey, connector.RunKey }
            where run.ScopeKey == scopeKey && run.Status == (int)EtlRunStatus.Preparing &&
                (run.PreparationExpiresAtUtcTicks == null || run.PreparationExpiresAtUtcTicks <= now)
            orderby run.CreatedAtUtcTicks, run.RunKey
            select new { Run = run, Connector = connector }).FirstOrDefaultAsync(cancellationToken);
        if (candidate is null) return null;
        var row = candidate.Run;
        var runId = EtlRuntimeRecordCodec.DecodeIdentity(row.RunId, "run id");
        ValidateRun(row, scope, runId);
        var state = await ReadConnectorStateAsync(EtlRuntimeRecordCodec.ReadInvocation(row.InvocationJson), candidate.Connector, cancellationToken);
        if (state.Prepared is not null) throw new InvalidDataException("A preparing connector already has prepared state.");
        now = NowTicks();
        row.PreparationOwner = EtlRuntimeRecordCodec.EncodeIdentity(workerId, "worker id");
        row.PreparationFence = checked(row.PreparationFence + 1);
        row.PreparationExpiresAtUtcTicks = Expiry(now, options.PreparationLease);
        row.Revision = checked(row.Revision + 1);
        row.UpdatedAtUtcTicks = now;
        await db.SaveChangesAsync(cancellationToken);
        if (row.PreparationExpiresAtUtcTicks <= NowTicks()) return null;
        await tx.CommitAsync(cancellationToken);
        return new(scope, runId, workerId, row.PreparationFence, EtlRuntimeRecordCodec.ReadUtc(row.PreparationExpiresAtUtcTicks.Value));
    }

    public async ValueTask<EtlStoreStatus> CommitConnectorPreparationAsync(EtlPreparationLease lease, EtlConnectorPreparedRun prepared, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        ArgumentNullException.ThrowIfNull(lease);
        var key = RunKey(lease.Scope, lease.RunId);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var run = await FindRunRowAsync(lease.Scope, lease.RunId, cancellationToken);
        if (run is null) return EtlStoreStatus.NotFound;
        var connector = await ConnectorRowAsync(key, cancellationToken);
        if (connector is null) return EtlStoreStatus.Invalid;
        var state = await ReadConnectorStateAsync(EtlRuntimeRecordCodec.ReadInvocation(run.InvocationJson), connector, cancellationToken);
        string preparedJson;
        try { preparedJson = EtlConnectorRecordCodec.ValidatePrepared(state.Pipeline, state.Bindings, prepared); }
        catch (Exception exception) when (exception is ArgumentException or OverflowException) { return EtlStoreStatus.Invalid; }
        if (connector.PreparedJson is not null)
            return connector.PreparedJson == preparedJson ? EtlStoreStatus.Replay : EtlStoreStatus.Conflict;
        var now = NowTicks();
        if (!HasPreparationLease(run, lease, now)) return run.Status >= (int)EtlRunStatus.Succeeded ? EtlStoreStatus.Terminal : EtlStoreStatus.LeaseLost;
        var expiry = run.PreparationExpiresAtUtcTicks!.Value;
        // Read back the bounded snapshot; no caller-owned collection survives the transition.
        prepared = EtlConnectorRecordCodec.Read<EtlConnectorPreparedRun>(preparedJson);
        var partitions = new List<EtlConnectorPartitionRecord>();
        var requests = new List<EtlConnectorHttpRequestRecord>();
        long metadataBytes = 0;
        foreach (var (position, ordinal) in InitialPositions(state.Pipeline, state.Bindings, prepared).Select((position, ordinal) => (position, ordinal)))
        {
            var partition = new EtlConnectorPartitionRecord
            {
                ScopeKey = key.ScopeKey, RunKey = key.RunKey, PartitionOrdinal = ordinal,
                PositionJson = EtlConnectorRecordCodec.Write(position), Completed = EtlConnectorRecordCodec.Complete(position),
                Revision = 1, SchemaVersion = EtlRuntimeEfModule.SchemaVersion
            };
            partition.MetadataBytes = MetadataBytes(partition.PositionJson);
            if (position is EtlConnectorHttpPosition http)
            {
                var request = HttpRequestRecord(partition, http);
                requests.Add(request);
                partition.MetadataBytes = checked(partition.MetadataBytes + request.MetadataBytes);
            }
            if (partition.MetadataBytes > state.Pipeline.Http.MaxRetainedMetadataBytes - metadataBytes)
                return EtlStoreStatus.BudgetExceeded;
            metadataBytes += partition.MetadataBytes;
            partitions.Add(partition);
        }
        connector.PreparedJson = preparedJson;
        connector.RetainedInputBytes = EtlConnectorRecordCodec.RetainedBytes(prepared);
        connector.RetainedMetadataBytes = metadataBytes;
        connector.Revision = checked(connector.Revision + 1);
        // Break the optional partition-pointer cycle without exposing partial preparation: every
        // save belongs to this transaction and expiry after any save rolls it all back.
        db.ConnectorPartitions.AddRange(partitions);
        await db.SaveChangesAsync(cancellationToken);
        db.ConnectorHttpRequests.AddRange(requests);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var request in requests)
        {
            var partition = partitions[request.PartitionOrdinal];
            partition.CurrentRequestSequence = request.PageSequence;
            partition.CurrentRequestHash = request.RequestHash;
        }
        run.Status = (int)EtlRunStatus.Running;
        run.PreparationOwner = null;
        run.PreparationExpiresAtUtcTicks = null;
        run.Revision = checked(run.Revision + 1);
        run.UpdatedAtUtcTicks = now;
        await db.SaveChangesAsync(cancellationToken);
        if (expiry <= NowTicks()) return EtlStoreStatus.LeaseLost;
        await tx.CommitAsync(cancellationToken);
        return EtlStoreStatus.Applied;
    }

    public async ValueTask<EtlConnectorPartitionLease?> ClaimConnectorPartitionAsync(EtlScope scope, string? runId, string workerId, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        EtlRuntimeRecordCodec.ValidateIdentity(workerId, "worker id");
        var scopeKey = EtlRuntimeRecordCodec.Scope(scope).Key;
        var runKey = runId is null ? null : EtlRuntimeRecordCodec.EncodeIdentity(runId, "run id");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var now = NowTicks();
        var candidate = await (from partition in db.ConnectorPartitions
            join run in db.Runs on new { partition.ScopeKey, partition.RunKey } equals new { run.ScopeKey, run.RunKey }
            join connector in db.ConnectorRuns on new { run.ScopeKey, run.RunKey } equals new { connector.ScopeKey, connector.RunKey }
            where partition.ScopeKey == scopeKey && (runKey == null || partition.RunKey == runKey) &&
                run.Status == (int)EtlRunStatus.Running && !partition.Completed &&
                (partition.NotBeforeUtcTicks == null || partition.NotBeforeUtcTicks <= now) &&
                (partition.ExpiresAtUtcTicks == null || partition.ExpiresAtUtcTicks <= now)
            orderby (partition.OwedBatchId != null) descending, run.CreatedAtUtcTicks, partition.RunKey, partition.PartitionOrdinal
            select new { Run = run, Connector = connector, Partition = partition }).FirstOrDefaultAsync(cancellationToken);
        if (candidate is null) return null;
        runId = EtlRuntimeRecordCodec.DecodeIdentity(candidate.Run.RunId, "run id");
        ValidateRun(candidate.Run, scope, runId);
        var invocation = EtlRuntimeRecordCodec.ReadInvocation(candidate.Run.InvocationJson);
        var state = await ReadConnectorStateAsync(invocation, candidate.Connector, cancellationToken);
        var position = await ReadConnectorPositionAsync(candidate.Partition, state.Pipeline, state.Bindings, state.Prepared, cancellationToken, auditHistory: true);
        var row = candidate.Partition;
        var owed = await ReadConnectorOwedBatchAsync(row, new(invocation, state.Bindings),
            state.Pipeline, state.Prepared!, position, cancellationToken);
        now = NowTicks();
        row.Owner = EtlRuntimeRecordCodec.EncodeIdentity(workerId, "worker id");
        row.Fence = checked(row.Fence + 1);
        row.ExpiresAtUtcTicks = Expiry(now, options.PartitionLease);
        row.Revision = checked(row.Revision + 1);
        await db.SaveChangesAsync(cancellationToken);
        if (row.ExpiresAtUtcTicks <= NowTicks()) return null;
        await tx.CommitAsync(cancellationToken);
        return new(scope, runId, row.PartitionOrdinal, workerId, row.Fence, EtlRuntimeRecordCodec.ReadUtc(row.ExpiresAtUtcTicks.Value), position) { OwedBatch = owed };
    }

    public ValueTask<EtlStoreStatus> RenewConnectorPartitionAsync(EtlConnectorPartitionLease lease, CancellationToken cancellationToken = default) =>
        UpdateConnectorLeaseAsync(lease, release: false, cancellationToken);

    public ValueTask<EtlStoreStatus> ReleaseConnectorPartitionAsync(EtlConnectorPartitionLease lease, CancellationToken cancellationToken = default) =>
        UpdateConnectorLeaseAsync(lease, release: true, cancellationToken);

    private async ValueTask<EtlStoreStatus> UpdateConnectorLeaseAsync(EtlConnectorPartitionLease lease, bool release, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        ArgumentNullException.ThrowIfNull(lease);
        var key = RunKey(lease.Scope, lease.RunId);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var run = await FindRunRowAsync(lease.Scope, lease.RunId, cancellationToken);
        if (run is null) return EtlStoreStatus.NotFound;
        if (run.Status >= (int)EtlRunStatus.Succeeded) return EtlStoreStatus.Terminal;
        var row = await db.ConnectorPartitions.SingleOrDefaultAsync(x => x.ScopeKey == key.ScopeKey && x.RunKey == key.RunKey && x.PartitionOrdinal == lease.PartitionOrdinal, cancellationToken);
        if (row is null) return EtlStoreStatus.NotFound;
        var connector = await ConnectorRowAsync(key, cancellationToken) ?? throw new InvalidDataException("The connector admission is missing.");
        var state = await ReadConnectorStateAsync(EtlRuntimeRecordCodec.ReadInvocation(run.InvocationJson), connector, cancellationToken);
        await ReadConnectorPositionAsync(row, state.Pipeline, state.Bindings, state.Prepared, cancellationToken);
        var now = NowTicks();
        if (run.Status != (int)EtlRunStatus.Running || !HasConnectorAuthority(row, lease.Owner, lease.Fence, now) ||
            row.PositionJson != EtlConnectorRecordCodec.Write(lease.Position)) return EtlStoreStatus.LeaseLost;
        var expiry = row.ExpiresAtUtcTicks!.Value;
        row.Owner = release ? null : row.Owner;
        row.ExpiresAtUtcTicks = release ? null : Expiry(now, options.PartitionLease);
        row.Revision = checked(row.Revision + 1);
        await db.SaveChangesAsync(cancellationToken);
        if (expiry <= NowTicks() || !release && row.ExpiresAtUtcTicks <= NowTicks()) return EtlStoreStatus.LeaseLost;
        await tx.CommitAsync(cancellationToken);
        return EtlStoreStatus.Applied;
    }

    private async ValueTask<EtlConnectorRunView> ConnectorViewAsync(EtlRuntimeRunRecord run, EtlConnectorRunRecord connector, CancellationToken cancellationToken)
    {
        var view = await ReadViewAsync(run, cancellationToken);
        var state = await ReadConnectorStateAsync(view.Invocation, connector, cancellationToken);
        return new(view, state.Bindings, state.Prepared, connector.RetainedInputBytes);
    }

    private (EtlPipelineV2 Pipeline, EtlConnectorRunBindings Bindings, EtlConnectorPreparedRun? Prepared) ReadConnectorState(EtlInvocation invocation, EtlConnectorRunRecord connector, long pageBytes, long metadataBytes, long outputBytes)
    {
        EnsureSchema(connector.SchemaVersion);
        try
        {
            var pipeline = EtlConnectorRecordCodec.Pipeline(invocation);
            var bindings = EtlConnectorRecordCodec.Read<EtlConnectorRunBindings>(connector.BindingsJson);
            EtlConnectorRecordCodec.ValidateBindings(pipeline, bindings, options.MaximumPartitionsPerRun);
            var prepared = connector.PreparedJson is null ? null : EtlConnectorRecordCodec.Read<EtlConnectorPreparedRun>(connector.PreparedJson);
            if (prepared is not null) EtlConnectorRecordCodec.ValidatePrepared(pipeline, bindings, prepared);
            if (pageBytes < 0 || connector.RetainedInputBytes != checked((prepared is null ? 0 : EtlConnectorRecordCodec.RetainedBytes(prepared)) + pageBytes) ||
                connector.RetainedInputBytes > pipeline.Http.MaxRetainedInputBytes || prepared is null && connector.RetainedInputBytes != 0)
                throw new InvalidDataException("The retained connector input accounting is invalid.");
            if (metadataBytes < 0 || connector.RetainedMetadataBytes != metadataBytes ||
                metadataBytes > pipeline.Http.MaxRetainedMetadataBytes || prepared is null && metadataBytes != 0)
                throw new InvalidDataException("The retained connector metadata accounting is invalid.");
            if (outputBytes < 0 || connector.RetainedOutputBytes != outputBytes ||
                outputBytes > pipeline.Limits.MaxRetainedOutputBytes || prepared is null && outputBytes != 0)
                throw new InvalidDataException("The retained connector output accounting is invalid.");
            return (pipeline, bindings, prepared);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            throw new InvalidDataException("The persisted connector state does not match its admitted artifact.", exception);
        }
    }

    private static IEnumerable<EtlConnectorPosition> InitialPositions(EtlPipelineV2 pipeline, EtlConnectorRunBindings bindings, EtlConnectorPreparedRun prepared)
    {
        if (prepared.File is { } file)
        {
            var count = (int)Math.Min(file.RecordCount, pipeline.Limits.FilePartitionCount);
            long start = 1;
            for (var ordinal = 0; ordinal < count; ordinal++)
            {
                var end = start + file.RecordCount / count + (ordinal < file.RecordCount % count ? 1 : 0);
                yield return new EtlConnectorFilePosition(start, end, start);
                start = end;
            }
        }
        else foreach (var shard in bindings.HttpShardIds) yield return new EtlConnectorHttpPosition(0, 0, new(shard, null), null, false);
    }

    private static EtlConnectorPosition ReadConnectorPosition(EtlConnectorPartitionRecord row, EtlPipelineV2 pipeline,
        EtlConnectorRunBindings bindings, EtlConnectorPreparedRun? prepared)
    {
        EnsureSchema(row.SchemaVersion);
        if (prepared is null) throw new InvalidDataException("The connector partition has no prepared input.");
        if (row.PartitionOrdinal < 0) throw new InvalidDataException("The connector partition ordinal is invalid.");
        var initial = InitialPositions(pipeline, bindings, prepared).ElementAtOrDefault(row.PartitionOrdinal)
            ?? throw new InvalidDataException("The connector partition ordinal is outside its prepared input.");
        var position = EtlConnectorRecordCodec.Read<EtlConnectorPosition>(row.PositionJson);
        var complete = EtlConnectorRecordCodec.Complete(position);
        if (position is EtlConnectorHttpPosition http) EtlConnectorRecordCodec.ValidateHttpPosition(http, pipeline.Http);
        if (complete != row.Completed || (initial, position) switch
            {
                (EtlConnectorFilePosition expected, EtlConnectorFilePosition actual) => expected.StartOrdinal != actual.StartOrdinal || expected.EndOrdinalExclusive != actual.EndOrdinalExclusive,
                (EtlConnectorHttpPosition expected, EtlConnectorHttpPosition actual) => expected.Request.ShardId != actual.Request.ShardId || actual.PageSequence >= pipeline.Http.MaxPagesPerPartition,
                _ => true
            }) throw new InvalidDataException("The connector partition does not match its pinned input.");
        return position;
    }

    private Task<bool> IsConnectorRunAsync((string ScopeKey, string RunKey) key, CancellationToken cancellationToken) =>
        db.ConnectorRuns.AnyAsync(x => x.ScopeKey == key.ScopeKey && x.RunKey == key.RunKey, cancellationToken);

    private Task<EtlConnectorRunRecord?> ConnectorRowAsync((string ScopeKey, string RunKey) key, CancellationToken cancellationToken) =>
        db.ConnectorRuns.SingleOrDefaultAsync(x => x.ScopeKey == key.ScopeKey && x.RunKey == key.RunKey, cancellationToken);

    private async ValueTask<bool> ConnectorIsCompleteAsync((string ScopeKey, string RunKey) key, CancellationToken cancellationToken)
    {
        var connector = await ConnectorRowAsync(key, cancellationToken);
        if (connector?.PreparedJson is null) return false;
        var run = await db.Runs.SingleAsync(x => x.ScopeKey == key.ScopeKey && x.RunKey == key.RunKey, cancellationToken);
        var state = await ReadConnectorStateAsync(EtlRuntimeRecordCodec.ReadInvocation(run.InvocationJson), connector, cancellationToken);
        var rows = await db.ConnectorPartitions.Where(x => x.ScopeKey == key.ScopeKey && x.RunKey == key.RunKey)
            .Take(options.MaximumPartitionsPerRun == int.MaxValue ? int.MaxValue : options.MaximumPartitionsPerRun + 1).ToListAsync(cancellationToken);
        var count = InitialPositions(state.Pipeline, state.Bindings, state.Prepared!).Count();
        if (rows.Count != count || await db.ConnectorBatches.AnyAsync(batch => batch.ScopeKey == key.ScopeKey &&
            batch.RunKey == key.RunKey && !batch.Settled, cancellationToken)) return false;
        foreach (var row in rows)
            if (!EtlConnectorRecordCodec.Complete(await ReadConnectorPositionAsync(row, state.Pipeline, state.Bindings, state.Prepared, cancellationToken, auditHistory: true))) return false;
        return true;
    }

    private async ValueTask<long?> ConnectorFailureExpiryAsync(EtlTerminalProposal proposal, long now, CancellationToken cancellationToken)
    {
        var key = RunKey(proposal.Scope, proposal.RunId);
        var row = await db.ConnectorPartitions.SingleOrDefaultAsync(x => x.ScopeKey == key.ScopeKey && x.RunKey == key.RunKey && x.PartitionOrdinal == proposal.PartitionOrdinal, cancellationToken);
        if (row is null) return null;
        var connector = await ConnectorRowAsync(key, cancellationToken) ?? throw new InvalidDataException("The connector admission is missing.");
        var run = await FindRunRowAsync(proposal.Scope, proposal.RunId, cancellationToken) ?? throw new InvalidDataException("The connector run is missing.");
        var state = await ReadConnectorStateAsync(EtlRuntimeRecordCodec.ReadInvocation(run.InvocationJson), connector, cancellationToken);
        await ReadConnectorPositionAsync(row, state.Pipeline, state.Bindings, state.Prepared, cancellationToken);
        return HasConnectorAuthority(row, proposal.PartitionOwner!, proposal.PartitionFence!.Value, now) ? row.ExpiresAtUtcTicks : null;
    }

    private static bool HasConnectorAuthority(EtlConnectorPartitionRecord row, string owner, long fence, long now) =>
        row.Owner == EtlRuntimeRecordCodec.EncodeIdentity(owner, "worker id") && row.Fence == fence && row.ExpiresAtUtcTicks is { } expiry && expiry > now;
}
