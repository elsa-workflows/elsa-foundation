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

        await ExecuteLeasedAsync([artifactId], leaseId, write, cancellationToken);
    }

    public async ValueTask ExecuteAsync(
        WorkflowExecutableIdentity root,
        string leaseId,
        Func<CancellationToken, ValueTask> write,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);
        ArgumentNullException.ThrowIfNull(write);

        var closure = await new WorkflowExecutableDependencyGraph(executableStore).LoadClosureAsync(root, cancellationToken);
        await ExecuteLeasedAsync(
            closure.Select(executable => executable.Identity.ArtifactId),
            leaseId,
            write,
            cancellationToken);
    }

    private async ValueTask ExecuteLeasedAsync(
        IEnumerable<string> artifactIds,
        string leaseId,
        Func<CancellationToken, ValueTask> write,
        CancellationToken cancellationToken)
    {
        var orderedArtifactIds = artifactIds
            .Select(artifactId =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
                return artifactId;
            })
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (orderedArtifactIds.Length == 0)
            throw new ArgumentException("At least one executable artifact must be leased.", nameof(artifactIds));

        var duration = options.Value.RootWriteLeaseDuration;
        if (duration <= TimeSpan.Zero)
            throw new InvalidOperationException($"{nameof(WorkflowExecutableGarbageCollectionOptions.RootWriteLeaseDuration)} must be greater than zero.");

        var leases = new List<WorkflowExecutableRootWriteLease>(orderedArtifactIds.Length);
        try
        {
            foreach (var artifactId in orderedArtifactIds)
            {
                var now = timeProvider.GetUtcNow();
                var lease = await executableStore.TryAcquireRootWriteLeaseAsync(
                    artifactId,
                    leaseId,
                    now.Add(duration),
                    now,
                    cancellationToken);
                if (lease is null)
                    throw new WorkflowExecutableRootWriteLeaseUnavailableException(artifactId, leaseId);
                leases.Add(lease);
            }
        }
        catch
        {
            await ReleaseAllAsync(leases);
            throw;
        }

        using var renewalStop = new CancellationTokenSource();
        using var writeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var renewalTask = RenewUntilStoppedAsync(leases, duration, renewalStop.Token, writeCancellation);
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

            await ReleaseAllAsync(leases);
        }

        if (renewalFailure is not null)
            throw renewalFailure;

        if (writeFailure is not null)
            ExceptionDispatchInfo.Capture(writeFailure).Throw();
    }

    private async Task RenewUntilStoppedAsync(
        IReadOnlyCollection<WorkflowExecutableRootWriteLease> leases,
        TimeSpan duration,
        CancellationToken stopToken,
        CancellationTokenSource writeCancellation)
    {
        var cadence = TimeSpan.FromTicks(Math.Max(1, duration.Ticks / 3));
        while (true)
        {
            await Task.Delay(cadence, timeProvider, stopToken);
            foreach (var lease in leases)
            {
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

    private async ValueTask ReleaseAllAsync(IEnumerable<WorkflowExecutableRootWriteLease> leases)
    {
        Exception? firstFailure = null;
        foreach (var lease in leases.Reverse())
        {
            try
            {
                await executableStore.ReleaseRootWriteLeaseAsync(lease, CancellationToken.None);
            }
            catch (Exception exception)
            {
                firstFailure ??= exception;
            }
        }

        if (firstFailure is not null)
            ExceptionDispatchInfo.Capture(firstFailure).Throw();
    }
}
