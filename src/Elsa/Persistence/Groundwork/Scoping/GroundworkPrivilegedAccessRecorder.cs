using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using Groundwork.Documents.Scoping;

namespace Elsa.Persistence.Groundwork.Scoping;

public enum GroundworkPrivilegedAccessEventKind
{
    Acquisition,
    Outcome
}

public enum GroundworkPrivilegedAccessOutcome
{
    Succeeded,
    Failed,
    Canceled,
    Abandoned
}

/// <summary>
/// Sanitized immutable identity shared by the acquisition and terminal records for one privileged session.
/// </summary>
public sealed record GroundworkPrivilegedAccessAudit(
    Guid AuditId,
    DocumentStoreAccessKind AccessKind,
    string ScopeReference,
    string Purpose);

/// <summary>A bounded sanitized privilege event. Raw tenant and exception values are never retained.</summary>
public sealed record GroundworkPrivilegedAccessRecord(
    long Sequence,
    Guid AuditId,
    GroundworkPrivilegedAccessEventKind EventKind,
    DocumentStoreAccessKind AccessKind,
    string ScopeReference,
    string Purpose,
    GroundworkPrivilegedAccessOutcome? Outcome,
    string? FailureType,
    string? CleanupFailureType);

/// <summary>Emits sanitized privilege events from one dependency-injection scope.</summary>
public interface IGroundworkPrivilegedAccessEmitter
{
    GroundworkPrivilegedAccessAudit RecordAcquisition(DocumentStoreAccess access);

    void RecordOutcome(
        GroundworkPrivilegedAccessAudit audit,
        GroundworkPrivilegedAccessOutcome outcome,
        Exception? failure = null,
        Exception? cleanupFailure = null);
}

/// <summary>
/// Application-lifetime bounded privilege-event sink. Sequence allocation and enqueueing share one critical
/// section so snapshots always preserve exact emission order under concurrency.
/// </summary>
public sealed class GroundworkPrivilegedAccessSink
{
    public const string MeterName = "Elsa.Persistence.Groundwork";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> EventCounter = Meter.CreateCounter<long>(
        "elsa.groundwork.privileged_access.events");
    private readonly Lock _gate = new();
    private readonly Queue<GroundworkPrivilegedAccessRecord> _records;
    private readonly int _capacity;
    private long _sequence;

    public GroundworkPrivilegedAccessSink(int capacity = 256)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
        _records = new Queue<GroundworkPrivilegedAccessRecord>(capacity);
    }

    public IReadOnlyList<GroundworkPrivilegedAccessRecord> Snapshot()
    {
        lock (_gate)
            return _records.ToArray();
    }

    internal void RecordAcquisition(GroundworkPrivilegedAccessAudit audit)
    {
        Write(
            audit,
            GroundworkPrivilegedAccessEventKind.Acquisition,
            null,
            null,
            null);
        EventCounter.Add(
            1,
            new KeyValuePair<string, object?>("event.kind", "acquisition"),
            new KeyValuePair<string, object?>("access.kind", AccessKind(audit.AccessKind)));
    }

    internal void RecordOutcome(
        GroundworkPrivilegedAccessAudit audit,
        GroundworkPrivilegedAccessOutcome outcome,
        Exception? failure,
        Exception? cleanupFailure)
    {
        Write(
            audit,
            GroundworkPrivilegedAccessEventKind.Outcome,
            outcome,
            failure?.GetType().Name ?? cleanupFailure?.GetType().Name,
            cleanupFailure?.GetType().Name);
        EventCounter.Add(
            1,
            new KeyValuePair<string, object?>("event.kind", "outcome"),
            new KeyValuePair<string, object?>("access.kind", AccessKind(audit.AccessKind)),
            new KeyValuePair<string, object?>("outcome", Outcome(outcome)));
    }

    private void Write(
        GroundworkPrivilegedAccessAudit audit,
        GroundworkPrivilegedAccessEventKind eventKind,
        GroundworkPrivilegedAccessOutcome? outcome,
        string? failureType,
        string? cleanupFailureType)
    {
        lock (_gate)
        {
            var record = new GroundworkPrivilegedAccessRecord(
                ++_sequence,
                audit.AuditId,
                eventKind,
                audit.AccessKind,
                audit.ScopeReference,
                audit.Purpose,
                outcome,
                failureType,
                cleanupFailureType);
            if (_records.Count == _capacity)
                _records.Dequeue();
            _records.Enqueue(record);
        }
    }

    private static string AccessKind(DocumentStoreAccessKind kind) => kind switch
    {
        DocumentStoreAccessKind.PrivilegedScoped => "privileged-scoped",
        DocumentStoreAccessKind.PrivilegedGlobal => "privileged-global",
        DocumentStoreAccessKind.PrivilegedAcrossScopes => "privileged-across-scopes",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static string Outcome(GroundworkPrivilegedAccessOutcome outcome) => outcome switch
    {
        GroundworkPrivilegedAccessOutcome.Succeeded => "succeeded",
        GroundworkPrivilegedAccessOutcome.Failed => "failed",
        GroundworkPrivilegedAccessOutcome.Canceled => "canceled",
        GroundworkPrivilegedAccessOutcome.Abandoned => "abandoned",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
    };
}

