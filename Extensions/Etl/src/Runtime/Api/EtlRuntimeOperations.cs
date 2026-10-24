using System.Collections.Generic;
using System.Text.Json;
using Elsa.Etl.Api;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Api.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Runtime.Api;

internal sealed partial class EtlRuntimeOperations(
    IServiceProvider services,
    EtlRequestScope requestScope,
    [FromKeyedServices(EtlRuntimeApi.OwnerId)] EtlCursorCodec cursors) : IEtlRuntimeOperations
{
    public async Task<EtlRunPage<EtlRunSummaryView>> ListRunsAsync(EtlRunPageRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = requestScope.Get();
        var cursor = Cursor(request.Cursor, request.Limit, "runs", scope);
        var store = Required<IEtlRuntimeStore>();
        var maximumPageSize = StorePageSize(store);
        var rows = new List<EtlRunView>(request.Limit + 1);
        var seenRunIds = new HashSet<string>(StringComparer.Ordinal);
        var afterRunId = cursor?.LastId;
        if (afterRunId is not null) seenRunIds.Add(afterRunId);
        while (rows.Count < request.Limit + 1)
        {
            var take = Math.Min(maximumPageSize, request.Limit + 1 - rows.Count);
            var page = await store.ListRunsAsync(scope, afterRunId, take, cancellationToken);
            ValidatePageSize(page.Count, take);
            if (page.Count == 0)
                break;

            foreach (var run in page)
            {
                var runId = run.Invocation.RunId;
                if (string.IsNullOrEmpty(runId) || !seenRunIds.Add(runId))
                    throw ProviderContractError();
                rows.Add(run);
                afterRunId = runId;
            }

            if (page.Count < take)
                break;
        }
        var items = rows.Take(request.Limit).Select(Summary).ToArray();
        var next = rows.Count > request.Limit
            ? cursors.Encode(State("runs", scope, request.Limit) with { LastId = items[^1].RunId })
            : null;
        return new(items, next);
    }

    public async Task<EtlRunDetailView> GetRunAsync(EtlRunRequest request, CancellationToken cancellationToken)
    {
        var scope = requestScope.Get();
        var run = await FindRunAsync(scope, request.Id, cancellationToken);
        if (run.Invocation.Artifact.FormatVersion != EtlArtifactV2Json.FormatVersion)
            return Detail(run);
        var inspection = await Required<IEtlConnectorRuntimeInspector>().FindConnectorInspectionAsync(scope, request.Id, cancellationToken);
        // Rejected admission retains a terminal base run without connector pins or partitions.
        if (inspection is null)
            return run.TerminalResult is not null ? Detail(run) : throw ProviderContractError();
        if (inspection.Run.Run.Invocation.Scope != scope || inspection.Run.Run.Invocation.RunId != request.Id ||
            inspection.Run.Run.Invocation.Artifact.FormatVersion != EtlArtifactV2Json.FormatVersion ||
            inspection.AcceptedMainEffects < 0 || inspection.AcceptedErrorEffects < 0)
            throw ProviderContractError();
        return ConnectorDetail(inspection);
    }

    public async Task<EtlRunPage<EtlRunPartitionView>> ListPartitionsAsync(EtlRunPartitionsRequest request, CancellationToken cancellationToken)
    {
        var scope = requestScope.Get();
        var cursor = Cursor(request.Cursor, request.Limit, "partitions", scope, request.Id);
        var run = await FindRunAsync(scope, request.Id, cancellationToken);
        if (run.Invocation.Artifact.FormatVersion == EtlArtifactV2Json.FormatVersion)
            throw new EtlApiException(409, "etl.connector-inspection-required");
        var store = Required<IEtlRuntimeStore>();
        return await PartitionPageAsync(request, scope, "partitions", cursor, StorePageSize(store),
            (after, take, token) => store.ListPartitionsAsync(scope, request.Id, after, take, token),
            row => row.PartitionOrdinal, Partition, cancellationToken);
    }

    private async Task<EtlRunPage<TView>> PartitionPageAsync<TSource, TView>(EtlRunPartitionsRequest request,
        EtlScope scope, string kind, EtlCursorState? cursor, int maximumPageSize,
        Func<int, int, CancellationToken, ValueTask<IReadOnlyList<TSource>>> read,
        Func<TSource, int> ordinal, Func<TSource, TView> project, CancellationToken cancellationToken)
    {
        if (maximumPageSize <= 0) throw ProviderContractError();
        var rows = new List<TSource>(request.Limit + 1);
        var after = cursor?.LastOrdinal ?? -1;
        while (rows.Count < request.Limit + 1)
        {
            var take = Math.Min(maximumPageSize, request.Limit + 1 - rows.Count);
            var page = await read(after, take, cancellationToken);
            ValidatePageSize(page.Count, take);
            foreach (var row in page)
            {
                var nextOrdinal = ordinal(row);
                if (nextOrdinal <= after) throw ProviderContractError();
                rows.Add(row);
                after = nextOrdinal;
            }
            if (page.Count < take) break;
        }
        var items = rows.Take(request.Limit).Select(project).ToArray();
        var next = rows.Count > request.Limit
            ? cursors.Encode(State(kind, scope, request.Limit, request.Id) with { LastOrdinal = ordinal(rows[request.Limit - 1]) })
            : null;
        return new(items, next);
    }

    public async Task<EtlRunCancellationView> CancelAsync(EtlRunRequest request, CancellationToken cancellationToken)
    {
        var scope = requestScope.Get();
        var run = await FindRunAsync(scope, request.Id, cancellationToken);
        var settlement = await Required<IEtlRuntimeStore>().CancelAsync(run.Invocation, cancellationToken);

        if (settlement.Result is { } terminal && settlement.Status is (EtlStoreStatus.Applied or EtlStoreStatus.Replay or EtlStoreStatus.Conflict))
            return new(terminal.Outcome == EtlTerminalOutcome.Cancelled, Terminal(terminal));

        throw settlement.Status switch
        {
            EtlStoreStatus.NotFound => new EtlApiException(404, "etl.run-not-found"),
            EtlStoreStatus.Invalid => new EtlApiException(400, "etl.request-invalid"),
            EtlStoreStatus.Conflict or EtlStoreStatus.LeaseLost or EtlStoreStatus.BudgetExceeded or EtlStoreStatus.Terminal =>
                new EtlApiException(409, "etl.run-conflict"),
            _ => new EtlApiException(500, "etl.operation-failed")
        };
    }

    private async Task<EtlRunView> FindRunAsync(EtlScope scope, string id, CancellationToken cancellationToken) =>
        await Required<IEtlRuntimeStore>().FindRunAsync(scope, id, cancellationToken) ?? throw NotFound();

    private EtlCursorState? Cursor(string? token, int limit, string kind, EtlScope scope, string? resourceId = null)
    {
        if (limit is < 1 or > 200)
            throw new EtlApiException(400, "etl.page-limit-invalid");
        if (token is null)
            return null;

        EtlCursorState state;
        try { state = cursors.Decode(token); }
        catch (FormatException) { throw InvalidCursor(); }

        if (state.Kind != kind || state.ScopeKey != EtlRuntimeIdentity.ScopeKey(scope) ||
            state.ResourceId != resourceId || state.Limit != limit)
            throw InvalidCursor();

        var validBoundary = kind switch
        {
            "runs" => state.LastId is not null && state.LastOrdinal is null && state.LastVersion == 0 &&
                state.ThroughVersion == 0 && state.LastAlias is null && state.LastRole is null,
            "partitions" or "connector-partitions" => state.LastId is null && state.LastOrdinal is >= 0 && state.LastVersion == 0 &&
                state.ThroughVersion == 0 && state.LastAlias is null && state.LastRole is null,
            _ => false
        };
        return validBoundary ? state : throw InvalidCursor();
    }

    private T Required<T>() where T : class => services.GetService<T>() ?? throw new EtlApiException(503, "etl.provider-unavailable");

    private static int StorePageSize(IEtlRuntimeStore store) => store.MaximumPageSize > 0
        ? store.MaximumPageSize
        : throw ProviderContractError();

    private static void ValidatePageSize(int actual, int requested)
    {
        if (actual > requested)
            throw ProviderContractError();
    }

    private static EtlApiException ProviderContractError() => new(500, "etl.provider-invalid");

    private static EtlCursorState State(string kind, EtlScope scope, int limit, string? resourceId = null) =>
        new(1, kind, EtlRuntimeIdentity.ScopeKey(scope), resourceId, limit, null, 0, 0, null, null);

    private static EtlRunSummaryView Summary(EtlRunView run) => new(
        run.Invocation.RunId,
        Selection(run.Invocation.Artifact),
        Status(run.Status),
        run.Prepared?.SourceRecordCount,
        run.OutstandingRecords,
        run.OutstandingBytes,
        run.AcceptedRecords,
        run.RejectedRecords,
        run.FilteredRecords,
        Diagnostic(run.TerminalResult?.Diagnostic));

    private static EtlRunDetailView Detail(EtlRunView run) => new(
        run.Invocation.RunId,
        Selection(run.Invocation.Artifact),
        Status(run.Status),
        run.Prepared?.SourceRecordCount,
        run.OutstandingRecords,
        run.OutstandingBytes,
        run.AcceptedRecords,
        run.RejectedRecords,
        run.FilteredRecords,
        run.Bindings is { } bindings
            ? new(bindings.Source.Alias, bindings.Lookup.Alias, bindings.Output.Alias, bindings.Errors.Alias)
            : null,
        Diagnostic(run.TerminalResult?.Diagnostic),
        run.TerminalResult is { } terminal ? Terminal(terminal) : null)
    {
        FormatVersion = run.Invocation.Artifact.FormatVersion,
        SourceInput = InputIdentity(run.Bindings?.Source, run.Prepared?.Source),
        LookupInput = InputIdentity(run.Bindings?.Lookup, run.Prepared?.Lookup)
    };

    private static EtlRunInputIdentityView? InputIdentity(EtlBindingRevision? binding, EtlObjectReference? snapshot) =>
        binding is null ? null : new(binding.Revision, binding.ExpectedSha256, snapshot?.ContentHash, snapshot?.ByteLength);

    private static EtlRunPartitionView Partition(EtlPartitionView partition) => new(
        partition.PartitionOrdinal,
        partition.StartOrdinal,
        partition.EndOrdinalExclusive,
        partition.NextOrdinal,
        partition.Owner is not null,
        partition.Owner is not null ? partition.Fence : null,
        partition.Owner is not null ? partition.ExpiresAt : null,
        partition.OwedBatch is { } owed
            ? new(owed.Batch.StartOrdinal, owed.Batch.EndOrdinalExclusive, owed.Batch.InputBytes)
            : null);

    private static EtlRunTerminalResultView Terminal(EtlTerminalResult terminal) => new(
        JsonNamingPolicy.CamelCase.ConvertName(terminal.Outcome.ToString()),
        terminal.AcceptedRecords,
        terminal.RejectedRecords,
        terminal.FilteredRecords,
        Diagnostic(terminal.Diagnostic),
        terminal.SettledAt);

    private static EtlRunDiagnosticView? Diagnostic(EtlRunDiagnostic? diagnostic) => diagnostic is null
        ? null
        : new(diagnostic.Code, diagnostic.RecordOrdinal, diagnostic.Field);

    private static string Selection(EtlArtifact artifact) =>
        new EtlPublishedSelection(artifact.DefinitionId, artifact.DefinitionVersion, artifact.ContentHash).ToToken();

    private static string Status(EtlRunStatus status) => JsonNamingPolicy.CamelCase.ConvertName(status.ToString());
    private static EtlApiException NotFound() => new(404, "etl.run-not-found");
    private static EtlApiException InvalidCursor() => new(400, "etl.cursor-invalid");
}
