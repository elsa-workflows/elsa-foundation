using System.Runtime.CompilerServices;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Services;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Tests.Services;

public sealed class OpenTelemetryIngestorTests
{
    [Fact]
    public async Task IngestAsync_ThreeArgumentConstructorPreservesStoreThenLiveFeedBehavior()
    {
        var calls = new List<string>();
        var ingestor = new OpenTelemetryIngestor(
            new PassthroughRedactor(),
            new RecordingStore(_ => calls.Add("store")),
            new RecordingLiveFeed(_ => calls.Add("live")));

        await ingestor.IngestAsync(EmptyBatch());

        Assert.Equal(["store", "live"], calls);
    }

    [Fact]
    public async Task IngestAsync_ContributorsReceiveOnlyTheRedactedBatch()
    {
        var contributor = new RecordingContributor();
        var ingestor = new OpenTelemetryIngestor(
            new OpenTelemetryRedactor(Options.Create(new OpenTelemetryDiagnosticsOptions())),
            new RecordingStore(),
            new RecordingLiveFeed(),
            [contributor]);

        await ingestor.IngestAsync(BatchWithSecret());

        var contributed = Assert.Single(contributor.Batches);
        Assert.Equal("[Redacted]", Assert.Single(contributed.Logs).Attributes["password"]);
    }

    [Fact]
    public async Task IngestAsync_AwaitsEveryContributorBeforeStoreAndLiveFeed()
    {
        var calls = new List<string>();
        var first = new RecordingContributor((_, _) =>
        {
            calls.Add("contributor-1");
            return ValueTask.CompletedTask;
        });
        var second = new RecordingContributor((_, _) =>
        {
            calls.Add("contributor-2");
            return ValueTask.CompletedTask;
        });
        var store = new RecordingStore(_ => calls.Add("store"));
        var liveFeed = new RecordingLiveFeed(_ => calls.Add("live"));
        var ingestor = new OpenTelemetryIngestor(new PassthroughRedactor(), store, liveFeed, [first, second]);

        await ingestor.IngestAsync(EmptyBatch());

        Assert.Equal(["contributor-1", "contributor-2", "store", "live"], calls);
        Assert.Single(first.Batches);
        Assert.Single(second.Batches);
        Assert.Same(first.Batches[0], second.Batches[0]);
    }

    [Fact]
    public async Task IngestAsync_WhenAContributorFails_DoesNotStoreOrPublish()
    {
        var expected = new InvalidOperationException("Durable contribution failed.");
        var failing = new RecordingContributor((_, _) => ValueTask.FromException(expected));
        var later = new RecordingContributor();
        var store = new RecordingStore();
        var liveFeed = new RecordingLiveFeed();
        var ingestor = new OpenTelemetryIngestor(new PassthroughRedactor(), store, liveFeed, [failing, later]);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => ingestor.IngestAsync(EmptyBatch()).AsTask());

        Assert.Same(expected, actual);
        Assert.Empty(later.Batches);
        Assert.Equal(0, store.WriteCount);
        Assert.Equal(0, liveFeed.PublishCount);
    }

    [Fact]
    public async Task IngestAsync_WhenCancelled_PropagatesCancellationWithoutStoringOrPublishing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        CancellationToken observedToken = default;
        var contributor = new RecordingContributor((_, cancellationToken) =>
        {
            observedToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        });
        var store = new RecordingStore();
        var liveFeed = new RecordingLiveFeed();
        var ingestor = new OpenTelemetryIngestor(new PassthroughRedactor(), store, liveFeed, [contributor]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ingestor.IngestAsync(EmptyBatch(), cancellation.Token).AsTask());

        Assert.Equal(cancellation.Token, observedToken);
        Assert.Equal(0, store.WriteCount);
        Assert.Equal(0, liveFeed.PublishCount);
    }

    private static OpenTelemetryBatch BatchWithSecret()
    {
        var log = new OtlpLogRecord(
            "log-1",
            "resource-1",
            DateTimeOffset.UnixEpoch,
            "Error",
            17,
            "failure",
            "trace-1",
            "span-1",
            new Dictionary<string, string?> { ["password"] = Guid.NewGuid().ToString("N") });
        return new([], [], [], [], [], [log]);
    }

    private static OpenTelemetryBatch EmptyBatch() => new([], [], [], [], [], []);

    private sealed class PassthroughRedactor : IOpenTelemetryRedactor
    {
        public OpenTelemetryBatch Redact(OpenTelemetryBatch batch) => batch;
    }

    private sealed class RecordingContributor(
        Func<OpenTelemetryBatch, CancellationToken, ValueTask>? contribute = null) : IOpenTelemetryIngestionContributor
    {
        public List<OpenTelemetryBatch> Batches { get; } = [];

        public ValueTask ContributeAsync(OpenTelemetryBatch redactedBatch, CancellationToken cancellationToken = default)
        {
            Batches.Add(redactedBatch);
            return contribute?.Invoke(redactedBatch, cancellationToken) ?? ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingStore(Action<OpenTelemetryBatch>? write = null) : IOpenTelemetryStore
    {
        public int WriteCount { get; private set; }

        public ValueTask WriteAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default)
        {
            WriteCount++;
            write?.Invoke(batch);
            return ValueTask.CompletedTask;
        }

        public ValueTask<OpenTelemetryResourceResult> QueryResourcesAsync(OpenTelemetryResourceFilter filter, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new OpenTelemetryResourceResult([], 0));

        public ValueTask<OpenTelemetryTraceResult> QueryTracesAsync(OpenTelemetryTraceFilter filter, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new OpenTelemetryTraceResult([], 0));

        public ValueTask<OpenTelemetryTraceDetail?> GetTraceAsync(string traceId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<OpenTelemetryTraceDetail?>(null);

        public ValueTask<OpenTelemetryMetricResult> QueryMetricsAsync(OpenTelemetryMetricFilter filter, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new OpenTelemetryMetricResult([], [], 0));

        public ValueTask<OpenTelemetryLogResult> QueryLogsAsync(OpenTelemetryLogFilter filter, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new OpenTelemetryLogResult([], 0));

        public ValueTask<OpenTelemetryStorageDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new OpenTelemetryStorageDiagnostics(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
    }

    private sealed class RecordingLiveFeed(Action<OpenTelemetryBatch>? publish = null) : IOpenTelemetryLiveFeed
    {
        public int PublishCount { get; private set; }

        public ValueTask PublishAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default)
        {
            PublishCount++;
            publish?.Invoke(batch);
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<OpenTelemetryStreamItem> SubscribeAsync(
            OpenTelemetryTraceFilter filter,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
