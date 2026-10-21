using System.Data;
using System.Security.Cryptography;
using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

/// <summary>Persists bounded ETL state transitions independently from workflow checkpoints and design data.</summary>
internal sealed partial class EfEtlRuntimeStore(
    EtlRuntimeDbContext db,
    TimeProvider clock,
    EtlRuntimeStoreOptions options,
    ILogger<EfEtlRuntimeStore>? logger = null) : IEtlRuntimeStore, IEtlConnectorRuntimeStore
{
    public int MaximumPageSize => options.MaximumPageSize;

    public async ValueTask<EtlAdmissionResult> AdmitAsync(EtlRunAdmission admission, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        EtlInvocation invocation;
        string bindingsJson;
        try
        {
            ArgumentNullException.ThrowIfNull(admission);
            invocation = EtlRuntimeRecordCodec.ValidateInvocation(admission.Invocation);
            bindingsJson = EtlRuntimeRecordCodec.ValidateBindings(invocation, admission.Bindings);
        }
        catch (Exception exception) when (exception is ArgumentException or System.Text.Json.JsonException)
        {
            return new(EtlStoreStatus.Invalid, null);
        }
        var fingerprint = EtlRuntimeIdentity.InvocationFingerprint(invocation);
        var key = RunKey(invocation.Scope, invocation.RunId);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var existing = await db.Runs.SingleOrDefaultAsync(x => x.ScopeKey == key.ScopeKey && x.RunKey == key.RunKey, cancellationToken);
        if (existing is not null)
        {
            ValidateRun(existing, invocation.Scope, invocation.RunId);
            var status = StringComparer.Ordinal.Equals(existing.InvocationFingerprint, fingerprint) &&
                         StringComparer.Ordinal.Equals(existing.BindingsJson, bindingsJson)
                ? EtlStoreStatus.Replay
                : EtlStoreStatus.Conflict;
            return new(status, await ReadViewAsync(existing, cancellationToken));
        }

        var now = NowTicks();
        var created = EtlRuntimeRecordCodec.RunRecord(invocation, fingerprint, bindingsJson, now);
        db.Runs.Add(created);
        await db.SaveChangesAsync(cancellationToken);
        var view = await ReadViewAsync(created, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(EtlStoreStatus.Applied, view);
    }

    public async ValueTask<EtlTerminalSettlementResult> RejectAdmissionAsync(EtlInvocation invocation, EtlRunDiagnostic diagnostic, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        try
        {
            EtlRuntimeRecordCodec.ValidateInvocation(invocation);
            EtlRuntimeRecordCodec.ValidateDiagnostic(diagnostic);
        }
        catch (Exception exception) when (exception is ArgumentException or System.Text.Json.JsonException)
        {
            return new(EtlStoreStatus.Invalid, null);
        }
        return await SettleAsync(invocation, EtlTerminalOutcome.BusinessError, diagnostic, null, cancellationToken);
    }

    public async ValueTask<EtlRunView?> FindRunAsync(EtlScope scope, string runId, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        ArgumentNullException.ThrowIfNull(scope);
        EtlRuntimeRecordCodec.ValidateIdentity(runId, "run id");
        return await ReadRunViewSnapshotAsync(scope, runId, cancellationToken);
    }

    public async ValueTask<EtlPreparationLease?> ClaimPreparationAsync(EtlScope scope, string workerId, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        ArgumentNullException.ThrowIfNull(scope);
        EtlRuntimeRecordCodec.ValidateIdentity(workerId, "worker id");
        var scopeKey = EtlRuntimeRecordCodec.Scope(scope).Key;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var now = NowTicks();
        var row = await db.Runs.Where(x => x.ScopeKey == scopeKey && x.Status == (int)EtlRunStatus.Preparing && x.BindingsJson != null &&
                    (x.PreparationExpiresAtUtcTicks == null || x.PreparationExpiresAtUtcTicks <= now) &&
                    !db.ConnectorRuns.Any(connector => connector.ScopeKey == x.ScopeKey && connector.RunKey == x.RunKey))
            .OrderBy(x => x.CreatedAtUtcTicks).ThenBy(x => x.RunKey).FirstOrDefaultAsync(cancellationToken);
        if (row is null) return null;
        ValidateRun(row, scope, EtlRuntimeRecordCodec.DecodeIdentity(row.RunId, "run id"));
        now = NowTicks();
        row.PreparationOwner = EtlRuntimeRecordCodec.EncodeIdentity(workerId, "worker id");
        row.PreparationFence = checked(row.PreparationFence + 1);
        row.PreparationExpiresAtUtcTicks = Expiry(now, options.PreparationLease);
        row.Revision = checked(row.Revision + 1);
        row.UpdatedAtUtcTicks = now;
        await db.SaveChangesAsync(cancellationToken);
        if (row.PreparationExpiresAtUtcTicks <= NowTicks()) return null;
        await tx.CommitAsync(cancellationToken);
        return new(scope, EtlRuntimeRecordCodec.DecodeIdentity(row.RunId, "run id"), workerId, row.PreparationFence,
            EtlRuntimeRecordCodec.ReadUtc(row.PreparationExpiresAtUtcTicks.Value));
    }

    public async ValueTask<EtlStoreStatus> RenewPreparationAsync(EtlPreparationLease lease, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        ArgumentNullException.ThrowIfNull(lease);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var now = NowTicks();
        var row = await FindRunRowAsync(lease.Scope, lease.RunId, cancellationToken);
        if (row is null) return EtlStoreStatus.NotFound;
        if (!HasPreparationLease(row, lease, now)) return row.Status >= (int)EtlRunStatus.Succeeded ? EtlStoreStatus.Terminal : EtlStoreStatus.LeaseLost;
        var leaseExpiresAt = row.PreparationExpiresAtUtcTicks!.Value;
        row.PreparationExpiresAtUtcTicks = Expiry(now, options.PreparationLease);
        row.Revision = checked(row.Revision + 1);
        row.UpdatedAtUtcTicks = now;
        await db.SaveChangesAsync(cancellationToken);
        if (leaseExpiresAt <= NowTicks() || row.PreparationExpiresAtUtcTicks <= NowTicks()) return EtlStoreStatus.LeaseLost;
        await tx.CommitAsync(cancellationToken);
        return EtlStoreStatus.Applied;
    }

    public async ValueTask<EtlStoreStatus> CommitPreparationAsync(EtlPreparationLease lease, EtlPreparedRun prepared, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        ArgumentNullException.ThrowIfNull(lease);
        try { EtlRuntimeRecordCodec.ValidatePrepared(prepared); }
        catch (ArgumentException) { return EtlStoreStatus.Invalid; }
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var now = NowTicks();
        var row = await FindRunRowAsync(lease.Scope, lease.RunId, cancellationToken);
        if (row is null) return EtlStoreStatus.NotFound;
        var key = RunKey(lease.Scope, lease.RunId);
        if (EtlRuntimeRecordCodec.ReadInvocation(row.InvocationJson).Artifact.FormatVersion != EtlArtifact.CurrentFormatVersion ||
            await IsConnectorRunAsync(key, cancellationToken)) return EtlStoreStatus.Invalid;
        var existing = await db.PreparedRuns.SingleOrDefaultAsync(x => x.ScopeKey == key.ScopeKey && x.RunKey == key.RunKey, cancellationToken);
        if (row.Status == (int)EtlRunStatus.Running && existing is not null)
            return EtlRuntimeRecordCodec.ReadPrepared(existing) == prepared ? EtlStoreStatus.Replay : EtlStoreStatus.Conflict;
        if (!HasPreparationLease(row, lease, now)) return row.Status >= (int)EtlRunStatus.Succeeded ? EtlStoreStatus.Terminal : EtlStoreStatus.LeaseLost;
        var leaseExpiresAt = row.PreparationExpiresAtUtcTicks!.Value;
        var invocation = EtlRuntimeRecordCodec.ReadInvocation(row.InvocationJson);
        var pipeline = EtlArtifactJson.Read(invocation.Artifact.Pipeline);
        var bindings = EtlRuntimeRecordCodec.ReadBindings(row.BindingsJson)
            ?? throw new InvalidDataException("An admitted ETL run has no pinned bindings.");
        long expectedIndexBytes;
        long maximumIndexBytes;
        try
        {
            expectedIndexBytes = checked((prepared.SourceRecordCount + 1) * (sizeof(long) * 3L));
            maximumIndexBytes = checked(prepared.Source.ByteLength * (sizeof(long) * 3L));
        }
        catch (OverflowException)
        {
            return EtlStoreStatus.Invalid;
        }
        if (pipeline.Limits.PartitionCount > options.MaximumPartitionsPerRun || prepared.SourceRecordCount < 0 ||
            prepared.Source.ByteLength > pipeline.Limits.MaxSnapshotBytes ||
            prepared.Lookup.ByteLength > pipeline.Limits.MaxLookupBytes ||
            prepared.Lookup.ByteLength > pipeline.Limits.MaxSnapshotBytes ||
            prepared.SourceIndex.ByteLength != expectedIndexBytes || prepared.SourceIndex.ByteLength > maximumIndexBytes ||
            !StringComparer.Ordinal.Equals(prepared.Source.ContentHash, bindings.Source.ExpectedSha256) ||
            !StringComparer.Ordinal.Equals(prepared.Lookup.ContentHash, bindings.Lookup.ExpectedSha256) ||
            prepared.SourceRecordCount == long.MaxValue)
            return EtlStoreStatus.Invalid;

        db.PreparedRuns.Add(PreparedRecord(key.ScopeKey, key.RunKey, prepared));
        var partitionCount = (int)Math.Min(prepared.SourceRecordCount, pipeline.Limits.PartitionCount);
        var quotient = partitionCount == 0 ? 0 : prepared.SourceRecordCount / partitionCount;
        var remainder = partitionCount == 0 ? 0 : prepared.SourceRecordCount % partitionCount;
        long cursor = 1;
        for (var ordinal = 0; ordinal < partitionCount; ordinal++)
        {
            var size = quotient + (ordinal < remainder ? 1 : 0);
            db.Partitions.Add(new EtlRuntimePartitionRecord
            {
                ScopeKey = key.ScopeKey,
                RunKey = key.RunKey,
                PartitionOrdinal = ordinal,
                StartOrdinal = cursor,
                EndOrdinalExclusive = cursor + size,
                NextOrdinal = cursor,
                Fence = 0,
                Revision = 1,
                SchemaVersion = EtlRuntimeEfModule.SchemaVersion
            });
            cursor += size;
        }
        row.Status = (int)EtlRunStatus.Running;
        row.PreparationOwner = null;
        row.PreparationExpiresAtUtcTicks = null;
        row.Revision = checked(row.Revision + 1);
        row.UpdatedAtUtcTicks = now;
        if (prepared.SourceRecordCount == 0)
        {
            var result = BuildTerminal(invocation, EtlTerminalOutcome.Succeeded, null, row, now);
            AddTerminalRows(row, result, now);
            row.Status = (int)EtlRunStatus.Succeeded;
            row.Revision = checked(row.Revision + 1);
        }
        await db.SaveChangesAsync(cancellationToken);
        if (leaseExpiresAt <= NowTicks()) return EtlStoreStatus.LeaseLost;
        await tx.CommitAsync(cancellationToken);
        return EtlStoreStatus.Applied;
    }

    public async ValueTask<EtlPartitionLease?> ClaimPartitionAsync(EtlScope scope, string? runId, string workerId, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        ArgumentNullException.ThrowIfNull(scope);
        if (runId is not null) EtlRuntimeRecordCodec.ValidateIdentity(runId, "run id");
        EtlRuntimeRecordCodec.ValidateIdentity(workerId, "worker id");
        var scopeKey = EtlRuntimeRecordCodec.Scope(scope).Key;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var now = NowTicks();
                var candidates = from partition in db.Partitions
                                 join run in db.Runs on new { partition.ScopeKey, partition.RunKey } equals new { run.ScopeKey, run.RunKey }
                                 where partition.ScopeKey == scopeKey && run.Status == (int)EtlRunStatus.Running &&
                                       (partition.NextOrdinal < partition.EndOrdinalExclusive || db.Reservations.Any(reservation =>
                                           reservation.ScopeKey == partition.ScopeKey && reservation.RunKey == partition.RunKey &&
                                           reservation.PartitionOrdinal == partition.PartitionOrdinal && !reservation.Settled)) &&
                                       (partition.ExpiresAtUtcTicks == null || partition.ExpiresAtUtcTicks <= now)
                                 select new { Run = run, Partition = partition };
                if (runId is not null)
                {
                    var runKey = EtlRuntimeRecordCodec.EncodeIdentity(runId, "run id");
                    candidates = candidates.Where(x => x.Run.RunKey == runKey);
                }
                var candidate = await candidates.OrderBy(x => x.Run.CreatedAtUtcTicks).ThenBy(x => x.Run.RunKey)
                    .ThenByDescending(x => db.Reservations.Any(reservation => reservation.ScopeKey == x.Partition.ScopeKey &&
                        reservation.RunKey == x.Partition.RunKey && reservation.PartitionOrdinal == x.Partition.PartitionOrdinal && !reservation.Settled))
                    .ThenBy(x => x.Partition.PartitionOrdinal).FirstOrDefaultAsync(cancellationToken);
                if (candidate is null) return null;
                var selectedRunId = EtlRuntimeRecordCodec.DecodeIdentity(candidate.Run.RunId, "run id");
                ValidateRun(candidate.Run, scope, selectedRunId);
                EnsureSchema(candidate.Partition.SchemaVersion);
                now = NowTicks();
                candidate.Partition.Owner = EtlRuntimeRecordCodec.EncodeIdentity(workerId, "worker id");
                candidate.Partition.Fence = checked(candidate.Partition.Fence + 1);
                candidate.Partition.ExpiresAtUtcTicks = Expiry(now, options.PartitionLease);
                candidate.Partition.Revision = checked(candidate.Partition.Revision + 1);
                await db.SaveChangesAsync(cancellationToken);
                if (candidate.Partition.ExpiresAtUtcTicks <= NowTicks()) return null;
                await tx.CommitAsync(cancellationToken);
                return ToPartitionLease(scope, selectedRunId, candidate.Partition, workerId);
            }
            catch (DbUpdateConcurrencyException) when (attempt == 0)
            {
                db.ChangeTracker.Clear();
            }
        }
        return null;
    }

    public async ValueTask<EtlStoreStatus> ReleasePartitionAsync(EtlPartitionLease lease, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        ArgumentNullException.ThrowIfNull(lease);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var now = NowTicks();
        var run = await FindRunRowAsync(lease.Scope, lease.RunId, cancellationToken);
        var partition = await FindPartitionRowAsync(lease.Scope, lease.RunId, lease.PartitionOrdinal, cancellationToken);
        if (run is null || partition is null) return EtlStoreStatus.NotFound;
        if (run.Status != (int)EtlRunStatus.Running) return EtlStoreStatus.Terminal;
        if (!HasPartitionLease(partition, lease, now, requireCursor: false)) return EtlStoreStatus.LeaseLost;
        var leaseExpiresAt = partition.ExpiresAtUtcTicks!.Value;
        partition.Owner = null;
        partition.ExpiresAtUtcTicks = null;
        partition.Fence = checked(partition.Fence + 1);
        partition.Revision = checked(partition.Revision + 1);
        await db.SaveChangesAsync(cancellationToken);
        if (leaseExpiresAt <= NowTicks()) return EtlStoreStatus.LeaseLost;
        await tx.CommitAsync(cancellationToken);
        return EtlStoreStatus.Applied;
    }

    public async ValueTask<EtlStoreStatus> RenewPartitionAsync(EtlPartitionLease lease, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        ArgumentNullException.ThrowIfNull(lease);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var now = NowTicks();
        var row = await FindPartitionRowAsync(lease.Scope, lease.RunId, lease.PartitionOrdinal, cancellationToken);
        if (row is null) return EtlStoreStatus.NotFound;
        var run = await FindRunRowAsync(lease.Scope, lease.RunId, cancellationToken);
        if (run is null) return EtlStoreStatus.NotFound;
        if (run.Status != (int)EtlRunStatus.Running) return EtlStoreStatus.Terminal;
        if (!HasPartitionLease(row, lease, now, requireCursor: false)) return EtlStoreStatus.LeaseLost;
        var leaseExpiresAt = row.ExpiresAtUtcTicks!.Value;
        row.ExpiresAtUtcTicks = Expiry(now, options.PartitionLease);
        row.Revision = checked(row.Revision + 1);
        await db.SaveChangesAsync(cancellationToken);
        if (leaseExpiresAt <= NowTicks() || row.ExpiresAtUtcTicks <= NowTicks()) return EtlStoreStatus.LeaseLost;
        await tx.CommitAsync(cancellationToken);
        return EtlStoreStatus.Applied;
    }

    public async ValueTask<EtlReservationResult> ReserveBatchAsync(EtlPartitionLease lease, EtlBatchDescriptor batch, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(batch);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var now = NowTicks();
        var run = await FindRunRowAsync(lease.Scope, lease.RunId, cancellationToken);
        var partition = await FindPartitionRowAsync(lease.Scope, lease.RunId, lease.PartitionOrdinal, cancellationToken);
        if (run is null || partition is null) return new(EtlStoreStatus.NotFound, null);
        if (run.Status != (int)EtlRunStatus.Running) return new(EtlStoreStatus.Terminal, null);
        if (!HasPartitionLease(partition, lease, now)) return new(EtlStoreStatus.LeaseLost, null);
        var key = RunKey(lease.Scope, lease.RunId);
        var existing = await db.Reservations.SingleOrDefaultAsync(x => x.ScopeKey == key.ScopeKey && x.RunKey == key.RunKey &&
            x.PartitionOrdinal == lease.PartitionOrdinal && x.StartOrdinal == batch.StartOrdinal, cancellationToken);
        if (existing is not null)
        {
            EnsureSchema(existing.SchemaVersion);
            if (!ReservationMatches(existing, batch)) return new(EtlStoreStatus.Conflict, null);
            return new(EtlStoreStatus.Replay, new(batch, existing.Settled));
        }
        if (batch.StartOrdinal != partition.NextOrdinal || batch.EndOrdinalExclusive <= batch.StartOrdinal ||
            batch.EndOrdinalExclusive > partition.EndOrdinalExclusive || batch.InputBytes <= 0 || !EtlRuntimeRecordCodec.IsSha256(batch.InputHash))
            return new(EtlStoreStatus.Invalid, null);

        var preparedRow = await db.PreparedRuns.SingleOrDefaultAsync(x => x.ScopeKey == key.ScopeKey && x.RunKey == key.RunKey, cancellationToken);
        if (preparedRow is null) return new(EtlStoreStatus.Invalid, null);
        var prepared = EtlRuntimeRecordCodec.ReadPrepared(preparedRow);
        var admission = ReadAdmission(run);
        string expectedBatchId;
        try
        {
            expectedBatchId = EtlRuntimeIdentity.BatchId(admission, prepared, lease.PartitionOrdinal,
                batch.StartOrdinal, batch.EndOrdinalExclusive, batch.InputBytes, batch.InputHash);
        }
        catch (ArgumentException) { return new(EtlStoreStatus.Invalid, null); }
        if (!StringComparer.Ordinal.Equals(batch.BatchId, expectedBatchId)) return new(EtlStoreStatus.Invalid, null);

        var pipeline = EtlArtifactJson.Read(admission.Invocation.Artifact.Pipeline);
        var records = batch.EndOrdinalExclusive - batch.StartOrdinal;
        if (records > pipeline.Limits.MaxBatchRecords || batch.InputBytes > pipeline.Limits.MaxBatchBytes ||
            run.OutstandingRecords > pipeline.Limits.MaxOutstandingRecords - records ||
            run.OutstandingBytes > pipeline.Limits.MaxOutstandingBytes - batch.InputBytes)
            return new(EtlStoreStatus.BudgetExceeded, null);
        if (await db.Reservations.AnyAsync(x => x.ScopeKey == key.ScopeKey && x.RunKey == key.RunKey && x.PartitionOrdinal == lease.PartitionOrdinal && !x.Settled, cancellationToken))
            return new(EtlStoreStatus.Conflict, null);

        db.Reservations.Add(new EtlRuntimeReservationRecord
        {
            ScopeKey = key.ScopeKey,
            RunKey = key.RunKey,
            PartitionOrdinal = lease.PartitionOrdinal,
            StartOrdinal = batch.StartOrdinal,
            EndOrdinalExclusive = batch.EndOrdinalExclusive,
            BatchId = batch.BatchId,
            InputBytes = batch.InputBytes,
            InputHash = batch.InputHash,
            Settled = false,
            SchemaVersion = EtlRuntimeEfModule.SchemaVersion
        });
        run.OutstandingRecords = checked(run.OutstandingRecords + records);
        run.OutstandingBytes = checked(run.OutstandingBytes + batch.InputBytes);
        run.Revision = checked(run.Revision + 1);
        run.UpdatedAtUtcTicks = now;
        partition.Revision = checked(partition.Revision + 1);
        await db.SaveChangesAsync(cancellationToken);
        if (!HasPartitionLease(partition, lease, NowTicks())) return new(EtlStoreStatus.LeaseLost, null);
        await tx.CommitAsync(cancellationToken);
        return new(EtlStoreStatus.Applied, new(batch, false));
    }

    public async ValueTask<EtlAcceptanceResult> AcceptBatchAsync(EtlPartitionLease lease, EtlBatchAcceptance acceptance, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(acceptance);
        ArgumentNullException.ThrowIfNull(acceptance.Effects);
        var effectCount = acceptance.Effects.Count;
        if (effectCount is < 0 or > 2) return new(EtlStoreStatus.Invalid, null);
        var effects = new EtlOutputEffect[effectCount];
        for (var index = 0; index < effectCount; index++)
            effects[index] = acceptance.Effects[index];
        acceptance = acceptance with { Effects = effects };
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var now = NowTicks();
        var run = await FindRunRowAsync(lease.Scope, lease.RunId, cancellationToken);
        var partition = await FindPartitionRowAsync(lease.Scope, lease.RunId, lease.PartitionOrdinal, cancellationToken);
        if (run is null || partition is null) return new(EtlStoreStatus.NotFound, null);
        if (run.Status != (int)EtlRunStatus.Running) return new(EtlStoreStatus.Terminal, null);
        if (!HasPartitionLease(partition, lease, now)) return new(EtlStoreStatus.LeaseLost, null);
        var leaseExpiresAt = partition.ExpiresAtUtcTicks!.Value;
        var leaseOwner = partition.Owner;
        var key = RunKey(lease.Scope, lease.RunId);
        var batch = acceptance.Batch;
        if (batch is null) return new(EtlStoreStatus.Invalid, null);
        var reservation = await db.Reservations.SingleOrDefaultAsync(x => x.ScopeKey == key.ScopeKey && x.RunKey == key.RunKey &&
            x.PartitionOrdinal == lease.PartitionOrdinal && x.StartOrdinal == batch.StartOrdinal, cancellationToken);
        if (reservation is null || !ReservationMatches(reservation, batch)) return new(EtlStoreStatus.Conflict, null);
        EnsureSchema(reservation.SchemaVersion);
        if (reservation.Settled)
        {
            var same = await EffectsMatchAsync(key.ScopeKey, key.RunKey, lease.PartitionOrdinal, acceptance, cancellationToken);
            return new(same ? EtlStoreStatus.Replay : EtlStoreStatus.Conflict, same ? ToPartitionLease(lease.Scope, lease.RunId, partition, lease.Owner) : null);
        }
        long settledRecords;
        try { settledRecords = checked(acceptance.AcceptedRecords + acceptance.RejectedRecords + acceptance.FilteredRecords); }
        catch (OverflowException) { return new(EtlStoreStatus.Invalid, null); }
        if (partition.NextOrdinal != batch.StartOrdinal || batch.EndOrdinalExclusive != reservation.EndOrdinalExclusive ||
            acceptance.AcceptedRecords < 0 || acceptance.RejectedRecords < 0 || acceptance.FilteredRecords < 0 ||
            settledRecords != batch.EndOrdinalExclusive - batch.StartOrdinal)
            return new(EtlStoreStatus.Invalid, null);

        var admission = ReadAdmission(run);
        var expected = new Dictionary<EtlOutputRoute, long>();
        if (acceptance.AcceptedRecords > 0) expected.Add(EtlOutputRoute.Main, acceptance.AcceptedRecords);
        if (acceptance.RejectedRecords > 0) expected.Add(EtlOutputRoute.Error, acceptance.RejectedRecords);
        if (acceptance.Effects.Count != expected.Count) return new(EtlStoreStatus.Invalid, null);
        long totalBytes = 0;
        foreach (var effect in acceptance.Effects)
        {
            if (effect is null || effect.Chunk is null || !Enum.IsDefined(effect.Route) || !expected.Remove(effect.Route, out var expectedCount) || effect.RecordCount != expectedCount || effect.Chunk.ByteLength <= 0)
                return new(EtlStoreStatus.Invalid, null);
            try { EtlRuntimeRecordCodec.ValidateObject(effect.Chunk, "output chunk"); }
            catch (ArgumentException) { return new(EtlStoreStatus.Invalid, null); }
            try { totalBytes = checked(totalBytes + effect.Chunk.ByteLength); }
            catch (OverflowException) { return new(EtlStoreStatus.BudgetExceeded, null); }
            var expectedKey = EtlRuntimeIdentity.OutputKey(admission, batch.BatchId, effect.Route);
            if (!StringComparer.Ordinal.Equals(expectedKey, effect.Chunk.Key)) return new(EtlStoreStatus.Invalid, null);
        }
        if (expected.Count != 0) return new(EtlStoreStatus.Invalid, null);
        var pipeline = EtlArtifactJson.Read(admission.Invocation.Artifact.Pipeline);
        if (totalBytes > pipeline.Limits.MaxOutputBatchBytes) return new(EtlStoreStatus.BudgetExceeded, null);

        foreach (var effect in acceptance.Effects)
        {
            var prior = await db.Manifest.SingleOrDefaultAsync(x => x.ScopeKey == key.ScopeKey && x.RunKey == key.RunKey &&
                x.PartitionOrdinal == lease.PartitionOrdinal && x.BatchStartOrdinal == batch.StartOrdinal && x.Route == (int)effect.Route, cancellationToken);
            if (prior is not null)
                return new(ManifestMatches(prior, batch, effect) ? EtlStoreStatus.Replay : EtlStoreStatus.Conflict, null);
            db.Manifest.Add(new EtlRuntimeManifestRecord
            {
                ScopeKey = key.ScopeKey,
                RunKey = key.RunKey,
                PartitionOrdinal = lease.PartitionOrdinal,
                BatchStartOrdinal = batch.StartOrdinal,
                Route = (int)effect.Route,
                BatchId = batch.BatchId,
                ChunkKey = effect.Chunk.Key,
                ContentHash = effect.Chunk.ContentHash,
                ByteLength = effect.Chunk.ByteLength,
                RecordCount = effect.RecordCount,
                AcceptedAtUtcTicks = now,
                SchemaVersion = EtlRuntimeEfModule.SchemaVersion
            });
        }
        reservation.Settled = true;
        partition.NextOrdinal = batch.EndOrdinalExclusive;
        partition.Revision = checked(partition.Revision + 1);
        if (partition.NextOrdinal >= partition.EndOrdinalExclusive)
        {
            partition.Owner = null;
            partition.ExpiresAtUtcTicks = null;
        }
        run.OutstandingRecords -= batch.EndOrdinalExclusive - batch.StartOrdinal;
        run.OutstandingBytes -= batch.InputBytes;
        run.AcceptedRecords = checked(run.AcceptedRecords + acceptance.AcceptedRecords);
        run.RejectedRecords = checked(run.RejectedRecords + acceptance.RejectedRecords);
        run.FilteredRecords = checked(run.FilteredRecords + acceptance.FilteredRecords);
        run.Revision = checked(run.Revision + 1);
        run.UpdatedAtUtcTicks = now;
        var otherPartitionsPending = await db.Partitions.AnyAsync(x => x.ScopeKey == key.ScopeKey && x.RunKey == key.RunKey &&
            x.PartitionOrdinal != lease.PartitionOrdinal && x.NextOrdinal != x.EndOrdinalExclusive, cancellationToken);
        var otherReservationsPending = await db.Reservations.AnyAsync(x => x.ScopeKey == key.ScopeKey && x.RunKey == key.RunKey &&
            x.PartitionOrdinal != lease.PartitionOrdinal && !x.Settled, cancellationToken);
        if (partition.NextOrdinal == partition.EndOrdinalExclusive && !otherPartitionsPending && !otherReservationsPending &&
            run.OutstandingRecords == 0 && run.OutstandingBytes == 0)
        {
            var outcome = run.RejectedRecords == 0 ? EtlTerminalOutcome.Succeeded : EtlTerminalOutcome.BusinessError;
            var diagnostic = outcome == EtlTerminalOutcome.BusinessError ? new EtlRunDiagnostic("etl.rows-rejected") : null;
            var result = BuildTerminal(admission.Invocation, outcome, diagnostic, run, now);
            AddTerminalRows(run, result, now);
            run.Status = outcome == EtlTerminalOutcome.Succeeded ? (int)EtlRunStatus.Succeeded : (int)EtlRunStatus.BusinessError;
            run.Revision = checked(run.Revision + 1);
        }
        await db.SaveChangesAsync(cancellationToken);
        if (leaseExpiresAt <= NowTicks() || partition.Fence != lease.Fence ||
            partition.StartOrdinal != lease.StartOrdinal || partition.EndOrdinalExclusive != lease.EndOrdinalExclusive ||
            partition.NextOrdinal != batch.EndOrdinalExclusive ||
            partition.NextOrdinal < partition.EndOrdinalExclusive && partition.Owner != leaseOwner)
            return new(EtlStoreStatus.LeaseLost, null);
        await tx.CommitAsync(cancellationToken);
        return new(EtlStoreStatus.Applied, partition.NextOrdinal < partition.EndOrdinalExclusive ? ToPartitionLease(lease.Scope, lease.RunId, partition, lease.Owner) : null);
    }

    public async ValueTask<EtlTerminalSettlementResult> SettleTerminalAsync(EtlTerminalProposal proposal, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        try
        {
            ArgumentNullException.ThrowIfNull(proposal);
            if (!Enum.IsDefined(proposal.Outcome) || proposal.Outcome == EtlTerminalOutcome.Cancelled)
                return new(EtlStoreStatus.Invalid, null);
            if (proposal.Diagnostic is not null) EtlRuntimeRecordCodec.ValidateDiagnostic(proposal.Diagnostic);
            ArgumentNullException.ThrowIfNull(proposal.Scope);
            EtlRuntimeRecordCodec.ValidateIdentity(proposal.RunId, "run id");
            ValidateAuthorityShape(proposal);
        }
        catch (ArgumentException)
        {
            return new(EtlStoreStatus.Invalid, null);
        }
        var run = await FindRunRowAsync(proposal.Scope, proposal.RunId, cancellationToken);
        if (run is null) return new(EtlStoreStatus.NotFound, null);
        var invocation = EtlRuntimeRecordCodec.ReadInvocation(run.InvocationJson);
        return await SettleAsync(invocation, proposal.Outcome, proposal.Diagnostic, proposal, cancellationToken);
    }

    public async ValueTask<EtlTerminalSettlementResult> CancelAsync(EtlInvocation invocation, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        try { EtlRuntimeRecordCodec.ValidateInvocation(invocation); }
        catch (Exception exception) when (exception is ArgumentException or System.Text.Json.JsonException)
        {
            return new(EtlStoreStatus.Invalid, null);
        }
        return await SettleAsync(invocation, EtlTerminalOutcome.Cancelled,
            new EtlRunDiagnostic("cancelled"), null, cancellationToken);
    }

    public ValueTask<EtlDeliveryLease?> ClaimDeliveryAsync(EtlScope scope, string workerId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return ClaimDeliveryCoreAsync(scope, workerId, cancellationToken);
    }

    public ValueTask<EtlDeliveryLease?> ClaimNextDeliveryAsync(string workerId, CancellationToken cancellationToken = default) =>
        ClaimDeliveryCoreAsync(null, workerId, cancellationToken);

    private async ValueTask<EtlDeliveryLease?> ClaimDeliveryCoreAsync(EtlScope? requestedScope, string workerId,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        EtlRuntimeRecordCodec.ValidateIdentity(workerId, "worker id");
        var scopeKey = requestedScope is null ? null : EtlRuntimeRecordCodec.Scope(requestedScope).Key;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var now = NowTicks();
        IQueryable<EtlRuntimeOutboxRecord> eligible = db.Outbox.AsNoTracking().Where(x => x.Disposition == null &&
            (x.ExpiresAtUtcTicks == null || x.ExpiresAtUtcTicks <= now));
        if (scopeKey is not null)
            eligible = eligible.Where(x => x.ScopeKey == scopeKey);
        if (requestedScope is not null)
        {
            var outbox = await eligible.OrderBy(x => x.ScopeKey).ThenBy(x => x.RunKey).FirstOrDefaultAsync(cancellationToken);
            if (outbox is null)
                return null;
            var validated = await ValidateDeliveryCandidateAsync(outbox, requestedScope, cancellationToken);
            return await ClaimDeliveryCandidateAsync(tx, outbox, validated, workerId, cancellationToken);
        }

        var afterScopeKey = (string?)null;
        var afterRunKey = (string?)null;
        Exception? firstInvalidCandidate = null;
        while (true)
        {
            var pageQuery = eligible;
            if (afterScopeKey is not null)
                pageQuery = pageQuery.Where(x => x.ScopeKey.CompareTo(afterScopeKey) > 0 ||
                    (x.ScopeKey == afterScopeKey && x.RunKey.CompareTo(afterRunKey!) > 0));
            var page = await pageQuery.OrderBy(x => x.ScopeKey).ThenBy(x => x.RunKey)
                .Take(options.MaximumPageSize).ToListAsync(cancellationToken);
            if (page.Count == 0)
                break;

            foreach (var candidate in page)
            {
                ValidatedDeliveryCandidate validated;
                try
                {
                    validated = await ValidateDeliveryCandidateAsync(candidate, null, cancellationToken);
                }
                catch (Exception exception) when (IsPersistedDataException(exception))
                {
                    firstInvalidCandidate ??= exception;
                    db.ChangeTracker.Clear();
                    logger?.LogWarning("ETL global delivery found invalid persisted candidate {CandidateId} ({FailureType}); it remains pending while later candidates are checked.",
                        DeliveryCandidateId(candidate), exception.GetType().Name);
                    continue;
                }

                var lease = await ClaimDeliveryCandidateAsync(tx, candidate, validated, workerId, cancellationToken);
                // The claim transaction must roll back if its lease expired before commit; don't commit it
                // accidentally while moving on to another candidate in this transaction.
                return lease;
            }

            var last = page[^1];
            afterScopeKey = last.ScopeKey;
            afterRunKey = last.RunKey;
        }

        if (firstInvalidCandidate is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(firstInvalidCandidate).Throw();
        return null;
    }

    private async ValueTask<ValidatedDeliveryCandidate> ValidateDeliveryCandidateAsync(EtlRuntimeOutboxRecord outbox,
        EtlScope? requestedScope, CancellationToken cancellationToken)
    {
        var run = await db.Runs.SingleOrDefaultAsync(x => x.ScopeKey == outbox.ScopeKey && x.RunKey == outbox.RunKey, cancellationToken);
        var terminal = await db.Terminals.SingleOrDefaultAsync(x => x.ScopeKey == outbox.ScopeKey && x.RunKey == outbox.RunKey, cancellationToken);
        if (run is null || terminal is null) throw new InvalidDataException("The ETL terminal outbox row has no run or terminal result.");
        EtlInvocation invocation;
        EtlScope scope;
        string runId;
        if (requestedScope is not null)
        {
            scope = requestedScope;
            runId = EtlRuntimeRecordCodec.DecodeIdentity(run.RunId, "run id");
            ValidateRun(run, scope, runId);
            invocation = EtlRuntimeRecordCodec.ReadInvocation(run.InvocationJson);
            if (requestedScope != invocation.Scope)
                throw new InvalidDataException("The ETL terminal outbox invocation does not match its requested scope.");
        }
        else
        {
            invocation = EtlRuntimeRecordCodec.ReadInvocation(run.InvocationJson);
            scope = invocation.Scope;
            runId = EtlRuntimeRecordCodec.DecodeIdentity(run.RunId, "run id");
            if (!StringComparer.Ordinal.Equals(outbox.ScopeKey, EtlRuntimeRecordCodec.Scope(scope).Key))
                throw new InvalidDataException("The ETL terminal outbox scope does not match its retained invocation.");
            ValidateRun(run, scope, runId);
        }
        if (!StringComparer.Ordinal.Equals(invocation.RunId, runId))
            throw new InvalidDataException("The ETL terminal outbox invocation does not match its retained run.");
        EnsureSchema(outbox.SchemaVersion);
        EnsureSchema(terminal.SchemaVersion);
        var result = ReadTerminal(terminal);
        if (!StringComparer.Ordinal.Equals(outbox.DeliveryId, DeliveryId(result.ResultId)))
            throw new InvalidDataException("The ETL terminal outbox delivery ID does not match its result.");
        if (!StringComparer.Ordinal.Equals(outbox.PayloadHash, OutboxHash(run.InvocationJson, terminal.ResultJson)))
            throw new InvalidDataException("The ETL terminal outbox payload hash does not match its frozen invocation and result.");

        return new(scope, runId, invocation, result);
    }

    private async ValueTask<EtlDeliveryLease?> ClaimDeliveryCandidateAsync(IDbContextTransaction tx,
        EtlRuntimeOutboxRecord outbox, ValidatedDeliveryCandidate validated, string workerId, CancellationToken cancellationToken)
    {
        var now = NowTicks();
        var expiresAtUtcTicks = Expiry(now, options.DeliveryLease);
        var expiresAt = EtlRuntimeRecordCodec.ReadUtc(expiresAtUtcTicks);
        db.Outbox.Attach(outbox);
        outbox.Owner = EtlRuntimeRecordCodec.EncodeIdentity(workerId, "worker id");
        outbox.Fence = checked(outbox.Fence + 1);
        outbox.ExpiresAtUtcTicks = expiresAtUtcTicks;
        outbox.Attempts = checked(outbox.Attempts + 1);
        await db.SaveChangesAsync(cancellationToken);
        if (outbox.ExpiresAtUtcTicks <= NowTicks()) return null;
        await tx.CommitAsync(cancellationToken);
        return new(validated.Scope, validated.RunId, outbox.DeliveryId,
            workerId, outbox.Fence, expiresAt, validated.Invocation.ResultAddress, validated.Result, outbox.PayloadHash);
    }

    private sealed record ValidatedDeliveryCandidate(EtlScope Scope, string RunId,
        EtlInvocation Invocation, EtlTerminalResult Result);

    private static bool IsPersistedDataException(Exception exception) =>
        exception is InvalidDataException or System.Text.Json.JsonException;

    private static string DeliveryCandidateId(EtlRuntimeOutboxRecord outbox) =>
        HashAscii(outbox.ScopeKey + "|" + outbox.RunKey)[..16];

    public async ValueTask<EtlStoreStatus> AcknowledgeDeliveryAsync(EtlDeliveryLease lease, EtlDeliveryDisposition disposition, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        ArgumentNullException.ThrowIfNull(lease);
        if (!Enum.IsDefined(disposition)) return EtlStoreStatus.Invalid;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var now = NowTicks();
        var key = RunKey(lease.Scope, lease.RunId);
        var outbox = await db.Outbox.SingleOrDefaultAsync(x => x.ScopeKey == key.ScopeKey && x.RunKey == key.RunKey, cancellationToken);
        if (outbox is null) return EtlStoreStatus.NotFound;
        EnsureSchema(outbox.SchemaVersion);
        if (outbox.Disposition is { } prior)
            return prior == (int)disposition ? EtlStoreStatus.Replay : EtlStoreStatus.Conflict;
        if (!StringComparer.Ordinal.Equals(outbox.DeliveryId, lease.DeliveryId) || outbox.Fence != lease.Fence ||
            !StringComparer.Ordinal.Equals(outbox.Owner, EtlRuntimeRecordCodec.EncodeIdentity(lease.Owner, "worker id")) ||
            outbox.ExpiresAtUtcTicks is null || outbox.ExpiresAtUtcTicks <= now || outbox.PayloadHash != lease.PayloadHash)
            return EtlStoreStatus.LeaseLost;
        var leaseExpiresAt = outbox.ExpiresAtUtcTicks.Value;
        outbox.Disposition = (int)disposition;
        outbox.AcknowledgedAtUtcTicks = now;
        outbox.Owner = null;
        outbox.ExpiresAtUtcTicks = null;
        await db.SaveChangesAsync(cancellationToken);
        if (leaseExpiresAt <= NowTicks()) return EtlStoreStatus.LeaseLost;
        await tx.CommitAsync(cancellationToken);
        return EtlStoreStatus.Applied;
    }

    public async ValueTask<IReadOnlyList<EtlRunView>> ListRunsAsync(EtlScope scope, string? afterRunId, int take, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        ValidatePage(scope, take);
        var scopeKey = EtlRuntimeRecordCodec.Scope(scope).Key;
        var query = RunSnapshots(scopeKey);
        if (afterRunId is not null)
        {
            EtlRuntimeRecordCodec.ValidateIdentity(afterRunId, "run id");
            var after = EtlRuntimeRecordCodec.EncodeIdentity(afterRunId, "run id");
            query = query.Where(x => x.Run.RunKey.CompareTo(after) > 0);
        }
        var rows = await query.OrderBy(x => x.Run.RunKey).Take(take + 1).ToListAsync(cancellationToken);
        var result = new List<EtlRunView>(Math.Min(take, rows.Count));
        foreach (var snapshot in rows.Take(take))
        {
            ValidateRun(snapshot.Run, scope, EtlRuntimeRecordCodec.DecodeIdentity(snapshot.Run.RunId, "run id"));
            result.Add(BuildRunView(snapshot.Run, snapshot.Prepared, snapshot.Terminal));
        }
        return result;
    }

    public async ValueTask<IReadOnlyList<EtlPartitionView>> ListPartitionsAsync(EtlScope scope, string runId, int afterPartitionOrdinal, int take, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        ValidatePage(scope, take);
        if (afterPartitionOrdinal < -1) throw new ArgumentOutOfRangeException(nameof(afterPartitionOrdinal));
        var key = RunKey(scope, runId);
        var rows = await (from partition in db.Partitions.AsNoTracking()
                          join run in db.Runs.AsNoTracking()
                              on new { partition.ScopeKey, partition.RunKey } equals new { run.ScopeKey, run.RunKey }
                          join reservation in db.Reservations.AsNoTracking().Where(x => !x.Settled)
                              on new { partition.ScopeKey, partition.RunKey, partition.PartitionOrdinal }
                              equals new { reservation.ScopeKey, reservation.RunKey, reservation.PartitionOrdinal } into reservationRows
                          from reservation in reservationRows.DefaultIfEmpty()
                          where partition.ScopeKey == key.ScopeKey && partition.RunKey == key.RunKey &&
                                partition.PartitionOrdinal > afterPartitionOrdinal
                          orderby partition.PartitionOrdinal
                          select new { Run = run, Partition = partition, Reservation = reservation })
            .Take(take).ToListAsync(cancellationToken);
        var result = new List<EtlPartitionView>(rows.Count);
        foreach (var snapshot in rows)
        {
            ValidateRun(snapshot.Run, scope, runId);
            var row = snapshot.Partition;
            EnsureSchema(row.SchemaVersion);
            EtlBatchReservation? owed = null;
            if (snapshot.Reservation is { } reservation)
            {
                EnsureSchema(reservation.SchemaVersion);
                owed = new(new(reservation.BatchId, reservation.StartOrdinal, reservation.EndOrdinalExclusive,
                    reservation.InputBytes, reservation.InputHash), false);
            }
            result.Add(new(row.PartitionOrdinal, row.StartOrdinal, row.EndOrdinalExclusive, row.NextOrdinal,
                row.Owner is null ? null : EtlRuntimeRecordCodec.DecodeIdentity(row.Owner, "worker id"), row.Fence,
                row.ExpiresAtUtcTicks is null ? null : EtlRuntimeRecordCodec.ReadUtc(row.ExpiresAtUtcTicks.Value), owed));
        }
        return result;
    }

    public async ValueTask<IReadOnlyList<EtlManifestEntry>> ListManifestAsync(EtlScope scope, string runId, EtlManifestPosition? after, int take, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        ValidatePage(scope, take);
        var key = RunKey(scope, runId);
        var run = await db.Runs.SingleOrDefaultAsync(x => x.ScopeKey == key.ScopeKey && x.RunKey == key.RunKey, cancellationToken);
        if (run is null) return Array.Empty<EtlManifestEntry>();
        ValidateRun(run, scope, runId);
        var query = db.Manifest.Where(x => x.ScopeKey == key.ScopeKey && x.RunKey == key.RunKey);
        if (after is not null)
        {
            if (after.PartitionOrdinal < 0 || after.BatchStartOrdinal < 0 || !Enum.IsDefined(after.Route))
                throw new ArgumentOutOfRangeException(nameof(after));
            query = query.Where(x => x.PartitionOrdinal > after.PartitionOrdinal ||
                (x.PartitionOrdinal == after.PartitionOrdinal && x.BatchStartOrdinal > after.BatchStartOrdinal) ||
                (x.PartitionOrdinal == after.PartitionOrdinal && x.BatchStartOrdinal == after.BatchStartOrdinal && x.Route > (int)after.Route));
        }
        var rows = await query.OrderBy(x => x.PartitionOrdinal).ThenBy(x => x.BatchStartOrdinal).ThenBy(x => x.Route)
            .Take(take).ToListAsync(cancellationToken);
        var result = new List<EtlManifestEntry>(rows.Count);
        foreach (var row in rows)
        {
            EnsureSchema(row.SchemaVersion);
            if (!Enum.IsDefined((EtlOutputRoute)row.Route)) throw new InvalidDataException("The ETL manifest row has an unsupported route.");
            var position = new EtlManifestPosition(row.PartitionOrdinal, row.BatchStartOrdinal, (EtlOutputRoute)row.Route);
            result.Add(new(position, row.BatchId,
                new((EtlOutputRoute)row.Route, new(row.ChunkKey, row.ContentHash, row.ByteLength), row.RecordCount),
                EtlRuntimeRecordCodec.ReadUtc(row.AcceptedAtUtcTicks)));
        }
        return result;
    }

    private async ValueTask<EtlTerminalSettlementResult> SettleAsync(EtlInvocation invocation, EtlTerminalOutcome outcome,
        EtlRunDiagnostic? diagnostic, EtlTerminalProposal? proposal, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        try
        {
            EtlRuntimeRecordCodec.ValidateInvocation(invocation);
            if (diagnostic is not null) EtlRuntimeRecordCodec.ValidateDiagnostic(diagnostic);
        }
        catch (Exception exception) when (exception is ArgumentException or System.Text.Json.JsonException)
        {
            return new(EtlStoreStatus.Invalid, null);
        }
        if (!Enum.IsDefined(outcome)) return new(EtlStoreStatus.Invalid, null);
        var fingerprint = EtlRuntimeIdentity.InvocationFingerprint(invocation);
        var key = RunKey(invocation.Scope, invocation.RunId);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var now = NowTicks();
        var run = await db.Runs.SingleOrDefaultAsync(x => x.ScopeKey == key.ScopeKey && x.RunKey == key.RunKey, cancellationToken);
        if (run is null)
        {
            if (outcome is not (EtlTerminalOutcome.Cancelled or EtlTerminalOutcome.BusinessError))
                return new(EtlStoreStatus.NotFound, null);
            run = EtlRuntimeRecordCodec.RunRecord(invocation, fingerprint, null, now);
            db.Runs.Add(run);
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            ValidateRun(run, invocation.Scope, invocation.RunId);
            if (!StringComparer.Ordinal.Equals(run.InvocationFingerprint, fingerprint)) return new(EtlStoreStatus.Conflict, null);
            var storedInvocation = EtlRuntimeRecordCodec.ReadInvocation(run.InvocationJson);
            if (EtlRuntimeIdentity.InvocationFingerprint(storedInvocation) != fingerprint)
                throw new InvalidDataException("The ETL invocation fingerprint does not match its persisted snapshot.");
            invocation = storedInvocation;
        }

        var terminalRow = await db.Terminals.SingleOrDefaultAsync(x => x.ScopeKey == key.ScopeKey && x.RunKey == key.RunKey, cancellationToken);
        var result = BuildTerminal(invocation, outcome, diagnostic, run, now);
        if (terminalRow is not null)
        {
            EnsureSchema(terminalRow.SchemaVersion);
            if (!StringComparer.Ordinal.Equals(terminalRow.ResultHash, EtlRuntimeRecordCodec.HashText(terminalRow.ResultJson)))
                throw new InvalidDataException("The ETL terminal result hash does not match its persisted document.");
            var prior = EtlRuntimeRecordCodec.Read<EtlTerminalResult>(terminalRow.ResultJson);
            if (prior.ResultId != result.ResultId || prior.Outcome != result.Outcome || prior.AcceptedRecords != result.AcceptedRecords ||
                prior.RejectedRecords != result.RejectedRecords || prior.FilteredRecords != result.FilteredRecords || prior.Diagnostic != result.Diagnostic)
                return new(EtlStoreStatus.Conflict, prior);
            return new(EtlStoreStatus.Replay, prior);
        }

        // A binding-resolution refusal may only settle an invocation before admission pins its
        // host revisions. A retry with different local binding availability must not cancel or
        // terminalize work that another admission attempt already made runnable.
        var connector = await IsConnectorRunAsync(key, cancellationToken);
        if (outcome == EtlTerminalOutcome.BusinessError && proposal is null && (run.BindingsJson is not null || connector))
            return new(EtlStoreStatus.Conflict, null);

        var currentStatus = (EtlRunStatus)run.Status;
        if (!Enum.IsDefined(currentStatus)) throw new InvalidDataException("The ETL run has an unsupported status.");
        if (currentStatus >= EtlRunStatus.Succeeded) return new(EtlStoreStatus.Terminal, null);
        long? failureLeaseExpiresAt = null;
        if (outcome == EtlTerminalOutcome.Succeeded)
        {
            if (run.Status != (int)EtlRunStatus.Running || run.OutstandingRecords != 0 || run.OutstandingBytes != 0 || run.RejectedRecords != 0 ||
                invocation.Artifact.FormatVersion == EtlArtifactV2Json.FormatVersion && !await ConnectorIsCompleteAsync(key, cancellationToken) ||
                await db.Partitions.AnyAsync(x => x.ScopeKey == key.ScopeKey && x.RunKey == key.RunKey && x.NextOrdinal != x.EndOrdinalExclusive, cancellationToken) ||
                await db.Reservations.AnyAsync(x => x.ScopeKey == key.ScopeKey && x.RunKey == key.RunKey && !x.Settled, cancellationToken))
                return new(EtlStoreStatus.Conflict, null);
        }
        else if (outcome is EtlTerminalOutcome.BusinessError or EtlTerminalOutcome.Faulted)
        {
            var preAdmissionRefusal = proposal is null && run.BindingsJson is null && !connector && currentStatus == EtlRunStatus.Preparing;
            if (!preAdmissionRefusal)
            {
                if (proposal is null || await FailureAuthorityExpiryAsync(proposal, NowTicks(), cancellationToken) is not { } expiry)
                    return new(EtlStoreStatus.LeaseLost, null);
                failureLeaseExpiresAt = expiry;
            }
        }

        run.Status = outcome switch
        {
            EtlTerminalOutcome.Succeeded => (int)EtlRunStatus.Succeeded,
            EtlTerminalOutcome.BusinessError => (int)EtlRunStatus.BusinessError,
            EtlTerminalOutcome.Faulted => (int)EtlRunStatus.Faulted,
            EtlTerminalOutcome.Cancelled => (int)EtlRunStatus.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };
        run.PreparationOwner = null;
        run.PreparationExpiresAtUtcTicks = null;
        run.Revision = checked(run.Revision + 1);
        run.UpdatedAtUtcTicks = now;
        AddTerminalRows(run, result, now);
        await db.SaveChangesAsync(cancellationToken);
        if (failureLeaseExpiresAt is { } expiration && expiration <= NowTicks()) return new(EtlStoreStatus.LeaseLost, null);
        await tx.CommitAsync(cancellationToken);
        return new(EtlStoreStatus.Applied, result);
    }

    private async ValueTask<long?> FailureAuthorityExpiryAsync(EtlTerminalProposal proposal, long now, CancellationToken cancellationToken)
    {
        if (proposal.PreparationOwner is not null && proposal.PreparationFence is { } prepFence &&
            proposal.PartitionOrdinal is null && proposal.PartitionOwner is null && proposal.PartitionFence is null)
        {
            var run = await FindRunRowAsync(proposal.Scope, proposal.RunId, cancellationToken);
            return run is not null && HasPreparationLease(run,
                new(proposal.Scope, proposal.RunId, proposal.PreparationOwner, prepFence, DateTimeOffset.MinValue), now)
                ? run.PreparationExpiresAtUtcTicks
                : null;
        }
        if (proposal.PreparationOwner is null && proposal.PreparationFence is null && proposal.PartitionOrdinal is { } ordinal &&
            proposal.PartitionOwner is not null && proposal.PartitionFence is { } fence)
        {
            var run = await FindRunRowAsync(proposal.Scope, proposal.RunId, cancellationToken);
            if (run?.Status != (int)EtlRunStatus.Running) return null;
            if (await IsConnectorRunAsync(RunKey(proposal.Scope, proposal.RunId), cancellationToken))
                return await ConnectorFailureExpiryAsync(proposal, now, cancellationToken);
            var partition = await FindPartitionRowAsync(proposal.Scope, proposal.RunId, ordinal, cancellationToken);
            return partition is not null && HasPartitionLease(partition,
                new(proposal.Scope, proposal.RunId, ordinal, proposal.PartitionOwner, fence, DateTimeOffset.MinValue,
                    partition.StartOrdinal, partition.EndOrdinalExclusive, partition.NextOrdinal), now)
                ? partition.ExpiresAtUtcTicks
                : null;
        }
        return null;
    }

    // Each view comes from one statement snapshot; no reader-held write transaction or N+1 component reads.
    private IQueryable<RunSnapshot> RunSnapshots(string scopeKey) =>
        from run in db.Runs.AsNoTracking()
        join prepared in db.PreparedRuns.AsNoTracking()
            on new { run.ScopeKey, run.RunKey } equals new { prepared.ScopeKey, prepared.RunKey } into preparedRows
        from prepared in preparedRows.DefaultIfEmpty()
        join terminal in db.Terminals.AsNoTracking()
            on new { run.ScopeKey, run.RunKey } equals new { terminal.ScopeKey, terminal.RunKey } into terminalRows
        from terminal in terminalRows.DefaultIfEmpty()
        where run.ScopeKey == scopeKey
        select new RunSnapshot { Run = run, Prepared = prepared, Terminal = terminal };

    private sealed class RunSnapshot
    {
        public required EtlRuntimeRunRecord Run { get; init; }
        public EtlRuntimePreparedRecord? Prepared { get; init; }
        public EtlRuntimeTerminalRecord? Terminal { get; init; }
    }

    private async ValueTask<EtlRunView?> ReadRunViewSnapshotAsync(EtlScope scope, string runId, CancellationToken cancellationToken)
    {
        var key = RunKey(scope, runId);
        var snapshot = await RunSnapshots(key.ScopeKey).SingleOrDefaultAsync(x => x.Run.RunKey == key.RunKey, cancellationToken);
        if (snapshot is null) return null;
        ValidateRun(snapshot.Run, scope, runId);
        return BuildRunView(snapshot.Run, snapshot.Prepared, snapshot.Terminal);
    }

    private async ValueTask<EtlRunView> ReadViewAsync(EtlRuntimeRunRecord row, CancellationToken cancellationToken)
    {
        var preparedRow = await db.PreparedRuns.SingleOrDefaultAsync(x => x.ScopeKey == row.ScopeKey && x.RunKey == row.RunKey, cancellationToken);
        var terminalRow = await db.Terminals.SingleOrDefaultAsync(x => x.ScopeKey == row.ScopeKey && x.RunKey == row.RunKey, cancellationToken);
        return BuildRunView(row, preparedRow, terminalRow);
    }

    private EtlRunView BuildRunView(EtlRuntimeRunRecord row, EtlRuntimePreparedRecord? preparedRow, EtlRuntimeTerminalRecord? terminalRow)
    {
        EnsureSchema(row.SchemaVersion);
        var invocation = EtlRuntimeRecordCodec.ReadInvocation(row.InvocationJson);
        var fingerprint = EtlRuntimeIdentity.InvocationFingerprint(invocation);
        if (!StringComparer.Ordinal.Equals(fingerprint, row.InvocationFingerprint))
            throw new InvalidDataException("The ETL runtime invocation fingerprint does not match its stored document.");
        var bindings = EtlRuntimeRecordCodec.ReadBindings(row.BindingsJson);
        if (bindings is not null) EtlRuntimeRecordCodec.ValidateStoredBindings(invocation, bindings);
        EtlPreparedRun? prepared = preparedRow is null ? null : EtlRuntimeRecordCodec.ReadPrepared(preparedRow);
        EtlTerminalResult? terminal = terminalRow is null ? null : ReadTerminal(terminalRow);
        var status = (EtlRunStatus)row.Status;
        if (!Enum.IsDefined(status)) throw new InvalidDataException("The ETL runtime row has an unsupported run status.");
        return new(invocation, bindings, status, prepared, row.OutstandingRecords, row.OutstandingBytes,
            row.AcceptedRecords, row.RejectedRecords, row.FilteredRecords, terminal);
    }

    private static void ValidateAuthorityShape(EtlTerminalProposal proposal)
    {
        var preparation = proposal.PreparationOwner is not null || proposal.PreparationFence is not null;
        var partition = proposal.PartitionOrdinal is not null || proposal.PartitionOwner is not null || proposal.PartitionFence is not null;
        if (proposal.Outcome == EtlTerminalOutcome.Succeeded)
        {
            if (preparation || partition || proposal.Diagnostic is not null)
                throw new ArgumentException("A success proposal cannot carry a failure claim or diagnostic.", nameof(proposal));
            return;
        }

        var hasPreparationClaim = proposal.PreparationOwner is not null && proposal.PreparationFence is > 0 && !partition;
        var hasPartitionClaim = !preparation && proposal.PartitionOrdinal is >= 0 &&
                                proposal.PartitionOwner is not null && proposal.PartitionFence is > 0;
        if (!hasPreparationClaim && !hasPartitionClaim)
            throw new ArgumentException("A failure proposal must carry exactly one complete current claim.", nameof(proposal));
        if (hasPreparationClaim)
            EtlRuntimeRecordCodec.ValidateIdentity(proposal.PreparationOwner, "preparation worker id");
        if (hasPartitionClaim)
            EtlRuntimeRecordCodec.ValidateIdentity(proposal.PartitionOwner, "partition worker id");
    }

    private EtlTerminalResult ReadTerminal(EtlRuntimeTerminalRecord row)
    {
        EnsureSchema(row.SchemaVersion);
        if (!StringComparer.Ordinal.Equals(row.ResultHash, EtlRuntimeRecordCodec.HashText(row.ResultJson)))
            throw new InvalidDataException("The ETL terminal result hash does not match its persisted document.");
        var result = EtlRuntimeRecordCodec.Read<EtlTerminalResult>(row.ResultJson);
        if (!Enum.IsDefined(result.Outcome) || (int)result.Outcome != row.Outcome ||
            result.SettledAt.UtcDateTime.Ticks != row.SettledAtUtcTicks)
            throw new InvalidDataException("The ETL terminal result does not match its persisted row.");
        return result;
    }

    private EtlTerminalResult BuildTerminal(EtlInvocation invocation, EtlTerminalOutcome outcome, EtlRunDiagnostic? diagnostic, EtlRuntimeRunRecord run, long now)
    {
        var seed = EtlRuntimeRecordCodec.Write(new { Version = "etl-terminal-v1", run.InvocationFingerprint, outcome, diagnostic,
            run.AcceptedRecords, run.RejectedRecords, run.FilteredRecords });
        return new(HashAscii(seed), outcome, run.AcceptedRecords, run.RejectedRecords, run.FilteredRecords,
            diagnostic, EtlRuntimeRecordCodec.ReadUtc(now));
    }

    private void AddTerminalRows(EtlRuntimeRunRecord run, EtlTerminalResult result, long now)
    {
        var scopeKey = run.ScopeKey;
        var runKey = run.RunKey;
        var resultJson = EtlRuntimeRecordCodec.Write(result);
        db.Terminals.Add(new EtlRuntimeTerminalRecord
        {
            ScopeKey = scopeKey, RunKey = runKey, ResultId = result.ResultId,
            Outcome = (int)result.Outcome, ResultJson = resultJson, ResultHash = EtlRuntimeRecordCodec.HashText(resultJson),
            SettledAtUtcTicks = now, SchemaVersion = EtlRuntimeEfModule.SchemaVersion
        });
        db.Outbox.Add(new EtlRuntimeOutboxRecord
        {
            ScopeKey = scopeKey, RunKey = runKey, DeliveryId = DeliveryId(result.ResultId),
            PayloadHash = OutboxHash(run.InvocationJson, resultJson), Fence = 0,
            SchemaVersion = EtlRuntimeEfModule.SchemaVersion
        });
    }

    private static EtlRuntimePreparedRecord PreparedRecord(string scopeKey, string runKey, EtlPreparedRun prepared) => new()
    {
        ScopeKey = scopeKey, RunKey = runKey, FormatVersion = prepared.FormatVersion,
        SourceKey = prepared.Source.Key, SourceHash = prepared.Source.ContentHash, SourceLength = prepared.Source.ByteLength,
        LookupKey = prepared.Lookup.Key, LookupHash = prepared.Lookup.ContentHash, LookupLength = prepared.Lookup.ByteLength,
        SourceIndexKey = prepared.SourceIndex.Key, SourceIndexHash = prepared.SourceIndex.ContentHash,
        SourceIndexLength = prepared.SourceIndex.ByteLength, SourceRecordCount = prepared.SourceRecordCount,
        SchemaVersion = EtlRuntimeEfModule.SchemaVersion
    };

    private async ValueTask<bool> EffectsMatchAsync(string scopeKey, string runKey, int ordinal, EtlBatchAcceptance acceptance, CancellationToken cancellationToken)
    {
        var rows = await db.Manifest.Where(x => x.ScopeKey == scopeKey && x.RunKey == runKey && x.PartitionOrdinal == ordinal &&
            x.BatchStartOrdinal == acceptance.Batch.StartOrdinal).ToListAsync(cancellationToken);
        if (rows.Count != acceptance.Effects.Count || acceptance.Effects.Count > 2 ||
            acceptance.Effects.Any(effect => effect is null || effect.Chunk is null) ||
            acceptance.Effects.Select(effect => effect.Route).Distinct().Count() != acceptance.Effects.Count)
            return false;
        var accepted = rows.Where(row => row.Route == (int)EtlOutputRoute.Main).Sum(row => row.RecordCount);
        var rejected = rows.Where(row => row.Route == (int)EtlOutputRoute.Error).Sum(row => row.RecordCount);
        var filtered = acceptance.Batch.EndOrdinalExclusive - acceptance.Batch.StartOrdinal - accepted - rejected;
        if (acceptance.AcceptedRecords != accepted || acceptance.RejectedRecords != rejected || acceptance.FilteredRecords != filtered)
            return false;
        return acceptance.Effects.All(effect => rows.Any(row => row.Route == (int)effect.Route && ManifestMatches(row, acceptance.Batch, effect)));
    }

    private static bool ManifestMatches(EtlRuntimeManifestRecord row, EtlBatchDescriptor batch, EtlOutputEffect effect) =>
        row.BatchId == batch.BatchId && row.ChunkKey == effect.Chunk.Key && row.ContentHash == effect.Chunk.ContentHash &&
        row.ByteLength == effect.Chunk.ByteLength && row.RecordCount == effect.RecordCount;

    private static bool ReservationMatches(EtlRuntimeReservationRecord row, EtlBatchDescriptor batch) =>
        row.BatchId == batch.BatchId && row.StartOrdinal == batch.StartOrdinal && row.EndOrdinalExclusive == batch.EndOrdinalExclusive &&
        row.InputBytes == batch.InputBytes && row.InputHash == batch.InputHash;

    private async ValueTask<EtlRuntimeRunRecord?> FindRunRowAsync(EtlScope scope, string runId, CancellationToken cancellationToken)
    {
        var key = RunKey(scope, runId);
        var row = await db.Runs.SingleOrDefaultAsync(x => x.ScopeKey == key.ScopeKey && x.RunKey == key.RunKey, cancellationToken);
        if (row is not null) ValidateRun(row, scope, runId);
        return row;
    }

    private async ValueTask<EtlRuntimePartitionRecord?> FindPartitionRowAsync(EtlScope scope, string runId, int ordinal, CancellationToken cancellationToken)
    {
        var key = RunKey(scope, runId);
        var row = await db.Partitions.SingleOrDefaultAsync(x => x.ScopeKey == key.ScopeKey && x.RunKey == key.RunKey && x.PartitionOrdinal == ordinal, cancellationToken);
        if (row is not null) EnsureSchema(row.SchemaVersion);
        return row;
    }

    private bool HasPreparationLease(EtlRuntimeRunRecord row, EtlPreparationLease lease, long now) =>
        row.Status == (int)EtlRunStatus.Preparing && row.PreparationOwner == EtlRuntimeRecordCodec.EncodeIdentity(lease.Owner, "worker id") &&
        row.PreparationFence == lease.Fence && row.PreparationExpiresAtUtcTicks is { } expiry && expiry > now;

    private bool HasPartitionLease(EtlRuntimePartitionRecord row, EtlPartitionLease lease, long now, bool requireCursor = true) =>
        row.Owner == EtlRuntimeRecordCodec.EncodeIdentity(lease.Owner, "worker id") && row.Fence == lease.Fence &&
        row.ExpiresAtUtcTicks is { } expiry && expiry > now && row.StartOrdinal == lease.StartOrdinal &&
        row.EndOrdinalExclusive == lease.EndOrdinalExclusive && (!requireCursor || row.NextOrdinal == lease.NextOrdinal);

    private EtlPartitionLease ToPartitionLease(EtlScope scope, string runId, EtlRuntimePartitionRecord row, string owner) =>
        new(scope, runId, row.PartitionOrdinal, owner, row.Fence, EtlRuntimeRecordCodec.ReadUtc(row.ExpiresAtUtcTicks!.Value),
            row.StartOrdinal, row.EndOrdinalExclusive, row.NextOrdinal);

    private EtlRunAdmission ReadAdmission(EtlRuntimeRunRecord run)
    {
        var invocation = EtlRuntimeRecordCodec.ReadInvocation(run.InvocationJson);
        var bindings = EtlRuntimeRecordCodec.ReadBindings(run.BindingsJson)
            ?? throw new InvalidDataException("An admitted ETL run has no pinned bindings.");
        EtlRuntimeRecordCodec.ValidateStoredBindings(invocation, bindings);
        return new(invocation, bindings);
    }

    private static (string ScopeKey, string RunKey) RunKey(EtlScope scope, string runId)
    {
        ArgumentNullException.ThrowIfNull(scope);
        EtlRuntimeRecordCodec.ValidateIdentity(runId, "run id");
        var identity = EtlRuntimeRecordCodec.Scope(scope);
        return (identity.Key, EtlRuntimeRecordCodec.EncodeIdentity(runId, "run id"));
    }

    private void ValidatePage(EtlScope scope, int take)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (take <= 0 || take > options.MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(take));
    }

    private static void ValidateRun(EtlRuntimeRunRecord row, EtlScope scope, string runId)
    {
        EtlRuntimeRecordCodec.EnsureScopeResidual(row, scope);
        if (!StringComparer.Ordinal.Equals(EtlRuntimeRecordCodec.DecodeIdentity(row.RunKey, "run id"), runId) ||
            !StringComparer.Ordinal.Equals(EtlRuntimeRecordCodec.DecodeIdentity(row.RunId, "run id"), runId))
            throw new InvalidDataException("The ETL runtime row does not match its run identity.");
        EnsureSchema(row.SchemaVersion);
    }

    private static long Expiry(long now, TimeSpan duration) => checked(now + duration.Ticks);
    private long NowTicks() => clock.GetUtcNow().UtcDateTime.Ticks;
    private static string HashAscii(string value) => EtlRuntimeRecordCodec.HashText(value);
    private static string DeliveryId(string resultId) => HashAscii("etl-delivery-v1|" + resultId);
    private static string OutboxHash(string invocationJson, string resultJson) => EtlRuntimeJson.ResultEnvelopeHash(invocationJson, resultJson);
    private static void EnsureSchema(string schemaVersion) => EtlRuntimeRecordCodec.EnsureSchema(schemaVersion);
}
