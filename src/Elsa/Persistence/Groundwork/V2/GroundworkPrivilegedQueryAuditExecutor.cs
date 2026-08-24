using System.Diagnostics.Metrics;
using Elsa.Persistence.Core;
using Groundwork.Kernel;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Composition;

/// <summary>The terminal outcome of one acquired privileged public-v2 query operation.</summary>
public enum GroundworkPrivilegedQueryOutcome
{
    Succeeded,
    Failed,
    Canceled
}

/// <summary>The immutable acquisition identity paired with one terminal query outcome.</summary>
public sealed record GroundworkPrivilegedQueryAuditAcquisition(
    Guid Id,
    string AuditIdentity,
    string Purpose,
    StorageAccess Access);

/// <summary>Receives exactly one acquisition and one terminal outcome for each admitted privileged query.</summary>
public interface IGroundworkPrivilegedQueryAuditSink
{
    GroundworkPrivilegedQueryAuditAcquisition RecordAcquisition(StorageAccess access);

    void RecordOutcome(
        GroundworkPrivilegedQueryAuditAcquisition acquisition,
        GroundworkPrivilegedQueryOutcome outcome,
        Exception? failure = null);
}

/// <summary>A bounded, sanitized public-v2 privilege audit event.</summary>
public sealed record GroundworkPrivilegedQueryAuditRecord(
    long Sequence,
    Guid AcquisitionId,
    GroundworkPrivilegedQueryAuditEventKind EventKind,
    StorageAccessKind AccessKind,
    string AuditIdentity,
    string Purpose,
    GroundworkPrivilegedQueryOutcome? Outcome,
    string? FailureType);

public enum GroundworkPrivilegedQueryAuditEventKind
{
    Acquisition,
    Outcome
}

/// <summary>
/// Application-lifetime bounded privilege telemetry for public-v2 queries. Scope values and exception
/// messages are never retained; only validated audit identity/purpose and exception type names are kept.
/// </summary>
public sealed class GroundworkPrivilegedQueryAuditSink :
    IGroundworkPrivilegedQueryAuditSink,
    IStorageAccessObserver
{
    public const string MeterName = "Elsa.Persistence.Groundwork.V2";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> EventCounter = Meter.CreateCounter<long>(
        "elsa.groundwork.v2.privileged_query.events");
    private static readonly Counter<long> ProviderOperationCounter = Meter.CreateCounter<long>(
        "elsa.groundwork.v2.privileged_query.provider_operations");
    private readonly Lock gate = new();
    private readonly Queue<GroundworkPrivilegedQueryAuditRecord> records;
    private readonly int capacity;
    private long sequence;

    public GroundworkPrivilegedQueryAuditSink(int capacity = 256)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        this.capacity = capacity;
        records = new Queue<GroundworkPrivilegedQueryAuditRecord>(capacity);
    }

    public IReadOnlyList<GroundworkPrivilegedQueryAuditRecord> Snapshot()
    {
        lock (gate)
            return records.ToArray();
    }

    public GroundworkPrivilegedQueryAuditAcquisition RecordAcquisition(StorageAccess access)
    {
        ArgumentNullException.ThrowIfNull(access);
        var audit = access.Audit;
        if (access.Kind != StorageAccessKind.PrivilegedAcrossScopes || audit is null)
        {
            throw new ArgumentException(
                "Public-v2 privileged query audit requires explicit across-scope access.",
                nameof(access));
        }

        var acquisition = new GroundworkPrivilegedQueryAuditAcquisition(
            Guid.NewGuid(),
            audit.Identity,
            audit.Purpose,
            access);
        Write(
            acquisition,
            GroundworkPrivilegedQueryAuditEventKind.Acquisition,
            null,
            null);
        EventCounter.Add(1, new KeyValuePair<string, object?>("event.kind", "acquisition"));
        return acquisition;
    }

    public void RecordOutcome(
        GroundworkPrivilegedQueryAuditAcquisition acquisition,
        GroundworkPrivilegedQueryOutcome outcome,
        Exception? failure = null)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        if (!Enum.IsDefined(outcome))
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null);
        if (outcome == GroundworkPrivilegedQueryOutcome.Failed && failure is null)
            throw new ArgumentNullException(nameof(failure), "A failed outcome requires a failure.");
        if (outcome != GroundworkPrivilegedQueryOutcome.Failed && failure is not null)
        {
            throw new ArgumentException(
                "Only failed outcomes may carry a failure.",
                nameof(failure));
        }

        Write(
            acquisition,
            GroundworkPrivilegedQueryAuditEventKind.Outcome,
            outcome,
            failure?.GetType().Name);
        EventCounter.Add(1, new KeyValuePair<string, object?>("event.kind", "outcome"));
    }

    /// <summary>Receives provider operation notifications without retaining provider or scope data.</summary>
    public void Observe(StorageAccessEvent accessEvent)
    {
        ArgumentNullException.ThrowIfNull(accessEvent);
        ProviderOperationCounter.Add(1);
    }

    private void Write(
        GroundworkPrivilegedQueryAuditAcquisition acquisition,
        GroundworkPrivilegedQueryAuditEventKind eventKind,
        GroundworkPrivilegedQueryOutcome? outcome,
        string? failureType)
    {
        var audit = acquisition.Access.Audit
            ?? throw new InvalidOperationException("A public-v2 audit acquisition requires provider audit metadata.");
        lock (gate)
        {
            var record = new GroundworkPrivilegedQueryAuditRecord(
                ++sequence,
                acquisition.Id,
                eventKind,
                acquisition.Access.Kind,
                audit.Identity,
                audit.Purpose,
                outcome,
                failureType);
            if (records.Count == capacity)
                records.Dequeue();
            records.Enqueue(record);
        }
    }
}

