using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Jobs.Core;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Jobs;

/// <summary>One host/shell lifecycle and bounded physical slots; no local queue, retry or result authority.</summary>
internal sealed class JobsWorker(IServiceScopeFactory scopes, JobRuntimeSettings settings, JobHandlerRegistry handlers,
    TimeProvider clock, ILogger<JobsWorker> logger, IHostApplicationLifetime? lifetime = null, JobSchedulePump? materializer = null)
    : IHostedService, IBackgroundTask, IDisposable
{
    private readonly object _lifecycle = new();
    private readonly CancellationTokenSource _stopClaims = new();
    private readonly CancellationTokenSource _stopHandlers = new();
    private readonly string _owner = "jobs-" + Guid.NewGuid().ToString("N");
    private Task? _running;
    private Task? _stopping;
    private long _nextScope = -1;

    public Task StartAsync(CancellationToken cancellationToken) => StartAsync(cancellationToken, true);
    Task IBackgroundTask.StartAsync(CancellationToken cancellationToken) => StartAsync(cancellationToken, false);

    private Task StartAsync(CancellationToken cancellationToken, bool waitForHost)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lifecycle)
        {
            if (_stopping is not null) throw new InvalidOperationException("A stopped Jobs worker cannot be restarted.");
            if (_running is not null) return Task.CompletedTask;
            if (handlers.Support.Count == 0) throw new JobException("jobs.handler.missing", "An enabled Jobs worker requires registered handlers.");
            _running = Task.Run(() => RunAsync(waitForHost));
        }
        return Task.CompletedTask;
    }

    Task ITask.ExecuteAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycle)
            return _running ?? throw new InvalidOperationException("The Jobs worker has not been started.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycle)
        {
            _stopping ??= DrainAsync();
            return _stopping.WaitAsync(cancellationToken);
        }
    }

    private async Task DrainAsync()
    {
        await _stopClaims.CancelAsync();
        var running = _running;
        if (running is null) return;
        var grace = Task.Delay(settings.ShutdownGracePeriod, clock, CancellationToken.None);
        if (await Task.WhenAny(running, grace) != running)
            await _stopHandlers.CancelAsync();
        // A cancellation-ignoring handler retains its slot/scope until physical exit. A caller may stop
        // waiting through its own token; that does not turn this task into evidence that the handler exited.
        await running;
    }

    private async Task RunAsync(bool waitForHost)
    {
        try
        {
            if (waitForHost && lifetime is not null)
            {
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using (lifetime.ApplicationStarted.Register(() => ready.TrySetResult()))
                    await ready.Task.WaitAsync(_stopClaims.Token);
            }
            await using (var scope = scopes.CreateAsyncScope())
            {
                _ = JobAuthority.Commands(scope.ServiceProvider);
                _ = JobAuthority.Queries(scope.ServiceProvider);
                _ = JobAuthority.Recurrence(scope.ServiceProvider);
            }
            // A synchronous provider/handler must not delay starting another physical slot or
            // the independent materializer. The number of host loops remains fixed and bounded.
            var pump = settings.MaterializeSchedules
                ? materializer ?? throw new JobException("jobs.materializer.missing", "Schedule materialization is enabled without its host component.")
                : null;
            var loops = Enumerable.Range(0, settings.MaximumConcurrentAttempts).Select(_ => Task.Run(RunSlotAsync)).ToList();
            if (pump is not null)
                loops.Add(Task.Run(() => pump.RunAsync(_stopClaims.Token)));
            await Task.WhenAll(loops);
        }
        catch (OperationCanceledException) when (_stopClaims.IsCancellationRequested) { }
    }

    private async Task RunSlotAsync()
    {
        JobClaimRequest? pending = null;
        while (!_stopClaims.IsCancellationRequested)
        {
            var progressed = false;
            try
            {
                if (pending is null)
                {
                    var scopeIndex = (int)((ulong)Interlocked.Increment(ref _nextScope) % (ulong)settings.WorkerScopes.Count);
                    pending = new(settings.WorkerScopes[scopeIndex], _owner, settings.Queues, handlers.Support,
                        settings.LeaseDuration, Guid.NewGuid().ToString("N"));
                }
                JobClaimResult claimed;
                await using (var claimScope = scopes.CreateAsyncScope())
                {
                    claimed = await JobAuthority.Commands(claimScope.ServiceProvider).ClaimAsync(pending, _stopClaims.Token);
                }
                if (claimed.Lease is { } lease && claimed.Status is JobCommandStatus.Applied or JobCommandStatus.Replayed)
                {
                    if (lease.Scope != pending.Scope || lease.Owner != _owner)
                        throw new JobException("jobs.lease.invalid", "The Jobs authority returned another worker's lease.");
                    pending = null;
                    progressed = true;
                    await ExecuteAttemptAsync(lease);
                }
                else if (claimed.Lease is null && claimed.Status is JobCommandStatus.NoEligibleWork or JobCommandStatus.Held or JobCommandStatus.CapacityUnavailable)
                    pending = null;
                // Unavailable/unknown outcomes retain this exact slot-local pull identity. A new
                // request could otherwise spend another permission after a lost commit response.
            }
            catch (OperationCanceledException) when (_stopClaims.IsCancellationRequested) { return; }
            catch (Exception)
            {
                logger.LogWarning("Jobs polling could not confirm progress; durable work remains with its authority.");
            }
            if (!progressed)
            {
                try { await Task.Delay(settings.PollInterval, clock, _stopClaims.Token); }
                catch (OperationCanceledException) when (_stopClaims.IsCancellationRequested) { return; }
            }
        }
    }

    private async Task ExecuteAttemptAsync(JobAttemptLease lease)
    {
        using var session = new JobAttemptSession(lease);
        session.ObserveShutdown(_stopHandlers.Token);
        JobPayload? payload = null;
        JobDiagnostic? diagnostic = null;
        var outcome = JobAttemptOutcome.Failed;
        var retryable = true;
        var physicalStarted = false;
        using var stopRenewing = new CancellationTokenSource();
        Task? renewing = null;
        try
        {
            await using var executionScope = scopes.CreateAsyncScope();
            JobSnapshot? snapshot;
            using (var timeout = new CancellationTokenSource(settings.LeaseDuration, clock))
                snapshot = await JobAuthority.Queries(executionScope.ServiceProvider).FindAsync(lease.Scope, lease.JobId, timeout.Token);
            if (snapshot is not null && (snapshot.Request is null || snapshot.UnavailableDiagnostic is not null))
            {
                // Unreadable retained bytes are not a business failure and cannot authorize a
                // fabricated completion. Preserve provider-owned responsibility without entry.
                logger.LogWarning("Jobs retained request is unavailable; physical execution was refused and responsibility remains with the authority.");
                return;
            }
            if (snapshot is null || snapshot.Scope != lease.Scope || snapshot.JobId != lease.JobId ||
                snapshot.Request is not { } request || snapshot.ActiveAttempt != lease ||
                !settings.Queues.Contains(request.Queue, StringComparer.Ordinal) ||
                snapshot.RequestSha256 != JobRequestDigest.Compute(request))
            {
                logger.LogWarning("Jobs snapshot identity did not prove the exact accepted invocation and lease; physical execution was refused.");
                return;
            }
            JobValidation.Request(request, settings.MaximumPayloadBytes);
            var registration = handlers.Resolve(request, lease.ImplementationIdentity);
            var dataPolicies = executionScope.ServiceProvider.GetServices<IJobDataPolicy>().ToArray();
            foreach (var policy in dataPolicies) policy.ValidateAdmission(lease.Scope, request);
            JobExecutionContext? invocationContext = null;
            var reporter = new JobProgressReporter(session, scopes, settings, dataPolicies,
                () => invocationContext ?? throw new JobDataPolicyException());
            invocationContext = new(lease.Scope, lease.JobId, lease.AttemptId, lease.AttemptNumber, InvocationKey(lease), reporter)
                { Handler = request.Handler };
            foreach (var policy in dataPolicies) policy.BeginInvocation(invocationContext, request);
            if (!await RenewAsync(session)) return;
            if (session.HandlerCancellation.IsCancellationRequested)
            {
                outcome = CancelledOutcome(session);
                diagnostic = StopDiagnostic(session);
            }
            else
            {
                await using (var commandScope = scopes.CreateAsyncScope())
                {
                    using var timeout = new CancellationTokenSource(settings.LeaseDuration, clock);
                    var started = await JobAuthority.Commands(commandScope.ServiceProvider).RecordPhysicalStartAsync(session.Lease, timeout.Token);
                    if (started.Status is not (JobCommandStatus.Applied or JobCommandStatus.Replayed))
                    {
                        session.RequestStop(JobStopReason.OwnershipLost);
                        return;
                    }
                }
                physicalStarted = true;
                renewing = KeepRenewingAsync(session, stopRenewing.Token);
                var result = await registration.InvokeAsync(executionScope.ServiceProvider, request.Input, invocationContext,
                    settings, session.HandlerCancellation.Token);
                foreach (var policy in dataPolicies) policy.ValidateResult(invocationContext, result);
                payload = result;
                outcome = JobAttemptOutcome.Succeeded;
                retryable = false;
            }
        }
        catch (OperationCanceledException) when (session.HandlerCancellation.IsCancellationRequested)
        {
            outcome = CancelledOutcome(session);
            diagnostic = StopDiagnostic(session);
        }
        catch (JobDataUnavailableException)
        {
            payload = null;
            diagnostic = new("jobs.data-policy.unavailable", "The Jobs data policy is temporarily unavailable.");
            retryable = true;
        }
        catch (JobDataPolicyException)
        {
            payload = null;
            diagnostic = new("jobs.data-policy.refused", "The Jobs data policy refused this operation.");
            retryable = false;
        }
        catch (JobPayloadException error)
        {
            diagnostic = new(error.Code, error.Message);
            retryable = false;
        }
        catch (Exception) when (!physicalStarted)
        {
            logger.LogWarning("Jobs could not validate retained invocation data before physical entry; responsibility remains with the authority.");
            return;
        }
        catch (Exception)
        {
            diagnostic = new("jobs.handler.failed", "The Jobs attempt failed; protected handler diagnostics remain host-owned.");
        }
        finally
        {
            session.MarkExited();
            await stopRenewing.CancelAsync();
            if (renewing is not null) await renewing;
        }
        var receipt = new JobPhysicalReceipt("receipt-" + Guid.NewGuid().ToString("N"), lease.AttemptId, lease.Owner,
            lease.Fence, outcome, payload, JobPayloadDigest.Compute(payload), diagnostic, retryable, clock.GetUtcNow());
        await SettleAsync(session, receipt);
    }

    private async Task<bool> RenewAsync(JobAttemptSession session)
    {
        await session.Commands.WaitAsync();
        try
        {
            var requestLease = session.Lease;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    // Two bounded attempts fit within a normally scheduled lease's renewal
                    // slack. Unknown success replays the same old token, never a guessed revision.
                    using var timeout = new CancellationTokenSource(settings.LeaseDuration / 4, clock);
                    var renewed = await JobAuthority.Commands(scope.ServiceProvider).RenewAsync(requestLease, settings.LeaseDuration, timeout.Token);
                    if (renewed.Status is JobCommandStatus.Applied or JobCommandStatus.Replayed && renewed.Lease is not null)
                    {
                        session.Update(renewed.Lease);
                        // Deadline decisions come from provider time; host wall time is not authority.
                        if (renewed.DeadlineReached) session.RequestStop(JobStopReason.TimedOut);
                        else if (renewed.CancellationRequested) session.RequestStop(JobStopReason.Cancelled);
                        return true;
                    }
                    if (renewed.DeadlineReached) session.RequestStop(JobStopReason.TimedOut);
                    else if (renewed.CancellationRequested) session.RequestStop(JobStopReason.Cancelled);
                    if (renewed.Status != JobCommandStatus.Unavailable) break;
                }
                catch (Exception) { /* Reconcile only with the original lease and request content. */ }
                if (attempt == 0)
                {
                    var retryDelay = settings.PollInterval < settings.RenewalInterval / 4
                        ? settings.PollInterval : settings.RenewalInterval / 4;
                    await Task.Delay(retryDelay, clock, CancellationToken.None);
                }
            }
            session.RequestStop(JobStopReason.OwnershipLost);
            return false;
        }
        catch (Exception)
        {
            session.RequestStop(JobStopReason.OwnershipLost);
            return false;
        }
        finally { session.Commands.Release(); }
    }

    private async Task KeepRenewingAsync(JobAttemptSession session, CancellationToken stopRenewing)
    {
        while (true)
        {
            // Both timestamps originate with the authority. A short absolute timeout needs a
            // correspondingly prompt provider check without trusting this host's wall clock.
            var timeoutCadence = TimeSpan.FromTicks(Math.Max(TimeSpan.FromMilliseconds(10).Ticks,
                (session.Lease.Deadline - session.Lease.GrantedAt).Ticks / 3));
            var cadence = session.StopReason == JobStopReason.None && timeoutCadence < settings.RenewalInterval
                ? timeoutCadence : settings.RenewalInterval;
            try { await Task.Delay(cadence, clock, stopRenewing); }
            catch (OperationCanceledException) when (stopRenewing.IsCancellationRequested) { return; }
            if (stopRenewing.IsCancellationRequested || !await RenewAsync(session)) return;
        }
    }

    private async Task SettleAsync(JobAttemptSession session, JobPhysicalReceipt receipt)
    {
        // Join a report already in flight before settlement; a report begun after physical exit
        // cannot pass the session's Exited check. No renewal can change the lease at this point.
        await session.Commands.WaitAsync();
        session.Commands.Release();
        if (!session.OwnershipLost)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    using var timeout = new CancellationTokenSource(settings.LeaseDuration, clock);
                    var result = await JobAuthority.Commands(scope.ServiceProvider).CompleteAsync(session.Lease, receipt, timeout.Token);
                    if (result.Status is JobCommandStatus.Applied or JobCommandStatus.Replayed) return;
                    if (result.Status != JobCommandStatus.Unavailable) break;
                    if (attempt < 2) await Task.Delay(settings.PollInterval, clock, CancellationToken.None);
                }
                catch (Exception)
                {
                    // Reconcile an uncertain acknowledgement with the same lease and receipt identity.
                    if (attempt < 2) await Task.Delay(settings.PollInterval, clock, CancellationToken.None);
                }
            }
        }
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                using var timeout = new CancellationTokenSource(settings.LeaseDuration, clock);
                var result = await JobAuthority.Commands(scope.ServiceProvider).RecordPhysicalReceiptAsync(session.Lease.Scope, session.Lease.JobId, receipt, timeout.Token);
                if (result.Status is JobCommandStatus.Applied or JobCommandStatus.Replayed) return;
                if (result.Status != JobCommandStatus.Unavailable) break;
            }
            catch (Exception) { /* Keep the same receipt identity while reconciling uncertainty. */ }
            if (attempt < 2) await Task.Delay(settings.PollInterval, clock, CancellationToken.None);
        }
        logger.LogWarning("Jobs could not confirm physical receipt persistence; no selected result or physical-stop claim was inferred.");
    }

    private static JobAttemptOutcome CancelledOutcome(JobAttemptSession session) => session.StopReason switch
    {
        JobStopReason.TimedOut => JobAttemptOutcome.TimedOut,
        JobStopReason.OwnershipLost or JobStopReason.Shutdown => JobAttemptOutcome.Abandoned,
        _ => JobAttemptOutcome.Cancelled
    };

    private static string InvocationKey(JobAttemptLease lease) => "elsa.jobs.invocation.v1:" +
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[]
        {
            lease.Scope.PersistencePartition, lease.Scope.TenantId, lease.JobId
        })));

    private static JobDiagnostic StopDiagnostic(JobAttemptSession session) => session.StopReason switch
    {
        JobStopReason.TimedOut => new("jobs.attempt.timeout", "The attempt reached its absolute provider deadline."),
        JobStopReason.Cancelled => new("jobs.attempt.cancelled", "The authority requested cooperative cancellation."),
        JobStopReason.OwnershipLost => new("jobs.attempt.ownership-lost", "The physical handler observed cancellation after authority was lost."),
        _ => new("jobs.worker.stopped", "The worker requested cooperative stopping after its drain interval.")
    };

    public void Dispose()
    {
        _stopClaims.Cancel();
        _stopHandlers.Cancel();
        // Active tasks own their scopes, cancellation sources and physical slots until they unwind.
    }
}
