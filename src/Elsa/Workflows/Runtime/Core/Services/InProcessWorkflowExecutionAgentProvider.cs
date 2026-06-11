using System.Collections.Concurrent;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InProcessWorkflowExecutionAgentProvider : IWorkflowExecutionAgentProvider
{
    public const string ProviderName = nameof(InProcessWorkflowExecutionAgentProvider);

    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly ConcurrentDictionary<string, InProcessWorkflowExecutionAgent> _agents = new(StringComparer.Ordinal);
    private readonly IWorkflowExecutionCommandProcessor _commandProcessor;
    private long _activationCounter;

    public InProcessWorkflowExecutionAgentProvider()
        : this(NoopWorkflowExecutionCommandProcessor.Instance)
    {
    }

    public InProcessWorkflowExecutionAgentProvider(IWorkflowExecutionCommandProcessor commandProcessor)
    {
        ArgumentNullException.ThrowIfNull(commandProcessor);

        _commandProcessor = commandProcessor;
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

        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            var agent = _agents.GetOrAdd(request.WorkflowExecutionId, workflowExecutionId =>
            {
                var activationId = Interlocked.Increment(ref _activationCounter);
                return new InProcessWorkflowExecutionAgent(workflowExecutionId, activationId, _commandProcessor);
            });

            return agent;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async ValueTask PassivateAsync(WorkflowExecutionAgentPassivationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (!_agents.TryGetValue(request.WorkflowExecutionId, out var agent))
                return;

            await agent.PassivateAsync(request, cancellationToken);
            _agents.TryRemove(KeyValuePair.Create(request.WorkflowExecutionId, agent));
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private sealed class InProcessWorkflowExecutionAgent : IWorkflowExecutionAgent
    {
        private readonly SemaphoreSlim _mailbox = new(1, 1);
        private readonly HashSet<string> _processedIdempotencyKeys = new(StringComparer.Ordinal);
        private readonly IWorkflowExecutionCommandProcessor _commandProcessor;
        private readonly string _workflowExecutionId;
        private readonly string _agentId;
        private readonly DateTimeOffset _activatedAt;
        private readonly object _statusLock = new();
        private WorkflowExecutionAgentStatus _status = WorkflowExecutionAgentStatus.Active;

        public InProcessWorkflowExecutionAgent(string workflowExecutionId, long activationId, IWorkflowExecutionCommandProcessor commandProcessor)
        {
            _workflowExecutionId = workflowExecutionId;
            _agentId = $"inprocess:{workflowExecutionId}:{activationId}";
            _activatedAt = DateTimeOffset.UtcNow;
            _commandProcessor = commandProcessor;
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
                _processedIdempotencyKeys.Add(envelope.IdempotencyKey);

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
