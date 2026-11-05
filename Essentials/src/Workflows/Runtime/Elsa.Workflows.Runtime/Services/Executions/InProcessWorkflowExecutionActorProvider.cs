using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Executions;

public sealed class InProcessWorkflowExecutionActorProvider : IWorkflowExecutionActorProvider, IDisposable
{
    internal DrainControlContext? DrainControls { private get; init; }
    internal ILogger? Logger { private get; init; }

    // Core and distributed composition must use the same wiring, regardless of registration order.
    internal static InProcessWorkflowExecutionActorProvider Create(IServiceProvider services) =>
        new(services.GetRequiredService<IWorkflowExecutionCommandExecutor>(), services.GetRequiredService<RuntimeActorEvictionOptions>())
        {
            DrainControls = services.GetService<DrainControlContext>(),
            Logger = services.GetService<ILogger<InProcessWorkflowExecutionActorProvider>>()
        };

    // In-process diagnostic/routing label only; not persisted across runs (the actor subsystem is in-memory and
    // descriptors are transient), so the nameof value tracks the type name without wire-compatibility risk.
    public const string ProviderName = nameof(InProcessWorkflowExecutionActorProvider);
    public const int DefaultMaxProcessedIdempotencyKeysPerAgent = 4096;

    /// <summary>
    /// Dispatch-result metadata key set to <c>"true"</c> when a command's drain committed a terminal workflow status.
    /// Kept low-blast-radius (a metadata entry, like the existing fault metadata) rather than a new result field, and
    /// read by the self-passivating handle to trigger terminal eviction (#542).
    /// </summary>
    public const string WorkflowTerminatedMetadataKey = "runtime.dispatch.workflowTerminated";

    /// <summary>Runtime self-instrumentation meter name (the runtime's first meter).</summary>
    public const string MeterName = "Elsa.Workflows.Runtime";

    /// <summary>Observable gauge reporting the number of live in-process mailboxes currently registered.</summary>
    public const string LiveMailboxGaugeName = "elsa.runtime.actor.live_mailboxes";

    private const string TerminalEvictionReason = "runtime.actor.terminal-eviction";
    /// <summary>The <c>source</c> metadata of the scheduler command that re-drives a drain whose yield was not consumed.</summary>
    internal const string YieldRedriveSource = "runtime.actor.yield-redrive";

    private readonly ConcurrentDictionary<ActorKey, SemaphoreSlim> _lifecycleLocks = new();
    private readonly ConcurrentDictionary<ActorKey, SelfPassivatingActorHandle> _agents = new();
    private readonly IWorkflowExecutionCommandExecutor _commandProcessor;
    private readonly int _maxProcessedIdempotencyKeysPerAgent;
    private readonly RuntimeActorEvictionOptions _evictionOptions;
    private readonly Meter _meter;
    private readonly bool _ownsMeter;
    private readonly CancellationTokenSource _disposed = new();
    private long _activationCounter;

    public InProcessWorkflowExecutionActorProvider()
        : this(NoopWorkflowExecutionCommandExecutor.Instance)
    {
    }

    public InProcessWorkflowExecutionActorProvider(IWorkflowExecutionCommandExecutor commandProcessor)
        : this(commandProcessor, DefaultMaxProcessedIdempotencyKeysPerAgent)
    {
    }

    public InProcessWorkflowExecutionActorProvider(IWorkflowExecutionCommandExecutor commandProcessor, int maxProcessedIdempotencyKeysPerAgent)
        : this(commandProcessor, maxProcessedIdempotencyKeysPerAgent, new RuntimeActorEvictionOptions())
    {
    }

    public InProcessWorkflowExecutionActorProvider(IWorkflowExecutionCommandExecutor commandProcessor, RuntimeActorEvictionOptions evictionOptions)
        : this(commandProcessor, DefaultMaxProcessedIdempotencyKeysPerAgent, evictionOptions)
    {
    }