/// <summary>
/// Shared public-v2 executor for privileged cross-scope queries. It refuses ordinary and privileged-global
/// contexts before unit/session acquisition, exposes only the query capability, and pairs every acquired
/// session with exactly one terminal outcome.
/// </summary>
public sealed class GroundworkPrivilegedQueryAuditExecutor(
    IGroundworkStorageSessionSource sessions,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IGroundworkPrivilegedQueryAuditSink auditSink)
{
    public TResult Execute<TResult>(
        string unitId,
        string auditIdentity,
        Func<IPrivilegedCrossScopeQuerySession, TResult> operation,
        CancellationToken cancellationToken = default,
        string? targetName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unitId);
        ArgumentException.ThrowIfNullOrWhiteSpace(auditIdentity);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(auditSink);

        var access = ResolveAccess(unitId, auditIdentity, cancellationToken, targetName);
        var acquisition = auditSink.RecordAcquisition(access);
        var terminal = 0;
        try
        {
            var session = OpenQuerySession(unitId, access, targetName);
            var result = operation(session);
            RecordOutcomeOnce(ref terminal, acquisition, GroundworkPrivilegedQueryOutcome.Succeeded);
            return result;
        }
        catch (OperationCanceledException operationFailure) when (Volatile.Read(ref terminal) == 0)
        {
            RecordOperationOutcome(
                ref terminal,
                acquisition,
                GroundworkPrivilegedQueryOutcome.Canceled,
                operationFailure);
            throw;
        }
        catch (Exception operationFailure) when (Volatile.Read(ref terminal) == 0)
        {
            RecordOperationOutcome(
                ref terminal,
                acquisition,
                GroundworkPrivilegedQueryOutcome.Failed,
                operationFailure);
            throw;
        }
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        string unitId,
        string auditIdentity,
        Func<IPrivilegedCrossScopeQuerySession, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default,
        string? targetName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unitId);
        ArgumentException.ThrowIfNullOrWhiteSpace(auditIdentity);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(auditSink);

        var access = ResolveAccess(unitId, auditIdentity, cancellationToken, targetName);
        var acquisition = auditSink.RecordAcquisition(access);
        var terminal = 0;
        try
        {
            var session = OpenQuerySession(unitId, access, targetName);
            var result = await operation(session, cancellationToken);
            RecordOutcomeOnce(ref terminal, acquisition, GroundworkPrivilegedQueryOutcome.Succeeded);
            return result;
        }
        catch (OperationCanceledException operationFailure) when (Volatile.Read(ref terminal) == 0)
        {
            RecordOperationOutcome(
                ref terminal,
                acquisition,
                GroundworkPrivilegedQueryOutcome.Canceled,
                operationFailure);
            throw;
        }
        catch (Exception operationFailure) when (Volatile.Read(ref terminal) == 0)
        {
            RecordOperationOutcome(
                ref terminal,
                acquisition,
                GroundworkPrivilegedQueryOutcome.Failed,
                operationFailure);
            throw;
        }
    }

    private StorageAccess ResolveAccess(
        string unitId,
        string auditIdentity,
        CancellationToken cancellationToken,
        string? targetName)
    {
        var context = accessContextAccessor.Current
            ?? throw new InvalidOperationException("The current persistence access context is unavailable.");
        if (context.AccessPolicy != PersistenceAccessPolicy.Privileged || !context.AcrossScopes)
        {
            throw new InvalidOperationException(
                "Privileged public-v2 queries require an explicit privileged-across-scopes context.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var unit = sessions.Unit(unitId, targetName);
        return GroundworkStorageAccessMapper.Map(
            context,
            unit.Scope,
            auditIdentity,
            auditSink as IStorageAccessObserver);
    }

    private IPrivilegedCrossScopeQuerySession OpenQuerySession(
        string unitId,
        StorageAccess access,
        string? targetName)
    {
        var session = sessions.Open(unitId, access, targetName);
        return session as IPrivilegedCrossScopeQuerySession
            ?? throw new InvalidOperationException(
                $"Groundwork unit '{unitId}' did not expose the public privileged cross-scope query capability.");
    }

    private void RecordOperationOutcome(
        ref int terminal,
        GroundworkPrivilegedQueryAuditAcquisition acquisition,
        GroundworkPrivilegedQueryOutcome outcome,
        Exception operationFailure)
    {
        try
        {
            RecordOutcomeOnce(
                ref terminal,
                acquisition,
                outcome,
                outcome == GroundworkPrivilegedQueryOutcome.Failed ? operationFailure : null);
        }
        catch (Exception completionFailure)
        {
            throw new AggregateException(
                "The public-v2 query failed while completing its audit outcome.",
                operationFailure,
                completionFailure);
        }
    }

    private void RecordOutcomeOnce(
        ref int terminal,
        GroundworkPrivilegedQueryAuditAcquisition acquisition,
        GroundworkPrivilegedQueryOutcome outcome,
        Exception? failure = null)
    {
        if (Interlocked.Exchange(ref terminal, 1) != 0)
            return;
        auditSink.RecordOutcome(acquisition, outcome, failure);
    }
}
