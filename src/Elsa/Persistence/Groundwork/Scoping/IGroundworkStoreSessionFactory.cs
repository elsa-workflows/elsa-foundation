using System.Runtime.ExceptionServices;
using Elsa.Persistence.Core;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Scoping;

/// <summary>Opens immutable Groundwork resources for one already-authorized Elsa access context.</summary>
public interface IGroundworkStoreSessionFactory
{
    ValueTask<GroundworkStoreSession> CreateAsync(
        PersistenceAccessPolicy requiredPolicy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes one privileged operation and records its terminal outcome after provider-lease cleanup.
    /// </summary>
    ValueTask<TResult> ExecutePrivilegedAsync<TResult>(
        Func<GroundworkStoreSession, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes one privileged operation only when the already-authorized context explicitly permits
    /// cross-scope access. Rejection occurs before provider resources are acquired.
    /// </summary>
    ValueTask<TResult> ExecutePrivilegedAcrossScopesAsync<TResult>(
        Func<GroundworkStoreSession, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provider-owned source backed by static admitted resources. Implementations create an access-bound
/// store without changing schema or retaining request state.
/// </summary>
public interface IGroundworkStoreSessionSource
{
    ValueTask<GroundworkStoreSessionResources> OpenAsync(
        DocumentStoreAccess access,
        CancellationToken cancellationToken = default);
}

/// <summary>The access-bound stores and optional provider lease returned by one source acquisition.</summary>
public sealed record GroundworkStoreSessionResources
{
    public GroundworkStoreSessionResources(
        IDocumentStore documentStore,
        IBoundedDocumentStore boundedDocumentStore,
        IAsyncDisposable? lease = null)
    {
        DocumentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
        BoundedDocumentStore = boundedDocumentStore ?? throw new ArgumentNullException(nameof(boundedDocumentStore));
        Lease = lease;
    }

    public IDocumentStore DocumentStore { get; }

    public IBoundedDocumentStore BoundedDocumentStore { get; }

    public IAsyncDisposable? Lease { get; }
}

/// <summary>An immutable access-bound session. Disposal releases its provider lease and finalizes its audit outcome.</summary>
public sealed class GroundworkStoreSession : IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly IGroundworkPrivilegedAccessEmitter? _privilegedAccessEmitter;
    private readonly GroundworkPrivilegedAccessAudit? _privilegedAccessAudit;
    private GroundworkStoreSessionResources? _resources;
    private PrivilegedCompletion? _privilegedCompletion;
    private Task? _disposeTask;

    internal GroundworkStoreSession(
        DocumentStoreAccess access,
        GroundworkStoreSessionResources resources,
        IGroundworkPrivilegedAccessEmitter? privilegedAccessEmitter,
        GroundworkPrivilegedAccessAudit? privilegedAccessAudit)
    {
        Access = access ?? throw new ArgumentNullException(nameof(access));
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        if (access.IsPrivileged != (privilegedAccessEmitter is not null && privilegedAccessAudit is not null))
        {
            throw new ArgumentException(
                "Privileged sessions require an audit emitter and acquisition identity; ordinary sessions require neither.",
                nameof(privilegedAccessEmitter));
        }

        _privilegedAccessEmitter = privilegedAccessEmitter;
        _privilegedAccessAudit = privilegedAccessAudit;
    }

    public DocumentStoreAccess Access { get; }

    public IDocumentStore DocumentStore => Resources.DocumentStore;

    public IBoundedDocumentStore BoundedDocumentStore => Resources.BoundedDocumentStore;

    /// <summary>Records the terminal outcome of a privileged session exactly once.</summary>
    public void RecordPrivilegedOutcome(
        GroundworkPrivilegedAccessOutcome outcome,
        Exception? failure = null)
    {
        if (!Access.IsPrivileged)
            throw new InvalidOperationException("Only privileged Groundwork sessions have a privileged access outcome.");
        if (!Enum.IsDefined(outcome))
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null);
        if (outcome == GroundworkPrivilegedAccessOutcome.Failed && failure is null)
            throw new ArgumentNullException(nameof(failure), "A failed privilege outcome requires a failure.");
        if (outcome != GroundworkPrivilegedAccessOutcome.Failed && failure is not null)
            throw new ArgumentException("Only failed privilege outcomes may carry a failure.", nameof(failure));

        lock (_gate)
        {
            if (_disposeTask is not null)
                throw new ObjectDisposedException(nameof(GroundworkStoreSession));
            if (_privilegedCompletion is not null)
                throw new InvalidOperationException("The privileged Groundwork session outcome has already been recorded.");
            _privilegedCompletion = new PrivilegedCompletion(outcome, failure);
        }
    }

    public ValueTask DisposeAsync()
    {
        Task disposeTask;
        lock (_gate)
        {
            _disposeTask ??= DisposeCoreAsync(Interlocked.Exchange(ref _resources, null)!);
            disposeTask = _disposeTask;
        }

        return new ValueTask(disposeTask);
    }

    private async Task DisposeCoreAsync(GroundworkStoreSessionResources? resources)
    {
        if (resources is null)
            return;

        Exception? cleanupFailure = null;
        try
        {
            if (resources.Lease is not null)
                await resources.Lease.DisposeAsync();
        }
        catch (Exception exception)
        {
            cleanupFailure = exception;
        }

        if (_privilegedAccessEmitter is not null && _privilegedAccessAudit is not null)
        {
            var completion = _privilegedCompletion
                ?? new PrivilegedCompletion(GroundworkPrivilegedAccessOutcome.Abandoned, null);
            _privilegedCompletion = null;
            var terminalOutcome = cleanupFailure is null
                ? completion.Outcome
                : GroundworkPrivilegedAccessOutcome.Failed;
            try
            {
                _privilegedAccessEmitter.RecordOutcome(
                    _privilegedAccessAudit,
                    terminalOutcome,
                    completion.Failure,
                    cleanupFailure);
            }
            catch (Exception auditFailure)
            {
                if (cleanupFailure is null)
                    throw;
                GroundworkSessionFailure.Attach(cleanupFailure, "PrivilegedAuditFailure", auditFailure);
            }
        }

        if (cleanupFailure is not null)
            ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
    }

    private GroundworkStoreSessionResources Resources => Volatile.Read(ref _resources)
        ?? throw new ObjectDisposedException(nameof(GroundworkStoreSession));

    private sealed record PrivilegedCompletion(
        GroundworkPrivilegedAccessOutcome Outcome,
        Exception? Failure);
}

internal static class GroundworkSessionFailure
{
    private const string DataKeyPrefix = "Elsa.Persistence.Groundwork.";

    public static void Attach(Exception primary, string role, Exception secondary)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(secondary);
        primary.Data[$"{DataKeyPrefix}{role}"] = secondary;
    }
}
