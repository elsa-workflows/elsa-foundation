using System.Runtime.ExceptionServices;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Services.Executables;

/// <summary>Default scope-based coordinator for executable retention-root writes.</summary>
public sealed class WorkflowExecutableRootWriteLeaseManager(
    IWorkflowExecutableStore executableStore,
    IOptions<WorkflowExecutableGarbageCollectionOptions> options,
    TimeProvider timeProvider,
    ILogger<WorkflowExecutableRootWriteLeaseManager>? logger = null) : IWorkflowExecutableRootWriteLeaseManager
{
    private readonly ILogger _logger = logger ?? NullLogger<WorkflowExecutableRootWriteLeaseManager>.Instance;

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
        CancellationToken cancellationToken,
        bool verifyBeforeWrite = false)
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
        var grantDeadlines = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
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

            // A large root set can take longer to acquire than its first lease's lifetime. No callback may begin
            // on an already expired grant. Revalidate all tokens and the earliest renewed deadline before writing.
            if (verifyBeforeWrite)
            {
                foreach (var lease in leases)
                {
                    var now = timeProvider.GetUtcNow();
                    var expiresAt = now.Add(duration);
                    if (!await executableStore.RenewRootWriteLeaseAsync(lease, expiresAt, now, cancellationToken))
                        throw new WorkflowExecutableRootWriteLeaseLostException(lease.ArtifactId, lease.LeaseId);
                    grantDeadlines[lease.ArtifactId] = expiresAt;
                }
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        catch
        {
            await ReleaseAllAsync(leases);
            throw;
        }

        using var renewalStop = new CancellationTokenSource();
        using var writeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        LeaseDeadlineGuard? deadlineGuard = null;
        var renewalTask = Task.CompletedTask;
        Exception? writeFailure = null;
        Exception? renewalFailure = null;
        try
        {
            deadlineGuard = verifyBeforeWrite ? new LeaseDeadlineGuard(grantDeadlines, leaseId, timeProvider, writeCancellation) : null;
            renewalTask = RenewUntilStoppedAsync(leases, duration, renewalStop.Token, writeCancellation, deadlineGuard);
            deadlineGuard?.ThrowIfLost();
            await write(writeCancellation.Token);
        }
        catch (Exception exception)
        {
            writeFailure = exception;
        }
        finally
        {
            deadlineGuard?.Dispose();
            await renewalStop.CancelAsync();
            try
            {
                await renewalTask;
            }
            catch (OperationCanceledException) when (deadlineGuard is null && renewalStop.IsCancellationRequested)
            {
                // Expected when the write completes before the next renewal cadence.
            }
            catch (Exception exception)
            {
                renewalFailure = exception;
            }

            if (deadlineGuard is not null) await deadlineGuard.ObserveCancellationAsync();
            await ReleaseAllAsync(leases);
        }

        var leaseFailure = CombineFailures(renewalFailure, deadlineGuard?.GetFailure());
        if (verifyBeforeWrite && leaseFailure is not null && writeFailure is not null and not OperationCanceledException)
            leaseFailure = CombineFailures(leaseFailure, writeFailure);
        if (leaseFailure is not null) ExceptionDispatchInfo.Capture(leaseFailure).Throw();
        if (writeFailure is not null)
            ExceptionDispatchInfo.Capture(writeFailure).Throw();
    }

    private static Exception? CombineFailures(Exception? first, Exception? second)
    {
        if (first is null) return second;
        if (second is null || ReferenceEquals(first, second)) return first;
        static IEnumerable<Exception> Causes(Exception failure) => failure is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions : [failure];
        var causes = Causes(first).Concat(Causes(second)).Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
        return causes.Length == 1 ? causes[0] : new AggregateException("Executable retention lease and cancellation failures.", causes);
    }

    public async ValueTask ExecuteAsync(
        IReadOnlyCollection<WorkflowExecutableIdentity> roots,
        string leaseId,
        Func<CancellationToken, ValueTask> write,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);
        ArgumentNullException.ThrowIfNull(write);
        cancellationToken.ThrowIfCancellationRequested();
        var savedRoots = roots.ToArray();
        if (savedRoots.Length == 0 || savedRoots.Any(root => root is null))
            throw new ArgumentException("An atomic root set requires at least one non-null executable identity.", nameof(roots));
        var graph = new WorkflowExecutableDependencyGraph(executableStore);
        var artifactIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in savedRoots)
        {
            var closure = await graph.LoadClosureAsync(root, cancellationToken);
            if (!WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(
                    closure.Single(executable => executable.Identity.ArtifactId == root.ArtifactId).Identity, root))
                throw new WorkflowExecutableDependencyGraphException(WorkflowExecutableDependencyGraphFailureKind.ConflictingIdentity,
                    "The requested root differs from its complete immutable executable pin.");
            artifactIds.UnionWith(closure.Select(executable => executable.Identity.ArtifactId));
        }
        await ExecuteLeasedAsync(artifactIds, leaseId, write, cancellationToken, verifyBeforeWrite: true);
    }

    private async Task RenewUntilStoppedAsync(
        IReadOnlyCollection<WorkflowExecutableRootWriteLease> leases,
        TimeSpan duration,
        CancellationToken stopToken,
        CancellationTokenSource writeCancellation,
        LeaseDeadlineGuard? deadlineGuard = null)
    {
        try
        {
            var cadence = TimeSpan.FromTicks(Math.Max(1, duration.Ticks / 3));
            while (true)
            {
                await Task.Delay(cadence, timeProvider, stopToken);
                foreach (var lease in leases)
                {
                    var now = timeProvider.GetUtcNow();
                    var expiresAt = now.Add(duration);
                    var renewed = await executableStore.RenewRootWriteLeaseAsync(
                        lease,
                        expiresAt,
                        now,
                        stopToken);

                    if (renewed)
                    {
                        deadlineGuard?.Refresh(lease.ArtifactId, expiresAt);
                        continue;
                    }

                    if (deadlineGuard is null) await writeCancellation.CancelAsync();
                    throw new WorkflowExecutableRootWriteLeaseLostException(lease.ArtifactId, lease.LeaseId);
                }
            }
        }
        catch (OperationCanceledException exception) when (deadlineGuard is not null &&
            stopToken.IsCancellationRequested && exception.CancellationToken == stopToken)
        {
            // Collection-path shutdown completes normally; an earlier unrequested provider cancellation remains
            // a provider failure and must not be hidden just because the callback subsequently stopped this loop.
        }
        catch (Exception failure)
        {
            if (deadlineGuard is not null && !stopToken.IsCancellationRequested)
            {
                try { await writeCancellation.CancelAsync(); }
                catch (Exception cancellationFailure)
                { throw new AggregateException("Executable retention lease renewal and cancellation failed.", failure, cancellationFailure); }
            }
            throw;
        }
    }

    private sealed class LeaseDeadlineGuard : IDisposable
    {
        private readonly object gate = new();
        private readonly Dictionary<string, DateTimeOffset> deadlines;
        private readonly string leaseId;
        private readonly TimeProvider clock;
        private readonly CancellationTokenSource writeCancellation;
        private readonly ITimer timer;
        private Exception? failure;
        private TaskCompletionSource? cancellationCompletion;
        private bool disposed;

        public LeaseDeadlineGuard(Dictionary<string, DateTimeOffset> deadlines, string leaseId, TimeProvider clock,
            CancellationTokenSource writeCancellation)
        {
            this.deadlines = deadlines; this.leaseId = leaseId; this.clock = clock; this.writeCancellation = writeCancellation;
            timer = clock.CreateTimer(_ => CheckDeadline(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            CheckDeadline();
        }

        public void Refresh(string artifactId, DateTimeOffset expiresAt)
        {
            // A late successful provider response cannot resurrect a grant after its previously known deadline.
            CheckDeadline();
            lock (gate)
            {
                if (failure is null && !disposed) deadlines[artifactId] = expiresAt;
            }
            CheckDeadline();
            ThrowIfLost();
        }

        private void CheckDeadline()
        {
            var cancel = false;
            TaskCompletionSource? completion = null;
            lock (gate)
            {
                if (disposed || failure is not null) return;
                var earliest = deadlines.MinBy(pair => pair.Value);
                var remaining = earliest.Value - clock.GetUtcNow();
                if (remaining > TimeSpan.Zero) timer.Change(remaining, Timeout.InfiniteTimeSpan);
                else
                {
                    failure = new WorkflowExecutableRootWriteLeaseLostException(earliest.Key, leaseId);
                    completion = cancellationCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    cancel = true;
                }
            }
            if (cancel)
            {
                try { writeCancellation.Cancel(); }
                catch (ObjectDisposedException) { /* The completed write already disposed its linked cancellation. */ }
                catch (Exception cancellationFailure)
                {
                    lock (gate) failure = new AggregateException("Executable retention lease expiry and cancellation failed.", failure!, cancellationFailure);
                }
                finally { completion!.TrySetResult(); }
            }
        }

        public Task ObserveCancellationAsync()
        {
            lock (gate) return cancellationCompletion?.Task ?? Task.CompletedTask;
        }

        public void ThrowIfLost()
        {
            lock (gate)
                if (failure is not null) throw failure;
        }

        public Exception? GetFailure()
        {
            lock (gate) return failure;
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (!disposed && failure is null)
                {
                    var earliest = deadlines.MinBy(pair => pair.Value);
                    if (earliest.Value <= clock.GetUtcNow()) failure = new WorkflowExecutableRootWriteLeaseLostException(earliest.Key, leaseId);
                }
                disposed = true;
            }
            timer.Dispose();
        }
    }

    // A release only shortens a lease that would otherwise expire on its own, so it never decides the outcome: a write
    // that succeeded stays a success, and a failed write surfaces its own exception. A lease that could
    // not be released holds its artifact from collection until it expires.
    private async ValueTask ReleaseAllAsync(IEnumerable<WorkflowExecutableRootWriteLease> leases)
    {
        foreach (var lease in leases.Reverse())
        {
            try
            {
                await executableStore.ReleaseRootWriteLeaseAsync(lease, CancellationToken.None);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Root-write lease {LeaseId} on workflow executable {ArtifactId} could not be released; it holds the executable from collection until it expires.",
                    lease.LeaseId,
                    lease.ArtifactId);
            }
        }
    }
}
