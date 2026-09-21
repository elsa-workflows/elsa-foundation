using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Core.Contracts;

public interface IAgentSessionService
{
    Task<AgentSession> CreateAsync(AgentSessionCreateRequest request, CancellationToken cancellationToken = default);

    Task<AgentSession?> FindAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<AgentMessage> AddMessageAsync(string sessionId, AgentMessageCreateRequest request, CancellationToken cancellationToken = default);

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

/// <summary>
/// The accumulated state handed to a provider for one step of a turn. The orchestrator owns the loop,
/// history, and tool execution; the provider maps this context to its SDK and yields the next step's
/// message deltas and tool-call requests.
/// </summary>
public sealed record AgentTurnContext(
    string SessionId,
    string ProviderSessionId,
    IReadOnlyList<AgentTurnMessage> History,
    IReadOnlyList<AgentToolResult> PendingToolResults,
    IReadOnlyCollection<AgentToolDescriptor> AvailableTools,
    IReadOnlyCollection<AgentContextAttachment> Context,
    int StepIndex,
    int MaxSteps)
{
    /// <summary>The most recent user message in the turn history, if any.</summary>
    public AgentTurnMessage? LatestUserMessage => History.LastOrDefault(x => x.Role == AgentRole.User);

    /// <summary>Convenience factory for a single-step, single-user-message turn (tests and simple callers).</summary>
    public static AgentTurnContext ForMessage(string sessionId, string content, IReadOnlyCollection<AgentContextAttachment> context)
        => new(sessionId, sessionId, [new AgentTurnMessage(AgentRole.User, content)], [], [], context, 0, 1);
}

public interface IAgentProvider
{
    string ProviderId { get; }

    Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Produces the next step of a turn. Yields <see cref="AgentStreamEventKind.MessageDelta"/> events for
    /// assistant text and <see cref="AgentStreamEventKind.ToolCallRequested"/> events for tools the provider
    /// wants run. The orchestrator executes any requested tools and calls this again with their results.
    /// </summary>
    IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, CancellationToken cancellationToken = default);

    Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default);

    Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default);
}

public interface IAgentProviderRegistry
{
    /// <summary>The single active agent provider, or null when no harness is enabled.</summary>
    IAgentProvider? Active { get; }
}
