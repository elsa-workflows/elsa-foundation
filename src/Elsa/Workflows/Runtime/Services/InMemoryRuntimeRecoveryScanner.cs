using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryRuntimeRecoveryScanner : IRuntimeRecoveryPagedScanner
{
    private readonly IExecutionLivenessStateStore _operationalStateStore;
    private readonly IRuntimeRecoveryContinuationCodec _continuationCodec;

    public bool SupportsPaging => _operationalStateStore is IRuntimeRecoveryLivenessPageSource;

    // Preserve the original one-argument constructor metadata for binaries compiled before recovery paging. The
    // optional codec overload is additive and is used by composition roots that supply a stable key.
    public InMemoryRuntimeRecoveryScanner(IExecutionLivenessStateStore operationalStateStore)
        : this(operationalStateStore, continuationCodec: null)
    {
    }

    public InMemoryRuntimeRecoveryScanner(
        IExecutionLivenessStateStore operationalStateStore,
        IRuntimeRecoveryContinuationCodec? continuationCodec = null)
    {
        ArgumentNullException.ThrowIfNull(operationalStateStore);
        _operationalStateStore = operationalStateStore;
        // Direct construction is retained for existing in-memory callers and tests. Application composition injects
        // the shared codec so a host can choose a key that survives restart when it exposes these pages.
        _continuationCodec = continuationCodec ?? new HmacRuntimeRecoveryContinuationCodec(
            Options.Create(new RuntimeRecoveryContinuationOptions { AllowEphemeralDevelopmentKey = true }));
    }

    public async ValueTask<IReadOnlyCollection<RuntimeRecoveryCandidate>> ScanAsync(
        RuntimeRecoveryScanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // Keep the legacy collection surface's historical behavior for custom stores: they may only implement the
        // collection contract, so a complete compatibility scan is still allowed there. Built-in stores advertise
        // the due-ordered page capability below and never materialize the recovery population.
        if (_operationalStateStore is not IRuntimeRecoveryLivenessPageSource)
        {
            var states = await RuntimeOperationalStorePagingExtensions.ListAllAsync(_operationalStateStore, cancellationToken);
            return RuntimeRecoveryCandidateSelector.Select(states, request);
        }

        var page = await ListRecoveryPageAsync(
            request,
            providerContinuation: null,
            cancellationToken);
        return RuntimeRecoveryCandidateSelector.Select(page.Items, request);
    }

    public async ValueTask<RuntimeRecoveryPage> ScanPageAsync(
        RuntimeRecoveryScanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var continuation = DecodeContinuation(request.ContinuationToken, Binding(request));
        var providerContinuation = continuation?.ProviderContinuationToken;
        var page = await ListRecoveryPageAsync(request, providerContinuation, cancellationToken);

        var candidates = RuntimeRecoveryCandidateSelector.Select(
            page.Items,
            new RuntimeRecoveryScanRequest(
                request.Now,
                request.LeaseTimeout,
                request.HeartbeatTimeout,
                RuntimeStorePageRequest.MaximumLimit,
                request.OwnerId));
        var items = candidates.Take(request.Limit).ToArray();
        var next = page.NextContinuationToken is not null
            ? EncodeContinuation(new RecoveryContinuation(Binding(request), page.NextContinuationToken))
            : null;
        return new RuntimeRecoveryPage(
            request,
            items,
            next);
    }

    private static string Binding(RuntimeRecoveryScanRequest request) =>
        $"recovery|{request.Now.UtcTicks}|{request.LeaseTimeout.Ticks}|{request.HeartbeatTimeout.Ticks}|{request.OwnerId}";

    private ValueTask<RuntimeStorePage<ExecutionLivenessState>> ListRecoveryPageAsync(
        RuntimeRecoveryScanRequest request,
        string? providerContinuation,
        CancellationToken cancellationToken)
    {
        if (_operationalStateStore is not IRuntimeRecoveryLivenessPageSource source)
        {
            throw new NotSupportedException(
                "This liveness store does not advertise due-ordered recovery paging.");
        }

        var query = new RuntimeStorePageRequest(request.Limit, providerContinuation);
        return source.ListRecoveryPageAsync(request, query, cancellationToken);
    }

    private string EncodeContinuation(RecoveryContinuation continuation)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(continuation);
        var token = _continuationCodec.Encode("imrrs1", payload);
        RuntimeStorePageRequest.ValidateContinuationToken(token, nameof(token));
        return token;
    }

    private RecoveryContinuation? DecodeContinuation(string? token, string binding)
    {
        if (token is null)
            return null;

        try
        {
            var payloadBytes = _continuationCodec.Decode("imrrs1", token);
            var continuation = JsonSerializer.Deserialize<RecoveryContinuation>(payloadBytes)
                               ?? throw new InvalidDataException("Recovery continuation token is empty.");
            if (!StringComparer.Ordinal.Equals(continuation.Binding, binding) ||
                string.IsNullOrWhiteSpace(continuation.ProviderContinuationToken))
            {
                throw new InvalidDataException("Recovery continuation token does not belong to this scan.");
            }

            return continuation;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or JsonException or InvalidDataException)
        {
            throw new ArgumentException("The recovery continuation token is invalid.", nameof(token), exception);
        }
    }

    private sealed record RecoveryContinuation(
        string Binding,
        string? ProviderContinuationToken);
}
