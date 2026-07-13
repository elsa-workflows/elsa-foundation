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
    public void Every_loss_reason_is_counted_independently_including_subscriber_delivery()
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
    public void Existing_live_feed_signals_map_to_subscriber_delivery_without_moving_fanout()
    {
        var structured = new DroppedEntriesSignal(4, DateTimeOffset.UnixEpoch);
        var telemetry = new OpenTelemetryDroppedItemSummary(OpenTelemetrySignalType.Trace, 3, "SubscriberQueueFull");
        var counters = new DiagnosticsPersistenceCounters();

        counters.RecordLoss(DiagnosticsPersistenceLossReason.SubscriberDelivery, checked((int)structured.DroppedCount));
        counters.RecordLoss(DiagnosticsPersistenceLossReason.SubscriberDelivery, checked((int)telemetry.Count));

        Assert.Equal(7, counters.Snapshot().Losses[DiagnosticsPersistenceLossReason.SubscriberDelivery]);
        Assert.Equal("SubscriberQueueFull", telemetry.Reason);
    }
}
