using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Core.Contracts;

public interface IAgentSessionService
{
    Task<AgentSession> CreateAsync(AgentSessionCreateRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<AgentSession?> FindAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<AgentMessage> AddMessageAsync(string sessionId, AgentMessageCreateRequest request, CancellationToken cancellationToken = default);

    Task<AgentMessage?> UpdateMessageAsync(string sessionId, string messageId, AgentMessageStatus status, AgentError? error = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<AgentMessage>> ListMessagesAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<AgentMessage?> FindMessageAsync(string sessionId, string messageId, CancellationToken cancellationToken = default);

    Task<AgentMessage?> FindLatestMessageAsync(string sessionId, AgentRole? role = null, CancellationToken cancellationToken = default);

    Task AddContextAsync(string sessionId, IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<AgentContextAttachment>> ListContextAsync(string sessionId, CancellationToken cancellationToken = default);
}

public interface IAgentPolicyEvaluator
{
    ValueTask<AgentPolicyDecision> EvaluateAvailabilityAsync(AgentPolicy policy, CancellationToken cancellationToken = default);

    ValueTask<AgentPolicyDecision> EvaluateContextAsync(AgentPolicy policy, IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default);

    ValueTask<AgentPolicyDecision> EvaluateCapabilityAsync(AgentPolicy policy, string capabilityId, CancellationToken cancellationToken = default);
}

public interface IAgentContextSanitizer
{
    ValueTask<IReadOnlyCollection<AgentContextAttachment>> SanitizeAsync(IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default);
}

public interface IAgentContextProvider
{
    string ScopeKind { get; }

    ValueTask<IReadOnlyCollection<AgentContextAttachment>> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default);
}

public interface IAgentContextCollector
{
    Task<AgentResult<IReadOnlyCollection<AgentContextAttachment>>> CollectAsync(AgentPolicy policy, AgentContextRequest request, CancellationToken cancellationToken = default);
}

public interface IAgentCapabilityProvider
{
    ValueTask<IReadOnlyCollection<AgentCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
}

public interface IAgentCapabilityCatalog
{
    Task<IReadOnlyCollection<AgentCapability>> ListAsync(CancellationToken cancellationToken = default);
}

public interface IAgentActionProposalExecutor
{
    Task<AgentResult<AgentProposalExecutionResult>> ExecuteAsync(AgentActionProposal proposal, CancellationToken cancellationToken = default);
}

public interface IAgentProposalService
{
    Task<AgentActionProposal> AddAsync(AgentActionProposal proposal, CancellationToken cancellationToken = default);

    Task<AgentActionProposal?> FindAsync(string proposalId, CancellationToken cancellationToken = default);

    Task<AgentResult<AgentActionProposal>> ApproveAsync(string proposalId, string actorId, string? expectedRevision = null, string? comment = null, CancellationToken cancellationToken = default);

    Task<AgentResult<AgentActionProposal>> DenyAsync(string proposalId, string actorId, string? reason, CancellationToken cancellationToken = default);

    Task<AgentResult<AgentProposalExecutionResult>> ExecuteAsync(string proposalId, string actorId, string? expectedRevision = null, CancellationToken cancellationToken = default);
}

public interface IAgentStreamingService
{
    IAsyncEnumerable<AgentStreamEvent> StreamAsync(string sessionId, CancellationToken cancellationToken = default);
}

public interface IAgentFeedbackService
{
    Task<AgentFeedback> AddAsync(AgentFeedback feedback, CancellationToken cancellationToken = default);
}

public interface IAgentAuditSink
{
    Task EmitAsync(AgentAuditEvent auditEvent, CancellationToken cancellationToken = default);
}

public interface IAgentAuditReader
{
    Task<IReadOnlyCollection<AgentAuditEvent>> ListAsync(string? sessionId = null, int? take = null, CancellationToken cancellationToken = default);
}

public interface IAgentProvider
{
    string ProviderId { get; }

    Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default);

    IAsyncEnumerable<AgentStreamEvent> SendMessageAsync(AgentProviderMessage message, CancellationToken cancellationToken = default);

    Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default);

    Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default);
}

public interface IAgentProviderRegistry
{
    IReadOnlyCollection<IAgentProvider> Providers { get; }

    IAgentProvider? Find(string providerId);
}
