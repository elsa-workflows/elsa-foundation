using Elsa.Jobs.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Jobs;

internal enum JobStopReason { None, Cancelled, TimedOut, OwnershipLost, Shutdown }

/// <summary>Only process-local resources for one physical attempt; the provider remains lifecycle authority.</summary>
internal sealed class JobAttemptSession(JobAttemptLease lease) : IDisposable
{
    private JobAttemptLease _lease = lease;
    private int _reason;
    private int _ownershipLost;
    private int _exited;
    private CancellationTokenRegistration _shutdownSignal;

    internal SemaphoreSlim Commands { get; } = new(1, 1);
    internal SemaphoreSlim Reports { get; } = new(1, 1);
    internal CancellationTokenSource HandlerCancellation { get; } = new();
    internal JobAttemptLease Lease => Volatile.Read(ref _lease);
    internal JobStopReason StopReason => (JobStopReason)Volatile.Read(ref _reason);
    internal bool OwnershipLost => Volatile.Read(ref _ownershipLost) != 0;
    internal bool Exited => Volatile.Read(ref _exited) != 0;

    internal void ObserveShutdown(CancellationToken shutdown) =>
        _shutdownSignal = shutdown.Register(() => RequestStop(JobStopReason.Shutdown));

    internal void Update(JobAttemptLease renewed)
    {
        var previous = Lease;
        if (renewed.Scope != previous.Scope || renewed.JobId != previous.JobId || renewed.AttemptId != previous.AttemptId ||
            renewed.Owner != previous.Owner || renewed.Fence != previous.Fence || renewed.AttemptNumber != previous.AttemptNumber ||
            renewed.ImplementationIdentity != previous.ImplementationIdentity || renewed.Revision < previous.Revision ||
            renewed.GrantedAt != previous.GrantedAt || renewed.Deadline != previous.Deadline)
            throw new JobException("jobs.lease.invalid", "The Jobs authority returned a conflicting renewal identity.");
        Volatile.Write(ref _lease, renewed);
    }

    internal void RequestStop(JobStopReason reason)
    {
        if (reason == JobStopReason.OwnershipLost) Interlocked.Exchange(ref _ownershipLost, 1);
        Interlocked.CompareExchange(ref _reason, (int)reason, (int)JobStopReason.None);
        // User cancellation callbacks must not interrupt fencing/receipt handling or block a renewal loop.
        _ = HandlerCancellation.CancelAsync().ContinueWith(task => _ = task.Exception,
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    internal void MarkExited() => Interlocked.Exchange(ref _exited, 1);

    public void Dispose()
    {
        _shutdownSignal.Dispose();
        HandlerCancellation.Dispose();
        // A handler may retain its reporter after exit. Leave this managed gate valid so a late
        // call can fail the Exited check instead of racing disposal with an in-flight report.
    }
}

internal sealed class JobProgressReporter(JobAttemptSession session, IServiceScopeFactory scopes,
    JobRuntimeSettings settings, IReadOnlyList<IJobDataPolicy>? dataPolicies = null,
    Func<JobExecutionContext>? invocationContext = null) : IJobProgressReporter
{
    public async ValueTask ReportAsync(JobProgress progress, CancellationToken cancellationToken = default)
    {
        if (session.Exited || session.OwnershipLost)
            throw new JobException("jobs.progress.lease-lost", "This physical attempt no longer owns progress reporting.");
        ValidateData(progress);
        JobValidation.Progress(progress, settings);
        if (!await session.Reports.WaitAsync(0, cancellationToken))
            throw new JobException("jobs.progress.busy", "Only one progress report may be in flight for an attempt.");
        try
        {
            await session.Commands.WaitAsync(cancellationToken);
            try
            {
                if (session.Exited || session.OwnershipLost)
                    throw new JobException("jobs.progress.lease-lost", "This physical attempt no longer owns progress reporting.");
                ValidateData(progress);
                await using var scope = scopes.CreateAsyncScope();
                // Observational progress must not consume the renewal slack or build an unbounded
                // waiter backlog in front of the heartbeat. At most one report is in flight.
                var timeoutTicks = Math.Min(settings.RenewalInterval.Ticks / 2,
                    (session.Lease.Deadline - session.Lease.GrantedAt).Ticks / 6);
                var duration = TimeSpan.FromTicks(Math.Max(TimeSpan.FromMilliseconds(1).Ticks,
                    Math.Min(timeoutTicks, TimeSpan.FromMilliseconds(250).Ticks)));
                using var timeout = new CancellationTokenSource(duration, scope.ServiceProvider.GetRequiredService<TimeProvider>());
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                var result = await JobAuthority.Commands(scope.ServiceProvider).RecordProgressAsync(session.Lease, progress, linked.Token);
                if (result.Status is JobCommandStatus.LeaseLost or JobCommandStatus.Terminal)
                {
                    session.RequestStop(JobStopReason.OwnershipLost);
                    throw new JobException("jobs.progress.lease-lost", "This physical attempt no longer owns progress reporting.");
                }
                if (result.Status is not (JobCommandStatus.Applied or JobCommandStatus.Replayed))
                    throw new JobException("jobs.progress.unavailable", "The Jobs authority did not confirm progress.");
            }
            finally { session.Commands.Release(); }
        }
        finally { session.Reports.Release(); }
    }

    private void ValidateData(JobProgress progress)
    {
        if (dataPolicies is not { Count: > 0 }) return;
        var context = invocationContext?.Invoke() ?? throw new JobDataPolicyException();
        foreach (var policy in dataPolicies) policy.ValidateProgress(context, progress);
    }
}
