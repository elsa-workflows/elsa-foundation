using System.Runtime.ExceptionServices;
using Elsa.Workflows.Runtime.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>Default scope-based coordinator for executable retention-root writes.</summary>
public sealed class WorkflowExecutableRootWriteLeaseManager(
    IWorkflowExecutableStore executableStore,
    IOptions<WorkflowExecutableGarbageCollectionOptions> options,
    TimeProvider timeProvider) : IWorkflowExecutableRootWriteLeaseManager
{
    public async ValueTask ExecuteAsync(
        string artifactId,
        string leaseId,
        Func<CancellationToken, ValueTask> write,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);
        ArgumentNullException.ThrowIfNull(write);

        var duration = options.Value.RootWriteLeaseDuration;
        if (duration <= TimeSpan.Zero)
            throw new InvalidOperationException($"{nameof(WorkflowExecutableGarbageCollectionOptions.RootWriteLeaseDuration)} must be greater than zero.");

        var now = timeProvider.GetUtcNow();
        var lease = await executableStore.TryAcquireRootWriteLeaseAsync(
            artifactId,
            leaseId,
            now.Add(duration),
            now,
            cancellationToken);

        if (lease is null)
            throw new WorkflowExecutableRootWriteLeaseUnavailableException(artifactId, leaseId);

        using var renewalStop = new CancellationTokenSource();
        using var writeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var renewalTask = RenewUntilStoppedAsync(lease, duration, renewalStop.Token, writeCancellation);
        Exception? writeFailure = null;
        Exception? renewalFailure = null;
        try
        {
            await write(writeCancellation.Token);
        }
        catch (Exception exception)
        {
            writeFailure = exception;
        }
        finally
        {
            await renewalStop.CancelAsync();
            try
            {
                await renewalTask;
            }
            catch (OperationCanceledException) when (renewalStop.IsCancellationRequested)
            {
                // Expected when the write completes before the next renewal cadence.
            }
            catch (Exception exception)
            {
                renewalFailure = exception;
            }

            await executableStore.ReleaseRootWriteLeaseAsync(lease, CancellationToken.None);
        }

        if (renewalFailure is not null)
            throw renewalFailure;

        if (writeFailure is not null)
            ExceptionDispatchInfo.Capture(writeFailure).Throw();
    }

    private async Task RenewUntilStoppedAsync(
        WorkflowExecutableRootWriteLease lease,
        TimeSpan duration,
        CancellationToken stopToken,
        CancellationTokenSource writeCancellation)
    {
        var cadence = TimeSpan.FromTicks(Math.Max(1, duration.Ticks / 3));
        while (true)
        {
            await Task.Delay(cadence, timeProvider, stopToken);
            var now = timeProvider.GetUtcNow();
            var renewed = await executableStore.RenewRootWriteLeaseAsync(
                lease,
                now.Add(duration),
                now,
                stopToken);

            if (renewed)
                continue;

            await writeCancellation.CancelAsync();
            throw new WorkflowExecutableRootWriteLeaseLostException(lease.ArtifactId, lease.LeaseId);
        }
    }
}
