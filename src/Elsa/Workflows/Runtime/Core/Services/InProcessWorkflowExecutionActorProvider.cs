using System.Collections.Concurrent;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InProcessWorkflowExecutionActorProvider : IWorkflowExecutionActorProvider
{
    // In-process diagnostic/routing label only; not persisted across runs (the actor subsystem is in-memory and
    // descriptors are transient), so the nameof value tracks the type name without wire-compatibility risk.
    public const string ProviderName = nameof(InProcessWorkflowExecutionActorProvider);
    public const int DefaultMaxProcessedIdempotencyKeysPerAgent = 4096;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _lifecycleLocks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, InProcessWorkflowExecutionActor> _agents = new(StringComparer.Ordinal);
    private readonly IWorkflowExecutionCommandExecutor _commandProcessor;
    private readonly int _maxProcessedIdempotencyKeysPerAgent;
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
    {
        ArgumentNullException.ThrowIfNull(commandProcessor);

        if (maxProcessedIdempotencyKeysPerAgent <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxProcessedIdempotencyKeysPerAgent), "The idempotency key cache size must be greater than zero.");

        _commandProcessor = commandProcessor;
        _maxProcessedIdempotencyKeysPerAgent = maxProcessedIdempotencyKeysPerAgent;
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

        var lifecycleLock = GetLifecycleLock(request.WorkflowExecutionId);

        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            var agent = _agents.GetOrAdd(request.WorkflowExecutionId, workflowExecutionId =>
            {
                var activationId = Interlocked.Increment(ref _activationCounter);
                return new InProcessWorkflowExecutionActor(workflowExecutionId, activationId, _commandProcessor, _maxProcessedIdempotencyKeysPerAgent);
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

        var lifecycleLock = GetLifecycleLock(request.WorkflowExecutionId);

        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (!_agents.TryGetValue(request.WorkflowExecutionId, out var agent))
                return;

            await agent.PassivateAsync(request, cancellationToken);
            _agents.TryRemove(KeyValuePair.Create(request.WorkflowExecutionId, agent));
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    private SemaphoreSlim GetLifecycleLock(string workflowExecutionId) =>
        _lifecycleLocks.GetOrAdd(workflowExecutionId, _ => new SemaphoreSlim(1, 1));

    private sealed class InProcessWorkflowExecutionActor : IWorkflowExecutionActor
    {
        private readonly SemaphoreSlim _mailbox = new(1, 1);
        private readonly HashSet<string> _processedIdempotencyKeys = new(StringComparer.Ordinal);
        private readonly Queue<string> _processedIdempotencyKeyOrder = new();
        private readonly IWorkflowExecutionCommandExecutor _commandProcessor;
        private readonly string _workflowExecutionId;
        private readonly string _agentId;
        private readonly DateTimeOffset _activatedAt;
        private readonly object _statusLock = new();
        private readonly int _maxProcessedIdempotencyKeys;
        private WorkflowExecutionActorStatus _status = WorkflowExecutionActorStatus.Active;

        public InProcessWorkflowExecutionActor(string workflowExecutionId, long activationId, IWorkflowExecutionCommandExecutor commandProcessor, int maxProcessedIdempotencyKeys)
        {
            _workflowExecutionId = workflowExecutionId;
            _agentId = $"inprocess:{workflowExecutionId}:{activationId}";
            _activatedAt = DateTimeOffset.UtcNow;
            _commandProcessor = commandProcessor;
            _maxProcessedIdempotencyKeys = maxProcessedIdempotencyKeys;
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

            await _mailbox.WaitAsync(cancellationToken);
            try
            {
                if (!string.Equals(envelope.WorkflowExecutionId, _workflowExecutionId, StringComparison.Ordinal))
                    return DispatchResult(envelope, WorkflowExecutionCommandDispatchStatus.Rejected, "Envelope workflow execution ID does not match this agent.");

                if (Status != WorkflowExecutionActorStatus.Active)
                    return DispatchResult(envelope, WorkflowExecutionCommandDispatchStatus.Deferred, "In-process workflow execution agent is passivated.");

                if (_processedIdempotencyKeys.Contains(envelope.IdempotencyKey))
                    return DispatchResult(envelope, WorkflowExecutionCommandDispatchStatus.Duplicate, "Idempotency key was already processed.");

                var processResult = await _commandProcessor.ProcessAsync(envelope, options, cancellationToken);
                RememberProcessedIdempotencyKey(envelope.IdempotencyKey);

                if (processResult.IsFaulted)
                {
                    var faultMetadata = new Dictionary<string, string>(StringComparer.Ordinal);
                    if (processResult.StopReason is { } stopReason)
                        faultMetadata["runtime.dispatch.drainStopReason"] = stopReason.ToString();
                    if (processResult.OutboxDeliveryFailed)
                        faultMetadata["runtime.dispatch.outboxDeliveryFailed"] = "true";

                    return DispatchResult(
                        envelope,
                        WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted,
                        processResult.FaultReason ?? "Workflow execution faulted during command processing.",
                        faultMetadata);
                }

                return DispatchResult(envelope, WorkflowExecutionCommandDispatchStatus.Accepted);
            }
            finally
            {
                _mailbox.Release();
            }
        }

        public async ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken)
        {
            if (!string.Equals(request.WorkflowExecutionId, _workflowExecutionId, StringComparison.Ordinal))
                return;

            SetStatus(WorkflowExecutionActorStatus.Passivating);

            try
            {
                await _mailbox.WaitAsync(cancellationToken);
            }
            catch
            {
                SetStatus(WorkflowExecutionActorStatus.Active);
                throw;
            }

            try
            {
                SetStatus(WorkflowExecutionActorStatus.Passivated);
            }
            finally
            {
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
}
