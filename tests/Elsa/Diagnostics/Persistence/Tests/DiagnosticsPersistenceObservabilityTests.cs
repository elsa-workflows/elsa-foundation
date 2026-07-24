using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.Persistence.Observability;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Tests;

public sealed class DiagnosticsPersistenceObservabilityTests
{
    [Fact]
    public void Observer_contract_cannot_receive_payloads_or_high_cardinality_labels()
    {
        var parameterTypes = typeof(IDiagnosticsPersistenceObserver)
            .GetMethods()
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.DoesNotContain(typeof(string), parameterTypes);
        Assert.DoesNotContain(typeof(object), parameterTypes);
        Assert.All(parameterTypes, type => Assert.False(type.Namespace?.StartsWith("Elsa.Diagnostics.StructuredLogs", StringComparison.Ordinal) == true));
        Assert.All(parameterTypes, type => Assert.False(type.Namespace?.StartsWith("Elsa.Diagnostics.OpenTelemetry", StringComparison.Ordinal) == true));
    }

    [Fact]
    public void Pull_counters_do_not_reference_recursive_logging_tracing_or_Groundwork_infrastructure()
    {
        var references = typeof(DiagnosticsPersistenceCounters).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, x => x.Name == "Microsoft.Extensions.Logging.Abstractions");
        Assert.DoesNotContain(references, x => x.Name == "System.Diagnostics.DiagnosticSource");
        Assert.DoesNotContain(references, x => x.Name?.StartsWith("Groundwork", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Every_loss_reason_is_counted_independently()
    {
        var counters = new DiagnosticsPersistenceCounters();
        foreach (var reason in Enum.GetValues<DiagnosticsPersistenceLossReason>())
            counters.RecordLoss(reason, (int)reason + 1);

        var snapshot = counters.Snapshot();
        foreach (var reason in Enum.GetValues<DiagnosticsPersistenceLossReason>())
            Assert.Equal((int)reason + 1, snapshot.Losses[reason]);
    }

    [Fact]
    public void Commit_and_retention_retries_and_failures_remain_distinct()
    {
        var counters = new DiagnosticsPersistenceCounters();
        counters.RecordRetry(DiagnosticsPersistenceOperation.Commit, 1, 2);
        counters.RecordRetry(DiagnosticsPersistenceOperation.Retention, 1, 2);
        counters.RecordOperationFailure(DiagnosticsPersistenceOperation.Commit);
        counters.RecordOperationFailure(DiagnosticsPersistenceOperation.Retention);

        var snapshot = counters.Snapshot();
        Assert.Equal(1, snapshot.CommitRetries);
        Assert.Equal(1, snapshot.RetentionRetries);
        Assert.Equal(1, snapshot.CommitFailures);
        Assert.Equal(1, snapshot.RetentionFailures);
    }

    [Fact]
    public void Accepted_queue_depth_and_high_water_are_payload_free_monotonic_counters()
    {
        var counters = new DiagnosticsPersistenceCounters();
        counters.RecordAccepted(3);
        counters.RecordQueueDepth(1);
        counters.RecordQueueDepth(5);
        counters.RecordQueueDepth(2);

        var snapshot = counters.Snapshot();
        Assert.Equal(3, snapshot.Accepted);
        Assert.Equal(2, snapshot.QueueDepth);
        Assert.Equal(5, snapshot.QueueHighWater);
        Assert.Throws<ArgumentOutOfRangeException>(() => counters.RecordAccepted(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => counters.RecordQueueDepth(-1));
    }

    [Fact]
    public void Ordered_depth_samples_ignore_stale_callbacks()
    {
        var counters = new DiagnosticsPersistenceCounters();
        counters.RecordQueueDepth(5, 2);
        counters.RecordQueueDepth(1, 1);
        counters.RecordQueueDepth(0, 3);

        var snapshot = counters.Snapshot();
        Assert.Equal(0, snapshot.QueueDepth);
        Assert.Equal(5, snapshot.QueueHighWater);
    }

    [Fact]
    public void Legacy_depth_samples_do_not_consume_the_ordered_sequence()
    {
        var counters = new DiagnosticsPersistenceCounters();
        counters.RecordQueueDepth(5, 2);
        counters.RecordQueueDepth(4);
        counters.RecordQueueDepth(0, 3);

        var snapshot = counters.Snapshot();
        Assert.Equal(0, snapshot.QueueDepth);
        Assert.Equal(5, snapshot.QueueHighWater);
    }

    [Fact]
    public void Existing_live_feed_signals_are_classified_through_the_production_bridge()
    {
        var counters = new DiagnosticsPersistenceCounters();
        var bridge = new DiagnosticsSubscriberDeliveryLossBridge(counters);
        var structuredLogs = bridge.CreateStructuredLogRecorder();

        structuredLogs(new DroppedEntriesSignal(4, DateTimeOffset.UnixEpoch));
        bridge.RecordOpenTelemetry(new OpenTelemetryDroppedItemSummary(OpenTelemetrySignalType.Trace, 3, "SubscriberQueueFull"));

        Assert.Equal(7, counters.Snapshot().Losses[DiagnosticsPersistenceLossReason.SubscriberDelivery]);
    }

    [Fact]
    public void Cumulative_structured_log_signals_record_only_deltas_even_when_repeated_or_concurrent()
    {
        var counters = new DiagnosticsPersistenceCounters();
        var bridge = new DiagnosticsSubscriberDeliveryLossBridge(counters);
        var since = DateTimeOffset.UnixEpoch;
        var structuredLogs = bridge.CreateStructuredLogRecorder();

        Parallel.For(1, 101, count => structuredLogs(new DroppedEntriesSignal(count, since)));
        structuredLogs(new DroppedEntriesSignal(100, since));

        Assert.Equal(100, counters.Snapshot().Losses[DiagnosticsPersistenceLossReason.SubscriberDelivery]);
    }

    [Fact]
    public void A_new_structured_log_subscription_starts_a_new_cumulative_epoch()
    {
        var counters = new DiagnosticsPersistenceCounters();
        var bridge = new DiagnosticsSubscriberDeliveryLossBridge(counters);
        var firstSubscription = bridge.CreateStructuredLogRecorder();
        var restartedSubscription = bridge.CreateStructuredLogRecorder();

        firstSubscription(new DroppedEntriesSignal(4, DateTimeOffset.UnixEpoch));
        restartedSubscription(new DroppedEntriesSignal(2, DateTimeOffset.UnixEpoch.AddSeconds(1)));

        Assert.Equal(6, counters.Snapshot().Losses[DiagnosticsPersistenceLossReason.SubscriberDelivery]);
    }

    [Fact]
    public void Subscriber_delivery_bridge_validates_dependencies_signals_and_counts_without_truncation()
    {
        Assert.Throws<ArgumentNullException>(() => new DiagnosticsSubscriberDeliveryLossBridge(null!));
        var counters = new DiagnosticsPersistenceCounters();
        var bridge = new DiagnosticsSubscriberDeliveryLossBridge(counters);
        var structuredLogs = bridge.CreateStructuredLogRecorder();

        Assert.Throws<ArgumentNullException>(() => structuredLogs(null!));
        Assert.Throws<ArgumentNullException>(() => bridge.RecordOpenTelemetry(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            structuredLogs(new DroppedEntriesSignal(0, DateTimeOffset.UnixEpoch)));
        bridge.RecordOpenTelemetry(new OpenTelemetryDroppedItemSummary(OpenTelemetrySignalType.Log, 3_000_000_000, "SubscriberQueueFull"));

        Assert.Equal(3_000_000_000,
            counters.Snapshot().Losses[DiagnosticsPersistenceLossReason.SubscriberDelivery]);
    }

    [Fact]
    public void Subscriber_delivery_observer_failure_cannot_break_live_feed_accounting_paths()
    {
        var bridge = new DiagnosticsSubscriberDeliveryLossBridge(new ThrowingObserver());
        var structuredLogs = bridge.CreateStructuredLogRecorder();

        structuredLogs(new DroppedEntriesSignal(2, DateTimeOffset.UnixEpoch));
        bridge.RecordOpenTelemetry(new OpenTelemetryDroppedItemSummary(
            OpenTelemetrySignalType.Trace,
            3,
            "SubscriberQueueFull"));
    }

    [Fact]
    public void Observer_rejects_invalid_state_operation_reason_count_and_attempt_values()
    {
        var counters = new DiagnosticsPersistenceCounters();

        Assert.Throws<ArgumentOutOfRangeException>(() => counters.RecordState((DiagnosticsDrainState)int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => counters.RecordRetry((DiagnosticsPersistenceOperation)int.MaxValue, 1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => counters.RecordOperationFailure((DiagnosticsPersistenceOperation)int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => counters.RecordLoss((DiagnosticsPersistenceLossReason)int.MaxValue, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => counters.RecordLoss(DiagnosticsPersistenceLossReason.QueueOverflow, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => counters.RecordRetry(DiagnosticsPersistenceOperation.Commit, 0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => counters.RecordRetry(DiagnosticsPersistenceOperation.Commit, 3, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => counters.RecordRetry(DiagnosticsPersistenceOperation.Commit, 1, 0));
    }

    private sealed class ThrowingObserver : IDiagnosticsPersistenceObserver
    {
        public void RecordState(DiagnosticsDrainState state) => throw new InvalidOperationException();
        public void RecordRetry(DiagnosticsPersistenceOperation operation, int attempt, int maxAttempts) => throw new InvalidOperationException();
        public void RecordOperationFailure(DiagnosticsPersistenceOperation operation) => throw new InvalidOperationException();
        public void RecordLoss(DiagnosticsPersistenceLossReason reason, long count) => throw new InvalidOperationException();
    }
}