    public InProcessWorkflowExecutionActorProvider(
        IWorkflowExecutionCommandExecutor commandProcessor,
        int maxProcessedIdempotencyKeysPerAgent,
        RuntimeActorEvictionOptions evictionOptions,
        Meter? meter = null)
    {
        ArgumentNullException.ThrowIfNull(commandProcessor);
        ArgumentNullException.ThrowIfNull(evictionOptions);

        if (maxProcessedIdempotencyKeysPerAgent <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxProcessedIdempotencyKeysPerAgent), "The idempotency key cache size must be greater than zero.");

        _commandProcessor = commandProcessor;
        _maxProcessedIdempotencyKeysPerAgent = maxProcessedIdempotencyKeysPerAgent;
        _evictionOptions = evictionOptions;

        // Accept a caller-supplied meter (tests pass a uniquely-named one for isolation); otherwise own one named after
        // the runtime. The live-mailbox gauge observes the registry size on demand — zero cost until a listener attaches.
        _ownsMeter = meter is null;
        _meter = meter ?? new Meter(MeterName);
        _meter.CreateObservableGauge(
            LiveMailboxGaugeName,
            () => _agents.Count,
            unit: "{mailbox}",
            description: "Live in-process workflow execution actor mailboxes currently registered.");
    }

    public WorkflowExecutionActorCapabilities Capabilities =>
        WorkflowExecutionActorCapabilities.InProcessMailbox |
        WorkflowExecutionActorCapabilities.Passivation;

    public async ValueTask<IWorkflowExecutionActor> GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var unsupportedCapabilities = request.RequiredCapabilities & ~Capabilities;
        if (unsupportedCapabilities != WorkflowExecutionActorCapabilities.None)
            throw new NotSupportedException($"The in-process workflow execution agent provider does not support required capabilities: {unsupportedCapabilities}.");

        var actorKey = ActorKey.From(request);
        var lifecycleLock = await AcquireLifecycleLockAsync(actorKey, cancellationToken);
        try
        {
            // The handle is cached per key (one per live mailbox), so repeated activations for the same execution return
            // the same instance — the single-mailbox-per-execution contract is unchanged. The handle delegates dispatch
            // to the inner actor and, gated by RuntimeActorEvictionOptions, self-passivates after a terminal drain.
            var agent = _agents.GetOrAdd(actorKey, key =>
            {
                var activationId = Interlocked.Increment(ref _activationCounter);
                var actor = new InProcessWorkflowExecutionActor(
                    key.WorkflowExecutionId,
                    request.Partition,
                    activationId,
                    _commandProcessor,
                    _maxProcessedIdempotencyKeysPerAgent,
                    DrainControls);
                var handle = new SelfPassivatingActorHandle(actor, this, _evictionOptions);
                actor.StrandedYield = () => RedriveStrandedYield(handle, key.WorkflowExecutionId, request.Partition);
                return handle;
            });

            return agent;
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    public async ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actorKey = ActorKey.From(request);
        var lifecycleLock = await AcquireLifecycleLockAsync(actorKey, cancellationToken);
        try
        {
            if (_agents.TryGetValue(actorKey, out var agent))
            {
                await agent.PassivateInnerAsync(request, cancellationToken);
                _agents.TryRemove(KeyValuePair.Create(actorKey, agent));
            }

            // Drop the per-execution lifecycle lock so the dictionary does not grow with every distinct workflow
            // execution ever activated. We remove the exact instance we currently hold (TryRemove on the key/value
            // pair), which is the canonical lock for this id — AcquireLifecycleLockAsync guarantees the holder always
            // owns the instance still registered in the dictionary.
            //
            // The removed SemaphoreSlim is deliberately NOT disposed: this type only ever calls WaitAsync/Release and
            // never touches AvailableWaitHandle, so no unmanaged wait handle is ever allocated and there is nothing to
            // release — dropping the reference lets the GC reclaim it. Disposing would instead risk
            // ObjectDisposedException for any activation still parked on this instance (the classic
            // dispose-out-from-under-a-waiter race), so we rely on the release-and-retry chain in
            // AcquireLifecycleLockAsync rather than explicit disposal.
            _lifecycleLocks.TryRemove(KeyValuePair.Create(actorKey, lifecycleLock));
        }
        finally
        {
            // A concurrent activation for the same id may already be parked on this instance; releasing (rather than
            // disposing) lets it wake, observe that the instance is no longer canonical, and retry onto the fresh lock.
            lifecycleLock.Release();
        }
    }

    public void Dispose()
    {
        // Stops any background yield re-drive; the resumption sweep owns that work after shutdown.
        if (!_disposed.IsCancellationRequested)
            _disposed.Cancel();
        if (_ownsMeter)
            _meter.Dispose();
    }

    // Acquires the lifecycle lock for an execution id and returns it already held (WaitAsync completed).
    //
    // The lock serializes activation and passivation for a single execution id. Passivation removes an id's lock from
    // the dictionary while still holding it, so a caller that resolved the lock via GetOrAdd just before the removal
    // could otherwise end up holding an orphaned instance while a later caller installs a fresh one — two callers
    // serialized on different semaphores, breaking mutual exclusion. To prevent that we re-check identity after
    // acquiring: only the instance that is still the registered (canonical) lock for the id is accepted; a stale
    // instance is released and the acquisition retried so every caller rendezvous on the single current lock. The
    // holder of the canonical lock is the only party that can remove it, so it cannot be pulled out mid-critical-section.
    private async ValueTask<SemaphoreSlim> AcquireLifecycleLockAsync(ActorKey actorKey, CancellationToken cancellationToken)
    {
        while (true)
        {
            var lifecycleLock = _lifecycleLocks.GetOrAdd(actorKey, _ => new SemaphoreSlim(1, 1));
            await lifecycleLock.WaitAsync(cancellationToken);

            if (_lifecycleLocks.TryGetValue(actorKey, out var current) && ReferenceEquals(current, lifecycleLock))
                return lifecycleLock;

            // The instance we acquired was removed by a concurrent passivation (and possibly replaced). Release it —
            // which also wakes any other waiters parked on the same stale instance so they retry too — and loop.
            lifecycleLock.Release();
        }
    }

    // A drain that yielded to a Cancel which then never ran (its waiter was cancelled, or it answered Duplicate) would
    // otherwise wait for the resumption sweep (#3647). Re-drive it through the handle, so terminal eviction still applies,
    // on a detached flow so no caller's ambient session or drain control reaches the new turn. Its drain delivers the
    // continuations the yielded segment flushed undelivered before it starts (WorkflowDrainOrchestrator).
    private void RedriveStrandedYield(SelfPassivatingActorHandle handle, string workflowExecutionId, WorkflowExecutionPartition partition)
    {
        var now = DateTimeOffset.UtcNow;
        var envelopeId = RuntimeExecutionIdGenerator.NewId(now);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["source"] = YieldRedriveSource };
        var envelope = new WorkflowExecutionCommandEnvelope(
            envelopeId: envelopeId,
            workflowExecutionId: workflowExecutionId,
            command: new WorkflowExecutionCommand(
                CommandId: RuntimeExecutionIdGenerator.NewId(now),
                WorkflowExecutionId: workflowExecutionId,
                Kind: WorkflowExecutionCommandKind.RunSchedulerWork,
                EnqueuedAt: now,
                Payload: null,
                Metadata: metadata),
            idempotencyKey: $"{YieldRedriveSource}:{workflowExecutionId}:{envelopeId}",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: now,
            metadata: metadata,
            partition: partition);

        if (_disposed.IsCancellationRequested)
            return;
        // UnsafeQueueUserWorkItem flows no ExecutionContext, whether or not the caller already suppressed flow.
        ThreadPool.UnsafeQueueUserWorkItem(static state => _ = state.Provider.RunRedriveAsync(state.Handle, state.Envelope),
            (Provider: this, Handle: handle, Envelope: envelope), preferLocal: false);
    }

    private async Task RunRedriveAsync(SelfPassivatingActorHandle handle, WorkflowExecutionCommandEnvelope envelope)
    {
        try
        {
            var result = await handle.EnqueueAsync(envelope, _disposed.Token);
            if (result.Status != WorkflowExecutionCommandDispatchStatus.Accepted)
                Logger?.LogDebug("Re-driving workflow execution '{WorkflowExecutionId}' after an unconsumed drain yield answered {Status}; the resumption sweep remains its backstop",
                    envelope.WorkflowExecutionId, result.Status);
        }
        catch (OperationCanceledException) when (_disposed.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            // The resumption sweep remains the backstop for the yielded work.
            Logger?.LogWarning(exception, "Re-driving workflow execution '{WorkflowExecutionId}' after an unconsumed drain yield failed", envelope.WorkflowExecutionId);
        }
    }

    // Wraps the inner mailbox and centralizes terminal-eviction policy so all six GetAgentAsync call sites stay
    // unchanged. Dispatch delegates to the inner actor; when a completed drain reports a terminal workflow status
    // (surfaced as WorkflowTerminatedMetadataKey on the dispatch result) and PassivateOnTerminal is set, the handle
    // passivates the execution AFTER the inner EnqueueAsync has released the mailbox — never inside the critical
    // section — riding AcquireLifecycleLockAsync's canonical recheck. That keeps single-writer-per-execution intact:
    // a concurrent redelivery either serializes behind the still-open mailbox and then blocks the passivation until it
    // releases, or arrives after eviction and re-activates a fresh mailbox (bounded activate/passivate churn, which the
    // resumption sweep's terminal purge stops from recurring).
    private sealed class SelfPassivatingActorHandle(
        InProcessWorkflowExecutionActor inner,
        InProcessWorkflowExecutionActorProvider provider,
        RuntimeActorEvictionOptions evictionOptions) : IWorkflowExecutionActor
    {
        public WorkflowExecutionActorDescriptor Descriptor => inner.Descriptor;

        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default) =>
            DispatchThenMaybeEvictAsync(envelope, inner.EnqueueAsync(envelope, cancellationToken));

        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(
            WorkflowExecutionCommandEnvelope envelope,
            WorkflowExecutionCommandDispatchOptions options,
            CancellationToken cancellationToken = default) =>
            DispatchThenMaybeEvictAsync(envelope, inner.EnqueueAsync(envelope, options, cancellationToken));

        internal ValueTask PassivateInnerAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken) =>
            inner.PassivateAsync(request, cancellationToken);

        private async ValueTask<WorkflowExecutionCommandDispatchResult> DispatchThenMaybeEvictAsync(
            WorkflowExecutionCommandEnvelope envelope,
            ValueTask<WorkflowExecutionCommandDispatchResult> dispatch)
        {
            var result = await dispatch;

            if (evictionOptions.PassivateOnTerminal && IsTerminal(result))
            {
                // Passivate outside the mailbox critical section (the inner EnqueueAsync's finally already released it).
                // Use CancellationToken.None: terminal eviction is pure registry cleanup — if the caller's token were
                // cancelled here the mailbox would linger until the resumption sweep reaper collects it, so we complete
                // it unconditionally instead.
                await provider.PassivateAsync(
                    new WorkflowExecutionActorPassivationRequest(
                        workflowExecutionId: envelope.WorkflowExecutionId,
                        boundary: WorkflowExecutionActorPassivationBoundary.AfterCheckpointCommit,
                        requestedAt: DateTimeOffset.UtcNow,
                        reason: TerminalEvictionReason,
                        partition: envelope.Partition),
                    CancellationToken.None);
            }

            return result;
        }

        private static bool IsTerminal(WorkflowExecutionCommandDispatchResult result) =>
            result.Metadata.TryGetValue(WorkflowTerminatedMetadataKey, out var value) &&
            string.Equals(value, "true", StringComparison.Ordinal);
    }

    private sealed class InProcessWorkflowExecutionActor : IWorkflowExecutionActor
    {
        private readonly DrainControlContext? _drainControls;
        private readonly DrainControl _drainControl = new();
        private readonly SemaphoreSlim _mailbox = new(1, 1);
        private readonly HashSet<string> _processedIdempotencyKeys = new(StringComparer.Ordinal);
        private readonly Queue<string> _processedIdempotencyKeyOrder = new();
        private readonly IWorkflowExecutionCommandExecutor _commandProcessor;
        private readonly string _workflowExecutionId;
        private readonly WorkflowExecutionPartition _partition;
        private readonly string _agentId;
        private readonly DateTimeOffset _activatedAt;
        private readonly object _statusLock = new();
        private readonly int _maxProcessedIdempotencyKeys;
        private WorkflowExecutionActorStatus _status = WorkflowExecutionActorStatus.Active;
        // 1 while the last drain turn yielded and no Cancel has run since.
        private int _strandedYield;

        /// <summary>Re-drives this execution after a yield whose Cancel never ran. Set by the provider.</summary>
        internal Action? StrandedYield { private get; set; }

        public InProcessWorkflowExecutionActor(
            string workflowExecutionId,
            WorkflowExecutionPartition partition,
            long activationId,
            IWorkflowExecutionCommandExecutor commandProcessor,
            int maxProcessedIdempotencyKeys,
            DrainControlContext? drainControls)
        {
            _workflowExecutionId = workflowExecutionId;
            _partition = partition;
            _agentId = $"inprocess:{workflowExecutionId}:{activationId}";
            _activatedAt = DateTimeOffset.UtcNow;
            _commandProcessor = commandProcessor;
            _maxProcessedIdempotencyKeys = maxProcessedIdempotencyKeys;
            _drainControls = drainControls;
        }

        public WorkflowExecutionActorDescriptor Descriptor => new(
            workflowExecutionId: _workflowExecutionId,
            agentId: _agentId,
            providerName: ProviderName,
            status: Status,
            capabilities: WorkflowExecutionActorCapabilities.InProcessMailbox | WorkflowExecutionActorCapabilities.Passivation,
            activatedAt: _activatedAt);

        private WorkflowExecutionActorStatus Status
        {
            get
            {
                lock (_statusLock)
                    return _status;
            }
        }

        public async ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            return await EnqueueAsync(envelope, WorkflowExecutionCommandDispatchOptions.Default, cancellationToken);
        }

        public async ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(
            WorkflowExecutionCommandEnvelope envelope,
            WorkflowExecutionCommandDispatchOptions options,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(envelope);
            ArgumentNullException.ThrowIfNull(options);

            cancellationToken.ThrowIfCancellationRequested();
            // Publish only a valid mailbox-local control wait, before waiting for its active turn. A cancelled
            // waiter removes its request; the running turn may safely have yielded in the meantime.
            // The registration is held through the Cancel's own turn, so a drain that yielded to it is settled before the
            // registration drops: either the Cancel ran, or the turn ends without it and the drain is re-driven.
            // A Cancel registers while the actor passivates too. If the passivation completes, the Cancel answers Deferred
            // and its check returns, since the actor is no longer Active. If it gives up, the Cancel's own signal keeps
            // the drain yielding, so the Cancel runs on the restored actor rather than waiting behind a drain that goes on.
            using var pendingControl = _drainControls is not null &&
                envelope.Command.Kind == WorkflowExecutionCommandKind.Cancel &&
                envelope.WorkflowExecutionId == _workflowExecutionId && envelope.Partition == _partition &&
                Status != WorkflowExecutionActorStatus.Passivated ? _drainControl.Wait() : null;
            try
            {
                await _mailbox.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                pendingControl?.Dispose();
                RedriveIfYieldStranded();
                throw;
            }
            try
            {
                // A mailbox grant can win the wait's cancellation race. Do not admit cancelled work.
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.Equals(envelope.WorkflowExecutionId, _workflowExecutionId, StringComparison.Ordinal))
                    return DispatchResult(envelope, WorkflowExecutionCommandDispatchStatus.Rejected, "Envelope workflow execution ID does not match this agent.");

                if (envelope.Partition != _partition)
                    return DispatchResult(envelope, WorkflowExecutionCommandDispatchStatus.Rejected, "Envelope persistence scope does not match this agent.");

                if (Status != WorkflowExecutionActorStatus.Active)
                    return DispatchResult(envelope, WorkflowExecutionCommandDispatchStatus.Deferred, "In-process workflow execution agent is passivated.");

                if (_processedIdempotencyKeys.Contains(envelope.IdempotencyKey))
                    return DispatchResult(envelope, WorkflowExecutionCommandDispatchStatus.Duplicate, "Idempotency key was already processed.");

                // A Cancel turn must consume its queued cancellation even when another Cancel waits, including
                // a retry with the same key that will subsequently return Duplicate without draining.
                using var controlScope = _drainControls?.Push(_workflowExecutionId,
                    envelope.Command.Kind == WorkflowExecutionCommandKind.Cancel ? null : _drainControl);
                var processResult = await _commandProcessor.ProcessAsync(envelope, options, cancellationToken);
                // A Cancel turn never yields (its control is null), so completing one settles any earlier yield. Any other
                // turn leaves it as it was, even one whose drain ran: that drain's session reads only its own overlay, so
                // it did not run the yielded segment's flushed continuations, and only the re-drive delivers them.
                if (processResult.StopReason == RuntimeSchedulerDrainStopReason.Yielded)
                    Interlocked.Exchange(ref _strandedYield, 1);
                else if (envelope.Command.Kind == WorkflowExecutionCommandKind.Cancel)
                    Interlocked.Exchange(ref _strandedYield, 0);

                // Admission shed (RB1, #1235). Deferred is the truthful status either way, but the two refusals differ
                // in what they left behind, and so in what the idempotency key must mean afterwards.
                //
                // A shed START wrote nothing, so its key is NOT consumed: the caller is told to retry (the HTTP edge
                // renders it 429), and a retry that answered Duplicate would strand a workflow that never ran. A shed
                // ALTERATION is refused the same start-shaped way (#1325) and for the same reason must keep its key,
                // which is load-bearing rather than incidental: the alteration pump re-claims the job once its lease
                // lapses and redelivers the SAME deterministic key, so a consumed key would answer Duplicate until
                // this agent is passivated or the key ages out of the bounded per-agent window, with no other stimulus
                // able to rescue the job in the meantime.
                //
                // A kind whose work item WAS durably queued before the refusal has its key consumed. That work is
                // waiting for the resumption sweep; an at-least-once redelivery of the same key — which the
                // distributed placement pump performs by design on every Deferred result — would otherwise queue the
                // same work a second time and run it twice once the first copy is consumed.
                //
                // Which case applies is never inferred from the kind here: it is read off ShedWorkQueued, so the
                // router stays the single owner of the refusal shape and this block cannot drift from it.
                if (processResult.Shed)
                {
                    if (processResult.ShedWorkQueued)
                        RememberProcessedIdempotencyKey(envelope.IdempotencyKey);

                    var shedMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [RuntimeMetadataKeys.DispatchShed] = "true"
                    };
                    if (processResult.RetryAfter is { } retryAfter)
                    {
                        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
                        shedMetadata[RuntimeMetadataKeys.DispatchRetryAfterSeconds] = seconds.ToString(CultureInfo.InvariantCulture);
                    }

                    return DispatchResult(
                        envelope,
                        WorkflowExecutionCommandDispatchStatus.Deferred,
                        processResult.FaultReason ?? "Runtime dispatch admission is at capacity.",
                        shedMetadata);
                }

                RememberProcessedIdempotencyKey(envelope.IdempotencyKey);

                if (processResult.IsFaulted)
                {
                    var faultMetadata = new Dictionary<string, string>(StringComparer.Ordinal);
                    if (processResult.StopReason is { } stopReason)
                        faultMetadata["runtime.dispatch.drainStopReason"] = stopReason.ToString();
                    if (processResult.OutboxDeliveryFailed)
                        faultMetadata["runtime.dispatch.outboxDeliveryFailed"] = "true";
                    if (processResult.WorkflowTerminated)
                        faultMetadata[WorkflowTerminatedMetadataKey] = "true";
                    if (processResult.CheckpointRuleViolation)
                        faultMetadata[RuntimeMetadataKeys.DispatchCheckpointRuleViolation] = "true";

                    return DispatchResult(
                        envelope,
                        WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted,
                        processResult.FaultReason ?? "Workflow execution faulted during command processing.",
                        faultMetadata);
                }

                // Surface the terminal fact on the accepted result so the self-passivating handle can evict the mailbox
                // after the critical section releases. Accepted results carry no reason but may carry metadata.
                var terminalMetadata = processResult.WorkflowTerminated
                    ? new Dictionary<string, string>(StringComparer.Ordinal) { [WorkflowTerminatedMetadataKey] = "true" }
                    : null;

                return DispatchResult(envelope, WorkflowExecutionCommandDispatchStatus.Accepted, metadata: terminalMetadata);
            }
            finally
            {
                pendingControl?.Dispose();
                _mailbox.Release();
                RedriveIfYieldStranded();
            }
        }

        // The yielding turn sets the flag before it checks for waiters, and a leaving waiter unregisters before it
        // checks the flag. Both are full fences, so whichever runs last sees both, and the exchange lets only one re-drive.
        // Only a Cancel that ran clears it otherwise, so a turn that takes the mailbox before a leaving waiter checks
        // cannot hide the yield from that check, and that turn's own check finds it too. A passivation that gives up
        // unregisters before it sets Active, and a leaving waiter unregisters before it reads the status. Both take the
        // status lock, so if the waiter reads Passivating the passivation's later check sees the waiter gone, and if it
        // reads Active its own check sees the passivation gone. A completed passivation never sets
        // Active again, so nothing re-drives on its actor; the resumption sweep does.
        private void RedriveIfYieldStranded()
        {
            if (_drainControl.IsPending || Status != WorkflowExecutionActorStatus.Active)
                return;
            if (Interlocked.Exchange(ref _strandedYield, 0) == 1)
                StrandedYield?.Invoke();
        }

        public async ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken)
        {
            if (!string.Equals(request.WorkflowExecutionId, _workflowExecutionId, StringComparison.Ordinal))
                return;

            // A running drain yields to the passivation at its next step, as it does to a waiting Cancel, so a drain that
            // keeps making progress still ends its turn and a hand-off releases placement while it holds the lease (#3678).
            // The yielded segment flushes its continuations durably and undelivered. This actor is no longer Active, so it
            // does not re-drive them: the resumption sweep delivers them and re-drives the execution, on its next owner.
            var pendingHandoff = _drainControls is null ? null : _drainControl.Wait();
            SetStatus(WorkflowExecutionActorStatus.Passivating);

            try
            {
                await _mailbox.WaitAsync(cancellationToken);
            }
            catch
            {
                // A passivation that gives up keeps the actor, so a drain that yielded to it is re-driven like one that
                // yielded to a withdrawn Cancel. A Cancel still waiting keeps the drain yielding and runs instead.
                pendingHandoff?.Dispose();
                SetStatus(WorkflowExecutionActorStatus.Active);
                RedriveIfYieldStranded();
                throw;
            }

            try
            {
                SetStatus(WorkflowExecutionActorStatus.Passivated);
            }
            finally
            {
                pendingHandoff?.Dispose();
                _mailbox.Release();
            }
        }

        private void SetStatus(WorkflowExecutionActorStatus status)
        {
            lock (_statusLock)
                _status = status;
        }

        private void RememberProcessedIdempotencyKey(string idempotencyKey)
        {
            _processedIdempotencyKeys.Add(idempotencyKey);
            _processedIdempotencyKeyOrder.Enqueue(idempotencyKey);

            while (_processedIdempotencyKeyOrder.Count > _maxProcessedIdempotencyKeys)
                _processedIdempotencyKeys.Remove(_processedIdempotencyKeyOrder.Dequeue());
        }

        private static WorkflowExecutionCommandDispatchResult DispatchResult(
            WorkflowExecutionCommandEnvelope envelope,
            WorkflowExecutionCommandDispatchStatus status,
            string? reason = null,
            IReadOnlyDictionary<string, string>? metadata = null) =>
            new(
                envelopeId: envelope.EnvelopeId,
                workflowExecutionId: envelope.WorkflowExecutionId,
                status: status,
                recordedAt: DateTimeOffset.UtcNow,
                reason: reason,
                metadata: metadata);
    }

    private readonly record struct ActorKey(string Partition, string WorkflowExecutionId)
    {
        public static ActorKey From(WorkflowExecutionActorActivationRequest request) =>
            new(request.Partition.Value, request.WorkflowExecutionId);

        public static ActorKey From(WorkflowExecutionActorPassivationRequest request) =>
            new(request.Partition.Value, request.WorkflowExecutionId);
    }
}
