using System.Text.Json;
using Elsa.Etl.Api;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Api.Models;

namespace Elsa.Etl.Runtime.Api;

internal sealed partial class EtlRuntimeOperations
{
    public async Task<EtlRunPage<EtlRunConnectorPartitionView>> ListConnectorPartitionsAsync(
        EtlRunPartitionsRequest request, CancellationToken cancellationToken)
    {
        var scope = requestScope.Get();
        var cursor = Cursor(request.Cursor, request.Limit, "connector-partitions", scope, request.Id);
        var run = await FindRunAsync(scope, request.Id, cancellationToken);
        if (run.Invocation.Artifact.FormatVersion != EtlArtifactV2Json.FormatVersion)
            throw new EtlApiException(409, "etl.connector-run-required");
        var inspector = Required<IEtlConnectorRuntimeInspector>();
        return await PartitionPageAsync(request, scope, "connector-partitions", cursor, inspector.MaximumPageSize,
            (after, take, token) => inspector.ListConnectorPartitionsAsync(scope, request.Id, after, take, token),
            row => row.PartitionOrdinal, ConnectorPartition, cancellationToken);
    }

    private static EtlRunDetailView ConnectorDetail(EtlConnectorRunInspection inspection)
    {
        var connector = inspection.Run;
        var bindings = connector.Bindings;
        return Detail(connector.Run) with
        {
            SourceRecordCount = connector.Prepared?.File?.RecordCount,
            BindingAliases = new(bindings.Source.Revision.Alias, bindings.Lookup?.Revision.Alias,
                bindings.Main.Revision.Alias, bindings.Errors.Revision.Alias),
            SourceInput = bindings.Source.Kind == EtlConnectorKind.Csv
                ? InputIdentity(bindings.Source.Revision, connector.Prepared?.File?.Source) : null,
            LookupInput = InputIdentity(bindings.Lookup?.Revision, connector.Prepared?.Lookup),
            Connector = new(Kind(bindings.Source.Kind), Kind(bindings.Main.Kind), Kind(bindings.Errors.Kind),
                inspection.AcceptedMainEffects, inspection.AcceptedErrorEffects)
        };
    }

    private static EtlRunConnectorPartitionView ConnectorPartition(EtlConnectorPartitionInspection row)
    {
        if ((row.File is null) == (row.Http is null) || row.AcceptedMainEffects < 0 || row.AcceptedErrorEffects < 0 ||
            row.IsClaimed != (row.ClaimFence is > 0 && row.ClaimExpiresAt is not null) ||
            !row.IsClaimed && (row.ClaimFence is not null || row.ClaimExpiresAt is not null) ||
            row.File is { } range && (range.StartOrdinal < 0 || range.NextOrdinal < range.StartOrdinal || range.NextOrdinal > range.EndOrdinalExclusive) ||
            row.Http is { } progress && (progress.PageSequence < 0 || progress.NextItem < 0 || progress.PageRecordCount < progress.NextItem || progress.PageRecordCount is null && progress.NextItem != 0))
            throw ProviderContractError();
        return new(row.PartitionOrdinal,
            row.File is { } file ? new(file.StartOrdinal, file.EndOrdinalExclusive, file.NextOrdinal) : null,
            row.Http is { } http ? new(http.PageSequence, http.NextItem, http.PageRecordCount, http.Exhausted, Retry(http.Fetch, source: true)) : null,
            row.IsClaimed, row.ClaimFence, row.ClaimExpiresAt, row.NotBefore,
            Owed(row.OwedBatch),
            row.AcceptedMainEffects, row.AcceptedErrorEffects);
    }

    private static EtlRunConnectorOwedView? Owed(EtlConnectorOwedInspection? owed)
    {
        if (owed is null) return null;
        var main = owed.Main?.RecordCount ?? 0;
        var errors = owed.Errors?.RecordCount ?? 0;
        if (owed.RecordCount <= 0 || owed.InputBytes <= 0 ||
            !owed.OutputRetained && (owed.Main is not null || owed.Errors is not null) ||
            main > owed.RecordCount || errors > owed.RecordCount - main)
            throw ProviderContractError();
        return new(owed.RecordCount, owed.InputBytes, owed.OutputRetained, Effect(owed.Main), Effect(owed.Errors));
    }

    private static EtlRunEffectView? Effect(EtlConnectorEffectInspection? effect)
    {
        if (effect is null) return null;
        if (effect.RecordCount <= 0 || effect.Kind is not (EtlConnectorKind.Http or EtlConnectorKind.Jsonl) ||
            effect.State is not ("retained" or "acknowledged" or "published") ||
            effect.State == "acknowledged" && effect.Kind != EtlConnectorKind.Http ||
            effect.State == "published" && effect.Kind != EtlConnectorKind.Jsonl ||
            effect.Kind != EtlConnectorKind.Http && effect.Retry is not null ||
            effect.Kind == EtlConnectorKind.Http &&
            ((effect.State == "acknowledged") != (effect.Retry?.Phase == "acknowledged")))
            throw ProviderContractError();
        return new(Kind(effect.Kind), effect.RecordCount, effect.State, Retry(effect.Retry));
    }

    private static EtlRunRetryView? Retry(EtlConnectorRetryInspection? retry, bool source = false)
    {
        if (retry is null) return null;
        if (retry.Phase is not ("inFlight" or "waiting" or "failed" or "captured" or "acknowledged") ||
            retry.AttemptCount < 1 || retry.LastStatusCode is < 100 or > 599 ||
            (retry.Phase == "waiting") != (retry.NotBefore is not null) ||
            retry.NotBefore >= retry.Deadline ||
            retry.Phase == "captured" && retry.LastStatusCode != 200 ||
            retry.Phase == (source ? "acknowledged" : "captured"))
            throw ProviderContractError();
        return new(retry.Phase, retry.AttemptCount, retry.Deadline, retry.NotBefore, retry.LastStatusCode);
    }

    private static string Kind(EtlConnectorKind kind) => Enum.IsDefined(kind)
        ? JsonNamingPolicy.CamelCase.ConvertName(kind.ToString()) : throw ProviderContractError();
}
