using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Scoping;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkPrivilegedAccessRecorderTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Capacity_must_be_positive(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GroundworkPrivilegedAccessSink(capacity));
    }

    [Fact]
    public void Acquisition_and_outcome_records_cover_each_privileged_access_kind()
    {
        var sink = new GroundworkPrivilegedAccessSink(capacity: 8);
        var recorder = new GroundworkPrivilegedAccessRecorder(sink);
        var accesses = new[]
        {
            DocumentStoreAccess.PrivilegedScoped(
                new PrivilegedStorageAccess("repair-index"),
                new StorageScope("tenant-a")),
            DocumentStoreAccess.PrivilegedGlobal(
                new PrivilegedStorageAccess("rotate-host-key")),
            DocumentStoreAccess.PrivilegedAcrossScopes(
                new PrivilegedStorageAccess("recover-stalled-work"))
        };

        foreach (var access in accesses)
        {
            var audit = recorder.RecordAcquisition(access);
            recorder.RecordOutcome(audit, GroundworkPrivilegedAccessOutcome.Succeeded);
        }

        var records = sink.Snapshot();
        Assert.Equal(6, records.Count);
        Assert.Equal(Enumerable.Range(1, 6).Select(value => (long)value), records.Select(record => record.Sequence));
        Assert.Equal(
            [
                DocumentStoreAccessKind.PrivilegedScoped,
                DocumentStoreAccessKind.PrivilegedGlobal,
                DocumentStoreAccessKind.PrivilegedAcrossScopes
            ],
            records.Where(record => record.EventKind == GroundworkPrivilegedAccessEventKind.Acquisition)
                .Select(record => record.AccessKind));
        Assert.All(
            records.Where(record => record.EventKind == GroundworkPrivilegedAccessEventKind.Outcome),
            record => Assert.Equal(GroundworkPrivilegedAccessOutcome.Succeeded, record.Outcome));
        Assert.Equal(
            ["repair-index", "rotate-host-key", "recover-stalled-work"],
            records.Where(record => record.EventKind == GroundworkPrivilegedAccessEventKind.Acquisition)
                .Select(record => record.Purpose));
        Assert.All(
            records.GroupBy(record => record.AuditId),
            group => Assert.Equal(
                [GroundworkPrivilegedAccessEventKind.Acquisition, GroundworkPrivilegedAccessEventKind.Outcome],
                group.Select(record => record.EventKind)));

        var scoped = records.Where(record => record.AccessKind == DocumentStoreAccessKind.PrivilegedScoped).ToArray();
        Assert.All(scoped, record => Assert.StartsWith("sha256:", record.ScopeReference, StringComparison.Ordinal));
        Assert.All(scoped, record => Assert.DoesNotContain("tenant-a", record.ScopeReference, StringComparison.Ordinal));
        Assert.All(
            records.Where(record => record.AccessKind == DocumentStoreAccessKind.PrivilegedGlobal),
            record => Assert.Equal("global", record.ScopeReference));
        Assert.All(
            records.Where(record => record.AccessKind == DocumentStoreAccessKind.PrivilegedAcrossScopes),
            record => Assert.Equal("across-scopes", record.ScopeReference));
        Assert.DoesNotContain(records, record => record.ToString().Contains("tenant-a", StringComparison.Ordinal));
    }

    [Fact]
    public void Scope_fingerprints_are_stable_distinct_and_never_expose_raw_scope_values()
    {
        var sink = new GroundworkPrivilegedAccessSink();
        var firstRecorder = new GroundworkPrivilegedAccessRecorder(sink);
        var secondRecorder = new GroundworkPrivilegedAccessRecorder(sink);

        var first = firstRecorder.RecordAcquisition(DocumentStoreAccess.PrivilegedScoped(
            new PrivilegedStorageAccess("repair-index"),
            new StorageScope("tenant-sensitive-a")));
        var same = secondRecorder.RecordAcquisition(DocumentStoreAccess.PrivilegedScoped(
            new PrivilegedStorageAccess("repair-index"),
            new StorageScope("tenant-sensitive-a")));
        var different = secondRecorder.RecordAcquisition(DocumentStoreAccess.PrivilegedScoped(
            new PrivilegedStorageAccess("repair-index"),
            new StorageScope("tenant-sensitive-b")));

        Assert.Equal(first.ScopeReference, same.ScopeReference);
        Assert.NotEqual(first.ScopeReference, different.ScopeReference);
        Assert.DoesNotContain("tenant-sensitive", first.ScopeReference, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-sensitive", different.ScopeReference, StringComparison.Ordinal);
    }

    [Fact]
    public void Bounded_buffer_keeps_only_the_latest_events_in_sequence_order()
    {
        var sink = new GroundworkPrivilegedAccessSink(capacity: 3);
        var recorder = new GroundworkPrivilegedAccessRecorder(sink);
        var access = DocumentStoreAccess.PrivilegedGlobal(new PrivilegedStorageAccess("host-repair"));

        var first = recorder.RecordAcquisition(access);
        recorder.RecordOutcome(first, GroundworkPrivilegedAccessOutcome.Succeeded);
        var second = recorder.RecordAcquisition(access);
        recorder.RecordOutcome(second, GroundworkPrivilegedAccessOutcome.Canceled);

        var records = sink.Snapshot();
        Assert.Equal(3, records.Count);
        Assert.Equal([2L, 3L, 4L], records.Select(record => record.Sequence));
        Assert.Equal(GroundworkPrivilegedAccessEventKind.Outcome, records[0].EventKind);
        Assert.Equal(GroundworkPrivilegedAccessOutcome.Canceled, records[^1].Outcome);
    }

    [Fact]
    public void Concurrent_recording_remains_bounded_and_preserves_exact_enqueue_sequence_order()
    {
        const int capacity = 16;
        const int eventCount = 128;
        var sink = new GroundworkPrivilegedAccessSink(capacity);
        var recorder = new GroundworkPrivilegedAccessRecorder(sink);
        var access = DocumentStoreAccess.PrivilegedGlobal(new PrivilegedStorageAccess("concurrent-repair"));
        var audit = recorder.RecordAcquisition(access);

        Parallel.For(0, eventCount, _ =>
            recorder.RecordOutcome(audit, GroundworkPrivilegedAccessOutcome.Succeeded));

        var records = sink.Snapshot();
        Assert.Equal(capacity, records.Count);
        Assert.Equal(
            Enumerable.Range(eventCount + 2 - capacity, capacity).Select(value => (long)value),
            records.Select(record => record.Sequence));
        Assert.Equal(
            records.Select(record => record.Sequence).Order(),
            records.Select(record => record.Sequence));
    }

    [Fact]
    public void Failed_outcome_keeps_only_failure_types_and_never_failure_or_scope_values()
    {
        const string sensitiveScope = "tenant-sensitive";
        const string sensitiveMessage = "Server=db.internal;Password=secret;tenant=tenant-sensitive";
        var sink = new GroundworkPrivilegedAccessSink();
        var recorder = new GroundworkPrivilegedAccessRecorder(sink);
        var access = DocumentStoreAccess.PrivilegedScoped(
            new PrivilegedStorageAccess("repair-projection"),
            new StorageScope(sensitiveScope));
        var audit = recorder.RecordAcquisition(access);

        recorder.RecordOutcome(
            audit,
            GroundworkPrivilegedAccessOutcome.Failed,
            new InvalidOperationException(sensitiveMessage),
            new ApplicationException("Password=cleanup-secret"));

        var record = Assert.Single(
            sink.Snapshot(),
            candidate => candidate.EventKind == GroundworkPrivilegedAccessEventKind.Outcome);
        Assert.Equal("InvalidOperationException", record.FailureType);
        Assert.Equal("ApplicationException", record.CleanupFailureType);
        Assert.Equal("repair-projection", record.Purpose);
        Assert.Equal(GroundworkPrivilegedAccessOutcome.Failed, record.Outcome);
        Assert.DoesNotContain(sensitiveScope, record.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveMessage, record.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Password", record.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Invalid_audit_and_outcome_branches_are_rejected_without_recording()
    {
        var sink = new GroundworkPrivilegedAccessSink();
        var recorder = new GroundworkPrivilegedAccessRecorder(sink);
        var ordinary = DocumentStoreAccess.Scoped(new StorageScope("tenant-a"));
        var privileged = DocumentStoreAccess.PrivilegedGlobal(new PrivilegedStorageAccess("host-repair"));
        var audit = recorder.RecordAcquisition(privileged);
        var initialCount = sink.Snapshot().Count;

        Assert.Throws<ArgumentException>(() => recorder.RecordAcquisition(ordinary));
        Assert.Throws<ArgumentNullException>(() => recorder.RecordAcquisition(null!));
        Assert.Throws<ArgumentNullException>(() => recorder.RecordOutcome(
            null!,
            GroundworkPrivilegedAccessOutcome.Succeeded));
        Assert.Throws<ArgumentNullException>(() => recorder.RecordOutcome(
            audit,
            GroundworkPrivilegedAccessOutcome.Failed));
        Assert.Throws<ArgumentException>(() => recorder.RecordOutcome(
            audit,
            GroundworkPrivilegedAccessOutcome.Succeeded,
            new InvalidOperationException("must-not-be-recorded")));
        Assert.Throws<ArgumentException>(() => recorder.RecordOutcome(
            audit,
            GroundworkPrivilegedAccessOutcome.Succeeded,
            cleanupFailure: new InvalidOperationException("must-not-be-recorded")));
        Assert.Throws<ArgumentOutOfRangeException>(() => recorder.RecordOutcome(
            audit,
            (GroundworkPrivilegedAccessOutcome)42));
        Assert.Equal(initialCount, sink.Snapshot().Count);
    }

    [Fact]
    public void Singleton_sink_retains_records_after_scoped_emitters_are_disposed()
    {
        var services = new ServiceCollection();
        services.AddGroundworkStoreSessions();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var sink = provider.GetRequiredService<GroundworkPrivilegedAccessSink>();
        GroundworkPrivilegedAccessAudit audit;

        using (var scope = provider.CreateScope())
        {
            var recorder = scope.ServiceProvider.GetRequiredService<IGroundworkPrivilegedAccessEmitter>();
            audit = recorder.RecordAcquisition(DocumentStoreAccess.PrivilegedGlobal(
                new PrivilegedStorageAccess("host-repair")));
        }

        using (var scope = provider.CreateScope())
        {
            var recorder = scope.ServiceProvider.GetRequiredService<IGroundworkPrivilegedAccessEmitter>();
            recorder.RecordOutcome(audit, GroundworkPrivilegedAccessOutcome.Succeeded);
        }

        var records = sink.Snapshot();
        Assert.Equal(2, records.Count);
        Assert.Equal(records[0].AuditId, records[1].AuditId);
    }

    [Fact]
    public void Metrics_use_only_bounded_dimensions_and_exclude_purpose_scope_and_failure_values()
    {
        var measurements = new ConcurrentQueue<IReadOnlyList<KeyValuePair<string, object?>>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == GroundworkPrivilegedAccessRecorder.MeterName)
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) => measurements.Enqueue(tags.ToArray()));
        listener.Start();

        const string sensitiveScope = "tenant-sensitive";
        const string sensitivePurpose = "repair-tenant-sensitive";
        const string sensitiveFailure = "Password=secret;tenant=tenant-sensitive";
        var sink = new GroundworkPrivilegedAccessSink();
        var recorder = new GroundworkPrivilegedAccessRecorder(sink);
        var access = DocumentStoreAccess.PrivilegedScoped(
            new PrivilegedStorageAccess(sensitivePurpose),
            new StorageScope(sensitiveScope));

        var audit = recorder.RecordAcquisition(access);
        recorder.RecordOutcome(
            audit,
            GroundworkPrivilegedAccessOutcome.Failed,
            new InvalidOperationException(sensitiveFailure));

        Assert.True(measurements.Count >= 2);
        var tags = measurements.SelectMany(measurement => measurement).ToArray();
        Assert.All(tags, tag => Assert.Contains(tag.Key, new[] { "event.kind", "access.kind", "outcome" }));
        Assert.Contains(tags, tag => tag is { Key: "event.kind", Value: "acquisition" });
        Assert.Contains(tags, tag => tag is { Key: "event.kind", Value: "outcome" });
        var serializedTags = string.Join(
            ";",
            tags.Select(tag => $"{tag.Key}={tag.Value}"));
        Assert.DoesNotContain(sensitiveScope, serializedTags, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitivePurpose, serializedTags, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveFailure, serializedTags, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", serializedTags, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(tags, tag => tag.Key.Contains("scope", StringComparison.OrdinalIgnoreCase));
    }
}
