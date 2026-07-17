using System.Runtime.ExceptionServices;
using Elsa.Persistence.Core;

namespace Elsa.Persistence.Groundwork.Scoping;

/// <summary>Creates one immutable Groundwork session from the current scoped Elsa access context.</summary>
public sealed class GroundworkStoreSessionFactory(
    IPersistenceAccessContextAccessor accessContextAccessor,
    IGroundworkStoreSessionSource source,
    IGroundworkPrivilegedAccessEmitter? privilegedAccessEmitter = null) : IGroundworkStoreSessionFactory
{
    public async ValueTask<GroundworkStoreSession> CreateAsync(
        PersistenceAccessPolicy requiredPolicy,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = accessContextAccessor.Current
            ?? throw new InvalidOperationException("The current persistence access context is unavailable.");
        var access = GroundworkPersistenceAccessMapper.Map(context, requiredPolicy);
        GroundworkStoreSessionResources? resources = null;
        try
        {
            resources = await source.OpenAsync(access, cancellationToken);
            if (resources.DocumentStore.Access != access)
            {
                throw new InvalidOperationException(
                    "The Groundwork session source returned resources bound to a different access context.");
            }

            GroundworkPrivilegedAccessAudit? audit = null;
            IGroundworkPrivilegedAccessEmitter? sessionEmitter = null;
            if (access.IsPrivileged)
            {
                sessionEmitter = privilegedAccessEmitter
                    ?? throw new InvalidOperationException("Privileged Groundwork sessions require an audit emitter.");
                audit = sessionEmitter.RecordAcquisition(access);
            }

            return new GroundworkStoreSession(access, resources, sessionEmitter, audit);
        }
        catch (Exception exception)
        {
            if (resources?.Lease is not null)
            {
                try
                {
                    await resources.Lease.DisposeAsync();
                }
                catch (Exception cleanupFailure)
                {
                    GroundworkSessionFailure.Attach(exception, "CleanupFailure", cleanupFailure);
                }
            }

            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
    }

    public async ValueTask<TResult> ExecutePrivilegedAsync<TResult>(
        Func<GroundworkStoreSession, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var session = await CreateAsync(PersistenceAccessPolicy.Privileged, cancellationToken);
        TResult result = default!;
        Exception? operationFailure = null;
        GroundworkPrivilegedAccessOutcome outcome;
        try
        {
            result = await operation(session, cancellationToken);
            outcome = GroundworkPrivilegedAccessOutcome.Succeeded;
        }
        catch (OperationCanceledException exception)
        {
            operationFailure = exception;
            outcome = GroundworkPrivilegedAccessOutcome.Canceled;
        }
        catch (Exception exception)
        {
            operationFailure = exception;
            outcome = GroundworkPrivilegedAccessOutcome.Failed;
        }

        session.RecordPrivilegedOutcome(
            outcome,
            outcome == GroundworkPrivilegedAccessOutcome.Failed ? operationFailure : null);

        Exception? completionFailure = null;
        try
        {
            await session.DisposeAsync();
        }
        catch (Exception exception)
        {
            completionFailure = exception;
        }

        if (operationFailure is not null)
        {
            if (completionFailure is not null)
                GroundworkSessionFailure.Attach(operationFailure, "SessionCompletionFailure", completionFailure);
            ExceptionDispatchInfo.Capture(operationFailure).Throw();
        }

        if (completionFailure is not null)
            ExceptionDispatchInfo.Capture(completionFailure).Throw();

        return result;
    }

    public ValueTask<TResult> ExecutePrivilegedAcrossScopesAsync<TResult>(
        Func<GroundworkStoreSession, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        var context = accessContextAccessor.Current
            ?? throw new InvalidOperationException("The current persistence access context is unavailable.");
        if (context.AccessPolicy != PersistenceAccessPolicy.Privileged || !context.AcrossScopes)
        {
            throw new InvalidOperationException(
                "The persistence operation requires explicit privileged across-scope access.");
        }

        return ExecutePrivilegedAsync(operation, cancellationToken);
    }
}