/// <summary>
/// Scoped emitter that strips raw scope values before forwarding events to the singleton bounded sink.
/// Named purposes and sanitized exception type names remain only in the bounded trail, never metric labels.
/// </summary>
public sealed class GroundworkPrivilegedAccessRecorder(GroundworkPrivilegedAccessSink sink)
    : IGroundworkPrivilegedAccessEmitter
{
    public const string MeterName = GroundworkPrivilegedAccessSink.MeterName;

    public GroundworkPrivilegedAccessAudit RecordAcquisition(DocumentStoreAccess access)
    {
        ArgumentNullException.ThrowIfNull(access);
        if (!access.IsPrivileged || access.Privilege is null)
            throw new ArgumentException("Privilege acquisition records require privileged document-store access.", nameof(access));

        var audit = new GroundworkPrivilegedAccessAudit(
            Guid.NewGuid(),
            access.Kind,
            GetScopeReference(access),
            access.Privilege.Reason);
        sink.RecordAcquisition(audit);
        return audit;
    }

    public void RecordOutcome(
        GroundworkPrivilegedAccessAudit audit,
        GroundworkPrivilegedAccessOutcome outcome,
        Exception? failure = null,
        Exception? cleanupFailure = null)
    {
        ArgumentNullException.ThrowIfNull(audit);
        if (!Enum.IsDefined(outcome))
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null);
        if (audit.AccessKind is not (
                DocumentStoreAccessKind.PrivilegedScoped or
                DocumentStoreAccessKind.PrivilegedGlobal or
                DocumentStoreAccessKind.PrivilegedAcrossScopes))
        {
            throw new ArgumentException("Privilege outcomes require a privileged access kind.", nameof(audit));
        }

        if (outcome == GroundworkPrivilegedAccessOutcome.Failed && failure is null && cleanupFailure is null)
            throw new ArgumentNullException(nameof(failure), "A failed privilege outcome requires a failure or cleanup failure.");
        if (outcome != GroundworkPrivilegedAccessOutcome.Failed && failure is not null)
            throw new ArgumentException("Only failed privilege outcomes may carry a failure.", nameof(failure));
        if (outcome != GroundworkPrivilegedAccessOutcome.Failed && cleanupFailure is not null)
            throw new ArgumentException("Only failed privilege outcomes may carry a cleanup failure.", nameof(cleanupFailure));

        sink.RecordOutcome(audit, outcome, failure, cleanupFailure);
    }

    private static string GetScopeReference(DocumentStoreAccess access) => access.Kind switch
    {
        DocumentStoreAccessKind.PrivilegedScoped when access.Scope is not null =>
            $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(access.Scope.Value))).ToLowerInvariant()}",
        DocumentStoreAccessKind.PrivilegedGlobal => "global",
        DocumentStoreAccessKind.PrivilegedAcrossScopes => "across-scopes",
        DocumentStoreAccessKind.PrivilegedScoped =>
            throw new ArgumentException("Privileged scoped access requires a storage scope.", nameof(access)),
        _ => throw new ArgumentException("Privilege acquisition records require privileged access.", nameof(access))
    };
}
