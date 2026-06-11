using System.Collections.Concurrent;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InProcessWorkflowExecutionAgentProvider : IWorkflowExecutionAgentProvider
{
    public const string ProviderName = nameof(InProcessWorkflowExecutionAgentProvider);
    public const int DefaultMaxProcessedIdempotencyKeysPerAgent = 4096;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _lifecycleLocks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, InProcessWorkflowExecutionAgent> _agents = new(StringComparer.Ordinal);
    private readonly IWorkflowExecutionCommandProcessor _commandProcessor;
    private readonly int _maxProcessedIdempotencyKeysPerAgent;
    private long _activationCounter;

    public InProcessWorkflowExecutionAgentProvider()
        : this(NoopWorkflowExecutionCommandProcessor.Instance)
    {
    }

    public InProcessWorkflowExecutionAgentProvider(IWorkflowExecutionCommandProcessor commandProcessor)
        : this(commandProcessor, DefaultMaxProcessedIdempotencyKeysPerAgent)
    {
    }

    public InProcessWorkflowExecutionAgentProvider(IWorkflowExecutionCommandProcessor commandProcessor, int maxProcessedIdempotencyKeysPerAgent)
    {
        ArgumentNullException.ThrowIfNull(commandProcessor);

        if (maxProcessedIdempotencyKeysPerAgent <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxProcessedIdempotencyKeysPerAgent), "The idempotency key cache size must be greater than zero.");

        _commandProcessor = commandProcessor;
        _maxProcessedIdempotencyKeysPerAgent = maxProcessedIdempotencyKeysPerAgent;
    }

    public WorkflowExecutionAgentCapabilities Capabilities =>
        WorkflowExecutionAgentCapabilities.InProcessMailbox |
        WorkflowExecutionAgentCapabilities.Passivation;

    public async ValueTask<IWorkflowExecutionAgent> GetAgentAsync(WorkflowExecutionAgentActivationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var unsupportedCapabilities = request.RequiredCapabilities & ~Capabilities;
        if (unsupportedCapabilities != WorkflowExecutionAgentCapabilities.None)
            throw new NotSupportedException($"The in-process workflow execution agent provider does not support required capabilities: {unsupportedCapabilities}.");

        var lifecycleLock = GetLifecycleLock(request.WorkflowExecutionId);

        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            var agent = _agents.GetOrAdd(request.WorkflowExecutionId, workflowExecutionId =>
            {
                var activationId = Interlocked.Increment(ref _activationCounter);
                return new InProcessWorkflowExecutionAgent(workflowExecutionId, activationId, _commandProcessor, _maxProcessedIdempotencyKeysPerAgent);
            });

            return agent;
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    public async ValueTask PassivateAsync(WorkflowExecutionAgentPassivationRequest request, CancellationToken cancellationToken = default)
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

    private sealed class InProcessWorkflowExecutionAgent : IWorkflowExecutionAgent
    {
        private readonly SemaphoreSlim _mailbox = new(1, 1);
        private readonly HashSet<string> _processedIdempotencyKeys = new(StringComparer.Ordinal);
        private readonly Queue<string> _processedIdempotencyKeyOrder = new();
        private readonly IWorkflowExecutionCommandProcessor _commandProcessor;
        private readonly string _workflowExecutionId;
        private readonly string _agentId;
        private readonly DateTimeOffset _activatedAt;
        private readonly object _statusLock = new();
        private readonly int _maxProcessedIdempotencyKeys;
        private WorkflowExecutionAgentStatus _status = WorkflowExecutionAgentStatus.Active;

        public InProcessWorkflowExecutionAgent(string workflowExecutionId, long activationId, IWorkflowExecutionCommandProcessor commandProcessor, int maxProcessedIdempotencyKeys)
        {
            _workflowExecutionId = workflowExecutionId;
            _agentId = $"inprocess:{workflowExecutionId}:{activationId}";
            _activatedAt = DateTimeOffset.UtcNow;
            _commandProcessor = commandProcessor;
            _maxProcessedIdempotencyKeys = maxProcessedIdempotencyKeys;
        }

        public WorkflowExecutionAgentDescriptor Descriptor => new(
            workflowExecutionId: _workflowExecutionId,
            agentId: _agentId,
            providerName: ProviderName,
            status: Status,
            capabilities: WorkflowExecutionAgentCapabilities.InProcessMailbox | WorkflowExecutionAgentCapabilities.Passivation,
            activatedAt: _activatedAt);

        private WorkflowExecutionAgentStatus Status
        {
            get
            {
                lock (_statusLock)
                    return _status;
            }
        }

        public async ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(envelope);

            await _mailbox.WaitAsync(cancellationToken);
            try
            {
                if (!string.Equals(envelope.WorkflowExecutionId, _workflowExecutionId, StringComparison.Ordinal))
                    return DispatchResult(envelope, WorkflowExecutionCommandDispatchStatus.Rejected, "Envelope workflow execution ID does not match this agent.");

                if (Status != WorkflowExecutionAgentStatus.Active)
                    return DispatchResult(envelope, WorkflowExecutionCommandDispatchStatus.Deferred, "In-process workflow execution agent is passivated.");

                if (_processedIdempotencyKeys.Contains(envelope.IdempotencyKey))
                    return DispatchResult(envelope, WorkflowExecutionCommandDispatchStatus.Duplicate, "Idempotency key was already processed.");

                await _commandProcessor.ProcessAsync(envelope, cancellationToken);
                RememberProcessedIdempotencyKey(envelope.IdempotencyKey);

                return DispatchResult(envelope, WorkflowExecutionCommandDispatchStatus.Accepted);
            }
            finally
            {
                _mailbox.Release();
            }
        }

        public async ValueTask PassivateAsync(WorkflowExecutionAgentPassivationRequest request, CancellationToken cancellationToken)
        {
            if (!string.Equals(request.WorkflowExecutionId, _workflowExecutionId, StringComparison.Ordinal))
                return;

            SetStatus(WorkflowExecutionAgentStatus.Passivating);

            try
            {
                await _mailbox.WaitAsync(cancellationToken);
            }
            catch
            {
                SetStatus(WorkflowExecutionAgentStatus.Active);
                throw;
            }

            try
            {
                SetStatus(WorkflowExecutionAgentStatus.Passivated);
            }
            finally
            {
                _mailbox.Release();
            }
        }

        private void SetStatus(WorkflowExecutionAgentStatus status)
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
            string? reason = null) =>
            new(
                envelopeId: envelope.EnvelopeId,
                workflowExecutionId: envelope.WorkflowExecutionId,
                status: status,
                recordedAt: DateTimeOffset.UtcNow,
                reason: reason);
    }
}
