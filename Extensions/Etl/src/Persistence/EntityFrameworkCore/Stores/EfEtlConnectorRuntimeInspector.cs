using System.Data;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfEtlRuntimeStore
{
    public async ValueTask<EtlConnectorRunInspection?> FindConnectorInspectionAsync(EtlScope scope, string runId,
        CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        var key = RunKey(scope, runId);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var run = await FindConnectorRunAsync(scope, runId, cancellationToken);
        if (run is null) return null;

        var counts = await AcceptedEffectCounts(key.ScopeKey, key.RunKey, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(run, counts.Main, counts.Errors);
    }

    public async ValueTask<IReadOnlyList<EtlConnectorPartitionInspection>> ListConnectorPartitionsAsync(EtlScope scope,
        string runId, int afterPartitionOrdinal, int take, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        ValidatePage(scope, take);
        if (afterPartitionOrdinal < -1) throw new ArgumentOutOfRangeException(nameof(afterPartitionOrdinal));
        var key = RunKey(scope, runId);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var run = await FindConnectorRunAsync(scope, runId, cancellationToken);
        if (run is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return Array.Empty<EtlConnectorPartitionInspection>();
        }

        var rows = await db.ConnectorPartitions.AsNoTracking()
            .Where(row => row.ScopeKey == key.ScopeKey && row.RunKey == key.RunKey && row.PartitionOrdinal > afterPartitionOrdinal)
            .OrderBy(row => row.PartitionOrdinal)
            .Take(take)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return Array.Empty<EtlConnectorPartitionInspection>();
        }

        var ordinals = rows.Select(row => row.PartitionOrdinal).ToArray();
        var effectCounts = await db.ConnectorBatches.AsNoTracking()
            .Where(row => row.ScopeKey == key.ScopeKey && row.RunKey == key.RunKey && row.Settled && ordinals.Contains(row.PartitionOrdinal))
            .GroupBy(row => row.PartitionOrdinal)
            .Select(group => new
            {
                PartitionOrdinal = group.Key,
                Main = group.Sum(row => row.MainEffectKind == null ? 0L : 1L),
                Errors = group.Sum(row => row.ErrorEffectKind == null ? 0L : 1L)
            })
            .ToDictionaryAsync(item => item.PartitionOrdinal, item => (Main: item.Main, Errors: item.Errors), cancellationToken);

        var pipeline = EtlConnectorRecordCodec.Pipeline(run.Run.Invocation);
        var prepared = run.Prepared ?? throw new InvalidDataException("Connector partitions exist without prepared input.");
        var admission = new EtlConnectorRunAdmission(run.Run.Invocation, run.Bindings);
        var result = new List<EtlConnectorPartitionInspection>(rows.Count);
        foreach (var row in rows)
        {
            EnsureSchema(row.SchemaVersion);
            var position = await ReadConnectorPositionAsync(row, pipeline, run.Bindings, prepared, cancellationToken);
            var owed = await ReadConnectorOwedBatchAsync(row, admission, pipeline, prepared, position, cancellationToken);
            if ((row.Owner is null) != (row.ExpiresAtUtcTicks is null) || row.Owner is not null && row.Fence <= 0)
                throw new InvalidDataException("The connector partition claim metadata is inconsistent.");
            var expires = row.ExpiresAtUtcTicks is { } expiresAt ? EtlRuntimeRecordCodec.ReadUtc(expiresAt) : (DateTimeOffset?)null;
            // Claim metadata describes the last persisted owner/fence even after its lease expires.
            // Callers can compare the separately reported expiry with their clock.
            var hasClaim = row.Owner is not null;
            var count = effectCounts.GetValueOrDefault(row.PartitionOrdinal);
            var file = position as EtlConnectorFilePosition;
            EtlConnectorHttpProgress? http = null;
            if (position is EtlConnectorHttpPosition httpPosition)
            {
                var request = await Requests(row).SingleOrDefaultAsync(item => item.PageSequence == httpPosition.PageSequence, cancellationToken)
                    ?? throw new InvalidDataException("The current HTTP request is missing.");
                var (_, fetch) = ReadHttpRequest(request, pipeline.Http);
                http = new(httpPosition.PageSequence, httpPosition.NextItem, httpPosition.Page?.RecordCount,
                    httpPosition.Exhausted, fetch is null ? null : FetchInspection(fetch));
            }
            else if (file is null)
                throw new InvalidDataException("The connector partition position is unsupported.");

            result.Add(new EtlConnectorPartitionInspection(row.PartitionOrdinal, file, http, hasClaim,
                row.Owner is null ? null : row.Fence, expires,
                row.NotBeforeUtcTicks is { } notBefore ? EtlRuntimeRecordCodec.ReadUtc(notBefore) : null,
                owed is null ? null : OwedInspection(owed), count.Main, count.Errors));
        }

        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<(long Main, long Errors)> AcceptedEffectCounts(string scopeKey, string runKey,
        CancellationToken cancellationToken)
    {
        var query = db.ConnectorBatches.AsNoTracking().Where(row => row.ScopeKey == scopeKey && row.RunKey == runKey && row.Settled);
        var counts = await query.GroupBy(_ => 1).Select(group => new
        {
            Main = group.Sum(row => row.MainEffectKind == null ? 0L : 1L),
            Errors = group.Sum(row => row.ErrorEffectKind == null ? 0L : 1L)
        }).SingleOrDefaultAsync(cancellationToken);
        return counts is null ? (0, 0) : (counts.Main, counts.Errors);
    }

    private static EtlConnectorRetryInspection FetchInspection(EtlConnectorHttpSourceFetch fetch)
    {
        var lastStatus = fetch.Capture is not null ? 200 : fetch.Attempts.LastOrDefault()?.Outcome?.StatusCode;
        return new(JsonNamingPolicy.CamelCase.ConvertName(fetch.Phase.ToString()), fetch.Attempts.Count,
            fetch.Deadline, fetch.NotBefore, lastStatus);
    }

    private static EtlConnectorOwedInspection OwedInspection(EtlConnectorBatchReservation reservation)
    {
        var records = reservation.Batch.Range switch
        {
            EtlConnectorFileBatchRange range => checked(range.EndOrdinalExclusive - range.StartOrdinal),
            EtlConnectorHttpBatchRange range => checked((long)range.EndItemExclusive - range.StartItem),
            _ => throw new InvalidDataException("The retained connector batch range is unsupported.")
        };
        var output = reservation.Output;
        return new(records, reservation.Batch.InputBytes, output is not null,
            EffectInspection(output?.Main, reservation.HttpDeliveries?.Main, reservation.FilePublications.Main),
            EffectInspection(output?.Errors, reservation.HttpDeliveries?.Errors, reservation.FilePublications.Errors));
    }

    private static EtlConnectorEffectInspection? EffectInspection(EtlConnectorRouteOutput? output,
        EtlConnectorHttpDelivery? delivery, bool published)
    {
        if (output is null) return null;
        var state = output.Kind switch
        {
            EtlConnectorKind.Http => delivery?.Phase == EtlHttpDeliveryPhase.Acknowledged ? "acknowledged" : "retained",
            EtlConnectorKind.Jsonl => published ? "published" : "retained",
            _ => throw new InvalidDataException("The retained connector route kind is unsupported.")
        };
        var retry = output.Kind == EtlConnectorKind.Http && delivery is not null
            ? new EtlConnectorRetryInspection(JsonNamingPolicy.CamelCase.ConvertName(delivery.Phase.ToString()),
                delivery.Attempts.Count, delivery.Deadline, delivery.NotBefore,
                delivery.Attempts.LastOrDefault()?.Response?.StatusCode)
            : null;
        return new(output.Kind, output.RecordCount, state, retry);
    }
}
