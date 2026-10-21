using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Bindings;

namespace Elsa.Etl.Runtime.Http;

/// <summary>One acquisition result; a missing page without a diagnostic yields durable responsibility.</summary>
internal sealed record EtlHttpSourceAcquisition(EtlStoreStatus Status, EtlConnectorPartitionLease? Lease,
    EtlCachedHttpPage? Page, string? FailureCode)
{
    public override string ToString() => nameof(EtlHttpSourceAcquisition);
}

/// <summary>Composes durable source permits, bounded transport and immutable capture under current ownership.</summary>
/// <remarks>The worker owns heartbeat and physical capacity until this operation has completely unwound.</remarks>
internal sealed class EtlHttpSourceAcquirer(IEtlConnectorRuntimeStore store, HostHttpBindings bindings,
    EtlHttpTransport transport, string snapshotRoot, TimeProvider clock)
{
    public async ValueTask<EtlHttpSourceAcquisition> AcquireAsync(EtlConnectorRunAdmission admission,
        EtlConnectorPartitionLease lease, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (lease.Scope != admission.Invocation.Scope || lease.RunId != admission.Invocation.RunId ||
            admission.Bindings.Source.Kind != EtlConnectorKind.Http ||
            lease.Position is not EtlConnectorHttpPosition { Exhausted: false } position)
            return Failed("etl.http.page-request-invalid");

        try
        {
            // Decode from the admitted artifact so transport, predecode and cache always share the same limits.
            var pipeline = EtlArtifactV2Json.Read(admission.Invocation.Artifact.Pipeline);
            if (pipeline.Source is not EtlHttpJsonSource source)
                return Failed("etl.http.page-admission-invalid");
            var pin = admission.Bindings.Source.Revision;
            await using var binding = await bindings.ResolveExactAsync(lease.Scope, pin.Alias, "Source",
                pin.Revision, pin.Handle, cancellationToken);
            var requests = new EtlHttpRequestBuilder(source, binding, pipeline.Http);
            var cache = new EtlHttpPageCache(snapshotRoot, admission, lease.PartitionOrdinal, requests);
            // Policy and manifest validation precede attempt allocation. Cache corruption is never a cache miss.
            var uri = requests.RequestUri(position.Request);
            var cached = await cache.ReadAsync(position.PageSequence, position.Request, cancellationToken);
            if (position.Page is { } attached)
            {
                if (cached is null || cached.Descriptor != attached)
                    return Failed("etl.http.page-cache-corrupt");
                return new(EtlStoreStatus.Replay, lease, cached, null);
            }

            var begin = await store.BeginConnectorHttpSourceAsync(lease, Guid.NewGuid().ToString("N"), cancellationToken);
            if (begin.Status is EtlStoreStatus.LeaseLost or EtlStoreStatus.Terminal or EtlStoreStatus.NotFound)
                return new(begin.Status, null, null, null);
            // A predecessor can publish while this worker is allocating its permit. Prefer that publication
            // before interpreting retry state or starting another physical request.
            cached ??= await cache.ReadAsync(position.PageSequence, position.Request, cancellationToken);
            if (cached is not null)
                return await CaptureAsync(lease, cached, begin.Fetch, cancellationToken);
            if (begin.Fetch is { Phase: EtlConnectorHttpSourceFetchPhase.Captured })
                return Failed("etl.http.page-cache-corrupt");
            if (begin.Permit is not { } permit)
                return WithoutPage(begin);
            if (begin.Status != EtlStoreStatus.Applied)
                return new(begin.Status, null, null, null); // A replayed live permit is not permission to resend.

            // Cache and SQL work may have taken time. Re-resolve the exact revision immediately before the
            // physical attempt; this cannot recall an already-sent request, but detects intervening revocation.
            EtlHttpFetchResponse response;
            await using (var dispatchBinding = await bindings.ResolveExactAsync(lease.Scope, pin.Alias, "Source",
                             pin.Revision, pin.Handle, cancellationToken))
                response = await transport.FetchAsync(dispatchBinding, uri, permit, pipeline.Http.MaxPageBytes, cancellationToken);
            if (response.Body is not { } body || response.ResponseReceivedAtUtc is not { } received)
                return await CompleteAsync(lease, permit, response.Outcome, cancellationToken);
            if (response.Outcome is not { StatusCode: 200, InvalidPage: false } || received < permit.StartedAt ||
                received >= permit.ExpiresAt || begin.Fetch is null || received >= begin.Fetch.Deadline)
                return Failed("etl.http.source-receipt-invalid");

            // Decode before publication so a deterministic bad page is recorded as such, rather than retried.
            try
            {
                var decoded = EtlJsonPageReader.Read(body, source, pipeline.Limits, pipeline.Http);
                _ = requests.NextContinuation(position.Request, decoded.Continuation);
            }
            catch (EtlJsonPageReadException exception)
            {
                return await InvalidPageAsync(lease, permit, received, exception.Code, cancellationToken);
            }
            catch (EtlHttpRequestBuilderException exception)
            {
                return await InvalidPageAsync(lease, permit, received, exception.Code, cancellationToken);
            }

            cached = await cache.PublishAsync(position.PageSequence, position.Request, Guid.ParseExact(permit.RequestId, "N"),
                permit.Fence, received, body, cancellationToken);
            // Another attempt may have published these identical bytes first. Reconcile its exact provenance,
            // never relabel the winning response with this attempt's fence or receive time.
            return await CaptureAsync(lease, cached, begin.Fetch, cancellationToken);
        }
        catch (Exception exception) when (exception is ArgumentException or System.Text.Json.JsonException or NotSupportedException)
        {
            return Failed("etl.http.page-admission-invalid");
        }
        catch (EtlBindingException exception)
        {
            return Failed(exception.Code);
        }
        catch (EtlHttpPageCacheException exception)
        {
            return Failed(exception.Code);
        }
        catch (EtlHttpRequestBuilderException exception)
        {
            return Failed(exception.Code);
        }
    }

    private async ValueTask<EtlHttpSourceAcquisition> CaptureAsync(EtlConnectorPartitionLease lease,
        EtlCachedHttpPage page, EtlConnectorHttpSourceFetch? fetch, CancellationToken token)
    {
        var record = fetch?.Attempts.SingleOrDefault(x => x.Attempt.RequestId == page.Provenance.AttemptId &&
            x.Attempt.Fence == page.Provenance.Fence);
        if (fetch is null || record is null || record.Outcome is not null ||
            record.Attempt.RequestHash != page.Descriptor.RequestHash || fetch.PageSequence != page.Descriptor.PageSequence ||
            page.Provenance.ResponseReceivedAtUtc < record.Attempt.StartedAt ||
            page.Provenance.ResponseReceivedAtUtc >= record.Attempt.ExpiresAt ||
            page.Provenance.ResponseReceivedAtUtc >= fetch.Deadline)
            return Failed("etl.http.page-provenance-invalid");
        var captured = await store.CaptureConnectorHttpSourceAsync(lease, record.Attempt, page.Descriptor,
            page.Provenance.ResponseReceivedAtUtc, token);
        if (captured.Status is not (EtlStoreStatus.Applied or EtlStoreStatus.Replay))
            return captured.Status == EtlStoreStatus.Conflict ? Failed("etl.http.page-provenance-invalid") : Refused(captured.Status);
        var attached = await store.AttachHttpPageAsync(lease, page.Descriptor, token);
        return (attached.Status is EtlStoreStatus.Applied or EtlStoreStatus.Replay) && attached.Lease is not null
            ? new(attached.Status, attached.Lease, page, null)
            : attached.Status == EtlStoreStatus.Conflict ? Failed("etl.http.page-attachment-conflict") : Refused(attached.Status);
    }

    private async ValueTask<EtlHttpSourceAcquisition> CompleteAsync(EtlConnectorPartitionLease lease,
        EtlHttpSourceFetchAttempt permit, EtlHttpSourceFetchOutcome outcome, CancellationToken token)
    {
        // Untimed status receipts become uncertain after expiry. A timed invalid-200 receipt retains
        // the transport observation while the store independently validates its original attempt window.
        if (outcome.ResponseReceivedAtUtc is null && clock.GetUtcNow() >= permit.ExpiresAt)
            outcome = new(null, null, false);
        return WithoutPage(await store.CompleteConnectorHttpSourceAsync(lease, permit, outcome, token));
    }

    private async ValueTask<EtlHttpSourceAcquisition> InvalidPageAsync(EtlConnectorPartitionLease lease,
        EtlHttpSourceFetchAttempt permit, DateTimeOffset receivedAt, string code, CancellationToken token)
    {
        var completed = await CompleteAsync(lease, permit, new(200, null, true, receivedAt), token);
        return completed.FailureCode == "etl.http.page-invalid" ? Failed(code) : completed;
    }

    private static EtlHttpSourceAcquisition WithoutPage(EtlConnectorHttpSourceFetchResult result) =>
        result.Fetch is { Phase: EtlConnectorHttpSourceFetchPhase.Failed, Failure: var failure }
            ? Failed(failure switch
            {
                EtlConnectorHttpSourceFetchFailure.AttemptsExhausted => "etl.http.source-attempts-exhausted",
                EtlConnectorHttpSourceFetchFailure.DeadlineExceeded => "etl.http.source-deadline-exceeded",
                EtlConnectorHttpSourceFetchFailure.InvalidPage => "etl.http.page-invalid",
                _ => "etl.http.source-response-rejected"
            })
            : Refused(result.Status);

    private static EtlHttpSourceAcquisition Refused(EtlStoreStatus status) =>
        status is EtlStoreStatus.Invalid or EtlStoreStatus.NotFound
            ? Failed("etl.http.source-transition-refused")
            : new(status, null, null, null);

    private static EtlHttpSourceAcquisition Failed(string code) => new(EtlStoreStatus.Invalid, null, null, code);
}
