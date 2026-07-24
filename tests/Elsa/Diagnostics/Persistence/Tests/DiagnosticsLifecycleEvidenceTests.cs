using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Diagnostics.Persistence.Observability;
using Elsa.Diagnostics.Persistence.Tests.Fixtures;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Tests;

/// <summary>
/// Keeps the committed US3 evidence manifest tied to executable lifecycle behavior rather than a manually
/// maintained prose claim. The full named suites provide broader coverage; this test executes the values
/// recorded in the manifest at the shared-drain seam.
/// </summary>
public sealed class DiagnosticsLifecycleEvidenceTests
{
    [Fact]
    public async Task Us3_manifest_matches_executed_queue_loss_and_completion_evidence()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(RepositoryPath(
            "specs/138-groundwork-diagnostics-persistence/evidence/us3-lifecycle-results.json")));
        var root = manifest.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("138-groundwork-diagnostics-persistence", root.GetProperty("workUnit").GetString());
        Assert.Equal("US3", root.GetProperty("userStory").GetString());
        Assert.Equal("pass", root.GetProperty("outcome").GetString());
        Assert.Equal(
            new[]
            {
                typeof(DiagnosticsDrainLoadTests).FullName,
                typeof(DiagnosticsDrainShutdownTests).FullName,
                typeof(DiagnosticsDrainLifecycleTests).FullName,
                typeof(DiagnosticsPersistenceObservabilityTests).FullName,
                typeof(DiagnosticsPersistenceLifecycleTests).FullName,
                typeof(DiagnosticsLifecycleEvidenceTests).FullName
            },
            root.GetProperty("coveredSuites").EnumerateArray().Select(value => value.GetString()));
        var testRun = root.GetProperty("testRun");
        Assert.Equal("Release", testRun.GetProperty("configuration").GetString());
        Assert.Equal(0, testRun.GetProperty("failed").GetInt32());
        Assert.Equal(0, testRun.GetProperty("skipped").GetInt32());
        Assert.True(DateTimeOffset.TryParse(testRun.GetProperty("recordedAtUtc").GetString(), out _));
        Assert.Matches("^[0-9a-f]{40}$", testRun.GetProperty("gitHead").GetString());
        Assert.Equal(CountFacts(
                typeof(DiagnosticsDrainLoadTests),
                typeof(DiagnosticsDrainShutdownTests),
                typeof(DiagnosticsDrainLifecycleTests),
                typeof(DiagnosticsPersistenceObservabilityTests),
                typeof(DiagnosticsPersistenceLifecycleTests),
                typeof(DiagnosticsLifecycleEvidenceTests)),
            testRun.GetProperty("passed").GetInt32());

        var queue = root.GetProperty("queueBounds");
        var losses = root.GetProperty("lossTotals");
        var outcomes = root.GetProperty("completionOutcomes");
        var retries = root.GetProperty("retries");

        await AssertConcurrentQueueBoundEvidenceAsync(queue);
        await AssertQueueOverflowEvidenceAsync(queue, losses);
        await AssertRetryExhaustionEvidenceAsync(losses, retries);
        await AssertGracefulCompletionEvidenceAsync(outcomes, losses);
        await AssertRetentionRetryEvidenceAsync(retries);
        await AssertTimedOutCompletionEvidenceAsync(outcomes, losses);
        await AssertWriteAfterClosureAndSubscriberDeliveryEvidenceAsync(losses);
    }

    private static async Task AssertConcurrentQueueBoundEvidenceAsync(JsonElement queue)
    {
        var queueCapacity = queue.GetProperty("concurrentProducerQueueCapacity").GetInt32();
        var producerCount = queue.GetProperty("concurrentProducerCount").GetInt32();
        var target = new DiagnosticsFailureTarget();
        await using var fixture = new DiagnosticsDrainTestFixture();
        var drain = fixture.Create(target, queueCapacity: queueCapacity, batchSize: 25);
        drain.Start();

        var acknowledgements = Enumerable.Range(1, producerCount)
            .AsParallel()
            .Select(item =>
            {
                Assert.True(drain.TryEnqueue(item, out var acknowledgement));
                return acknowledgement;
            })
            .ToArray();

        await drain.StopAsync();
        Assert.Equal(Enumerable.Range(1, producerCount).Order(), (await Task.WhenAll(acknowledgements)).Order());
        Assert.Equal(producerCount, target.State.PersistedItems.Count);
    }

    private static async Task AssertQueueOverflowEvidenceAsync(JsonElement queue, JsonElement losses)
    {
        var queueCapacity = queue.GetProperty("overflowQueueCapacity").GetInt32();
        var submitted = queue.GetProperty("overflowSubmittedCount").GetInt32();
        var counters = new DiagnosticsPersistenceCounters();
        await using var fixture = new DiagnosticsDrainTestFixture();
        var drain = fixture.Create(new DiagnosticsFailureTarget(), counters, queueCapacity: queueCapacity);
        var acknowledgements = new List<Task<int>>();
        for (var item = 1; item <= submitted; item++)
        {
            Assert.True(drain.TryEnqueue(item, out var acknowledgement));
            acknowledgements.Add(acknowledgement);
        }

        var overflow = await Assert.ThrowsAsync<DiagnosticsDrainException>(() => acknowledgements[0]);
        Assert.Equal(DiagnosticsPersistenceLossReason.QueueOverflow, overflow.Reason);
        drain.Start();
        await drain.StopAsync();
        Assert.Equal(Enumerable.Range(2, submitted - 1), await Task.WhenAll(acknowledgements.Skip(1)));
        Assert.Equal(queue.GetProperty("overflowShedCount").GetInt64(),
            counters.Snapshot().Losses[DiagnosticsPersistenceLossReason.QueueOverflow]);
        Assert.Equal(losses.GetProperty("queueOverflow").GetInt64(),
            counters.Snapshot().Losses[DiagnosticsPersistenceLossReason.QueueOverflow]);
    }

    private static async Task AssertRetryExhaustionEvidenceAsync(JsonElement losses, JsonElement retries)
    {
        var counters = new DiagnosticsPersistenceCounters();
        var target = new DiagnosticsFailureTarget
        {
            Failure = (batch, _) => batch.Items[0] == 1
                ? new DiagnosticsOperationalException("persistently unavailable")
                : null
        };
        await using var fixture = new DiagnosticsDrainTestFixture();
        var drain = fixture.Create(target, counters, batchSize: 1);
        drain.Start();

        var failed = drain.EnqueueAsync(1).AsTask();
        var exception = await Assert.ThrowsAsync<DiagnosticsDrainException>(() => failed);
        Assert.Equal(DiagnosticsPersistenceLossReason.RetryExhausted, exception.Reason);
        var recovered = drain.EnqueueAsync(2).AsTask();
        await drain.StopAsync();

        Assert.Equal(2, await recovered);
        var snapshot = counters.Snapshot();
        Assert.Equal(retries.GetProperty("commitRetryCountBeforeExhaustion").GetInt64(), snapshot.CommitRetries);
        Assert.Equal(retries.GetProperty("commitFailureCountAfterExhaustion").GetInt64(), snapshot.CommitFailures);
        Assert.Equal(losses.GetProperty("retryExhausted").GetInt64(),
            snapshot.Losses[DiagnosticsPersistenceLossReason.RetryExhausted]);
    }

    private static async Task AssertGracefulCompletionEvidenceAsync(JsonElement outcomes, JsonElement losses)
    {
        var graceful = outcomes.GetProperty("graceful");
        var acceptedCount = graceful.GetProperty("acceptedAcknowledgementCount").GetInt32();
        var deleted = graceful.GetProperty("finalRetentionDeletedCount").GetInt32();
        var counters = new DiagnosticsPersistenceCounters();
        var target = new DiagnosticsFailureTarget { RetentionDeleted = deleted };
        await using var fixture = new DiagnosticsDrainTestFixture();
        var drain = fixture.Create(target, counters);
        drain.Start();
        var acknowledgements = Enumerable.Range(1, acceptedCount)
            .Select(item => drain.EnqueueAsync(item).AsTask())
            .ToArray();

        var stop = await drain.StopAsync();

        Assert.Equal(graceful.GetProperty("state").GetString(), stop.State.ToString());
        Assert.Equal(graceful.GetProperty("drained").GetBoolean(), stop.Drained);
        Assert.Equal(Enumerable.Range(1, acceptedCount), await Task.WhenAll(acknowledgements));
        Assert.True(target.RetentionCalls >= 1);
        Assert.Equal(losses.GetProperty("durableRetentionDeletion").GetInt64(),
            counters.Snapshot().Losses[DiagnosticsPersistenceLossReason.DurableRetentionDeletion]);
    }

    private static async Task AssertRetentionRetryEvidenceAsync(JsonElement retries)
    {
        var counters = new DiagnosticsPersistenceCounters();
        var target = new DiagnosticsFailureTarget
        {
            RetentionFailure = call => call < 3
                ? new DiagnosticsOperationalException("retention unavailable")
                : null
        };
        await using var fixture = new DiagnosticsDrainTestFixture();
        var drain = fixture.Create(target, counters);
        var acknowledgement = drain.EnqueueAsync(1).AsTask();

        await drain.StopAsync();

        Assert.Equal(1, await acknowledgement);
        Assert.Equal(retries.GetProperty("retentionRetryCountBeforeRecovery").GetInt64(),
            counters.Snapshot().RetentionRetries);
    }

    private static async Task AssertTimedOutCompletionEvidenceAsync(JsonElement outcomes, JsonElement losses)
    {
        var timedOut = outcomes.GetProperty("timedOut");
        var target = new DiagnosticsFailureTarget
        {
            CommitRelease = new(TaskCreationOptions.RunContinuationsAsynchronously),
            IgnoreCancellation = true
        };
        var counters = new DiagnosticsPersistenceCounters();
        await using var fixture = new DiagnosticsDrainTestFixture();
        var drain = fixture.Create(
            target,
            counters,
            shutdownTimeout: TimeSpan.FromMilliseconds(50));
        drain.Start();
        Assert.True(drain.TryEnqueue(1, out var acknowledgement));
        await target.CommitEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var stop = await drain.StopAsync();
            stopwatch.Stop();
            Assert.Equal(timedOut.GetProperty("state").GetString(), stop.State.ToString());
            Assert.Equal(timedOut.GetProperty("drained").GetBoolean(), stop.Drained);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(
                timedOut.GetProperty("upperBoundMilliseconds").GetInt32()));
            var exception = await Assert.ThrowsAsync<DiagnosticsDrainException>(() => acknowledgement);
            Assert.Equal(DiagnosticsPersistenceLossReason.ShutdownTimeout, exception.Reason);
            Assert.True(acknowledgement.IsCompleted);
            Assert.Equal(losses.GetProperty("shutdownTimeout").GetInt64(),
                counters.Snapshot().Losses[DiagnosticsPersistenceLossReason.ShutdownTimeout]);
        }
        finally
        {
            target.CommitRelease.TrySetResult();
        }
    }

    private static async Task AssertWriteAfterClosureAndSubscriberDeliveryEvidenceAsync(JsonElement losses)
    {
        var counters = new DiagnosticsPersistenceCounters();
        await using var fixture = new DiagnosticsDrainTestFixture();
        var drain = fixture.Create(new DiagnosticsFailureTarget(), counters);
        drain.Start();
        await drain.StopAsync();
        Assert.False(drain.TryEnqueue(1, out var acknowledgement));
        var exception = await Assert.ThrowsAsync<DiagnosticsDrainException>(() => acknowledgement);
        Assert.Equal(DiagnosticsPersistenceLossReason.WriteAfterClosure, exception.Reason);
        Assert.Equal(losses.GetProperty("writeAfterClosure").GetInt64(),
            counters.Snapshot().Losses[DiagnosticsPersistenceLossReason.WriteAfterClosure]);

        var bridge = new DiagnosticsSubscriberDeliveryLossBridge(counters);
        var structuredLogs = bridge.CreateStructuredLogRecorder();
        structuredLogs(new DroppedEntriesSignal(4, DateTimeOffset.UnixEpoch));
        bridge.RecordOpenTelemetry(new OpenTelemetryDroppedItemSummary(
            OpenTelemetrySignalType.Trace,
            3,
            "SubscriberQueueFull"));

        Assert.Equal(losses.GetProperty("subscriberDelivery").GetInt64(),
            counters.Snapshot().Losses[DiagnosticsPersistenceLossReason.SubscriberDelivery]);
    }

    private static string RepositoryPath(string relativePath, [CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "../../../../..", relativePath));

    private static int CountFacts(params Type[] suites) => suites.Sum(suite => suite
        .GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public)
        .Count(method => method.GetCustomAttribute<FactAttribute>() is not null));
}
