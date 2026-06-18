using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Elsa.Foundation.Agent.Abstractions.Contracts;
using Elsa.Foundation.Agent.Abstractions.Models;
using static Elsa.Foundation.Agent.Abstractions.Services.AgentIds;

namespace Elsa.Foundation.Agent.Abstractions.Services;

public sealed class DefaultAgentPolicyEvaluator : IAgentPolicyEvaluator
{
    public ValueTask<AgentPolicyDecision> EvaluateAvailabilityAsync(AgentPolicy policy, CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<AgentViolation> violations = policy.Enabled
            ? []
            : [new AgentViolation("agent.disabled", $"Agent policy '{policy.Id}' disables assistant features.")];

        return ValueTask.FromResult(new AgentPolicyDecision(policy.Enabled, violations));
    }

    public ValueTask<AgentPolicyDecision> EvaluateContextAsync(AgentPolicy policy, IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default)
    {
        var violations = new List<AgentViolation>();
        var allowedKinds = policy.AllowedContextKinds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var attachment in attachments)
        {
            if (!IsAllowed(allowedKinds, attachment))
                violations.Add(new("agent.context.kind_denied", $"Context '{attachment.Source}/{attachment.ContentType}' is not allowed by policy '{policy.Id}'.", attachment.Id));

            if (GetEffectiveSensitivity(attachment.Sensitivity) > policy.MaxContextSensitivity)
                violations.Add(new("agent.context.sensitivity_denied", $"Context attachment '{attachment.Label}' exceeds policy sensitivity '{policy.MaxContextSensitivity}'.", attachment.Id));
        }

        return ValueTask.FromResult(new AgentPolicyDecision(violations.Count == 0, violations));
    }

    public ValueTask<AgentPolicyDecision> EvaluateCapabilityAsync(AgentPolicy policy, string capabilityId, CancellationToken cancellationToken = default)
    {
        var denied = policy.DeniedCapabilityIds.Contains(capabilityId, StringComparer.OrdinalIgnoreCase);
        IReadOnlyCollection<AgentViolation> violations = denied
            ? [new AgentViolation("agent.capability.denied", $"Capability '{capabilityId}' is denied by policy '{policy.Id}'.", capabilityId)]
            : [];

        return ValueTask.FromResult(new AgentPolicyDecision(!denied, violations));
    }

    private static bool IsAllowed(ISet<string> allowedKinds, AgentContextAttachment attachment)
    {
        var normalizedContentType = attachment.ContentType.Replace("-", ".", StringComparison.OrdinalIgnoreCase);
        return allowedKinds.Contains(attachment.Source) ||
               allowedKinds.Contains(attachment.ContentType) ||
               allowedKinds.Contains(normalizedContentType) ||
               allowedKinds.Contains($"{attachment.Source}.{normalizedContentType}");
    }

    private static AgentContextSensitivity GetEffectiveSensitivity(AgentContextSensitivity sensitivity)
        => sensitivity == AgentContextSensitivity.SecretRedacted ? AgentContextSensitivity.Sensitive : sensitivity;
}

public sealed class DefaultAgentContextSanitizer : IAgentContextSanitizer
{
    public ValueTask<IReadOnlyCollection<AgentContextAttachment>> SanitizeAsync(IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default)
    {
        var sanitized = attachments.Select(attachment =>
        {
            if (attachment.Sensitivity != AgentContextSensitivity.Secret)
                return attachment;

            return attachment with
            {
                Sensitivity = AgentContextSensitivity.SecretRedacted,
                Content = null,
                Summary = "Secret context was redacted before provider use.",
                References = attachment.References.ToDictionary(x => x.Key, _ => "[redacted]")
            };
        }).ToList();

        return ValueTask.FromResult<IReadOnlyCollection<AgentContextAttachment>>(sanitized);
    }
}

public sealed class DefaultAgentContextCollector(
    IEnumerable<IAgentContextProvider> providers,
    IAgentContextSanitizer sanitizer,
    IAgentPolicyEvaluator policyEvaluator,
    IAgentAuditSink auditSink) : IAgentContextCollector
{
    public async Task<AgentResult<IReadOnlyCollection<AgentContextAttachment>>> CollectAsync(AgentPolicy policy, AgentContextRequest request, CancellationToken cancellationToken = default)
    {
        var provider = providers.FirstOrDefault(x => string.Equals(x.ScopeKind, request.ScopeKind, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
            return AgentResult<IReadOnlyCollection<AgentContextAttachment>>.Failure("agent.context.provider_missing", $"No context provider is registered for scope '{request.ScopeKind}'.", 404);

        var availability = await policyEvaluator.EvaluateAvailabilityAsync(policy, cancellationToken);
        if (!availability.Allowed)
            return AgentResult<IReadOnlyCollection<AgentContextAttachment>>.Failure("agent.disabled", string.Join(" ", availability.Violations.Select(x => x.Message)), 403);

        var attachments = await sanitizer.SanitizeAsync(await provider.CollectAsync(request, cancellationToken), cancellationToken);
        var decision = await policyEvaluator.EvaluateContextAsync(policy, attachments, cancellationToken);
        if (decision.Allowed)
        {
            await auditSink.EmitAsync(new(
                NewId(),
                AgentAuditEventKind.ContextCollected,
                request.SessionId,
                null,
                "Agent context collected after policy and redaction.",
                DateTimeOffset.UtcNow,
                attachments.ToDictionary(x => x.Id, x => $"{x.Source}/{x.ContentType}:{x.Sensitivity}")), cancellationToken);

            return AgentResult<IReadOnlyCollection<AgentContextAttachment>>.Success(attachments);
        }

        await auditSink.EmitAsync(new(
            NewId(),
            AgentAuditEventKind.ContextDenied,
            request.SessionId,
            null,
            "Agent context collection denied by policy.",
            DateTimeOffset.UtcNow,
            decision.Violations.Select((x, i) => new { Key = $"{i}:{x.Code}", x.Message }).ToDictionary(x => x.Key, x => x.Message)), cancellationToken);

        return AgentResult<IReadOnlyCollection<AgentContextAttachment>>.Failure("agent.context.denied", string.Join(" ", decision.Violations.Select(x => x.Message)), 400);
    }
}

public sealed class DefaultAgentCapabilityCatalog(IEnumerable<IAgentCapabilityProvider> providers) : IAgentCapabilityCatalog
{
    public async Task<IReadOnlyCollection<AgentCapability>> ListAsync(CancellationToken cancellationToken = default)
    {
        var capabilities = new List<AgentCapability>();
        foreach (var provider in providers)
            capabilities.AddRange(await provider.GetCapabilitiesAsync(cancellationToken));

        return capabilities
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed class InMemoryAgentSessionService(IAgentAuditSink auditSink) : IAgentSessionService
{
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<AgentMessage>> _messages = new(StringComparer.OrdinalIgnoreCase);

    public async Task<AgentSession> CreateAsync(AgentSessionCreateRequest request, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new AgentSession(
            NewId(),
            request.Title,
            request.TenantId,
            request.ConversationId,
            request.ProviderId,
            request.Mode,
            request.Policy ?? AgentPolicy.Default,
            AgentSessionStatus.Active,
            now,
            now,
            now.AddHours(8),
            request.Metadata);

        _sessions[session.Id] = session;
        _messages[session.Id] = [];

        await auditSink.EmitAsync(new(
            NewId(),
            AgentAuditEventKind.SessionCreated,
            session.Id,
            null,
            "Agent session created.",
            now,
            new Dictionary<string, string> { ["providerId"] = session.ProviderId }), cancellationToken);

        return session;
    }

    public Task<AgentSession?> FindAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return Task.FromResult(session);
    }

    public async Task<AgentMessage> AddMessageAsync(string sessionId, AgentMessageCreateRequest request, CancellationToken cancellationToken = default)
    {
        if (!_sessions.ContainsKey(sessionId))
            throw new InvalidOperationException($"Agent session '{sessionId}' was not found.");

        var message = new AgentMessage(
            NewId(),
            sessionId,
            request.Role,
            request.Content,
            request.Status,
            request.CapabilityId,
            DateTimeOffset.UtcNow,
            null,
            null,
            request.ContextAttachmentIds,
            [],
            request.Context);
        var messages = _messages.GetOrAdd(sessionId, _ => []);

        lock (messages)
            messages.Add(message);

        await auditSink.EmitAsync(new(
            NewId(),
            AgentAuditEventKind.MessageAccepted,
            sessionId,
            null,
            "Agent message accepted.",
            message.CreatedAt,
            new Dictionary<string, string> { ["messageId"] = message.Id, ["role"] = message.Role.ToString() }), cancellationToken);

        return message;
    }
}

public sealed class InMemoryAgentProposalService(
    IAgentActionProposalExecutor executor,
    IAgentAuditSink auditSink) : IAgentProposalService
{
    private readonly ConcurrentDictionary<string, AgentActionProposal> _proposals = new(StringComparer.OrdinalIgnoreCase);

    public async Task<AgentActionProposal> AddAsync(AgentActionProposal proposal, CancellationToken cancellationToken = default)
    {
        _proposals[proposal.Id] = proposal;
        await auditSink.EmitAsync(new(
            NewId(),
            AgentAuditEventKind.ProposalCreated,
            proposal.SessionId,
            null,
            "Agent proposal created.",
            proposal.CreatedAt,
            new Dictionary<string, string> { ["proposalId"] = proposal.Id, ["capabilityId"] = proposal.CapabilityId }), cancellationToken);

        return proposal;
    }

    public Task<AgentActionProposal?> FindAsync(string proposalId, CancellationToken cancellationToken = default)
    {
        _proposals.TryGetValue(proposalId, out var proposal);
        return Task.FromResult(proposal);
    }

    public async Task<AgentResult<AgentActionProposal>> ApproveAsync(string proposalId, string actorId, string? expectedRevision = null, string? comment = null, CancellationToken cancellationToken = default)
    {
        if (!_proposals.TryGetValue(proposalId, out var proposal))
            return AgentResult<AgentActionProposal>.Failure("agent.proposal.not_found", $"Proposal '{proposalId}' was not found.", 404);

        if (proposal.Status is AgentActionProposalStatus.Denied or AgentActionProposalStatus.Executed)
            return AgentResult<AgentActionProposal>.Failure("agent.proposal.closed", $"Proposal '{proposalId}' is already {proposal.Status}.", 409);

        if (!RevisionMatches(proposal, expectedRevision))
            return AgentResult<AgentActionProposal>.Failure("agent.proposal.revision_conflict", $"Proposal '{proposalId}' targets revision '{proposal.BaseRevision}', not requested revision '{expectedRevision}'.", 409);

        var now = DateTimeOffset.UtcNow;
        var approved = proposal with { Status = AgentActionProposalStatus.Approved, ApprovedBy = actorId, ApprovedAt = now, UpdatedAt = now };
        _proposals[proposalId] = approved;

        await auditSink.EmitAsync(new(
            NewId(),
            AgentAuditEventKind.ProposalApproved,
            proposal.SessionId,
            actorId,
            "Agent proposal approved.",
            now,
            new Dictionary<string, string> { ["proposalId"] = proposal.Id, ["comment"] = comment ?? string.Empty }), cancellationToken);

        return AgentResult<AgentActionProposal>.Success(approved);
    }

    public async Task<AgentResult<AgentActionProposal>> DenyAsync(string proposalId, string actorId, string? reason, CancellationToken cancellationToken = default)
    {
        if (!_proposals.TryGetValue(proposalId, out var proposal))
            return AgentResult<AgentActionProposal>.Failure("agent.proposal.not_found", $"Proposal '{proposalId}' was not found.", 404);

        var now = DateTimeOffset.UtcNow;
        var denied = proposal with { Status = AgentActionProposalStatus.Denied, UpdatedAt = now };
        _proposals[proposalId] = denied;

        await auditSink.EmitAsync(new(
            NewId(),
            AgentAuditEventKind.ProposalDenied,
            proposal.SessionId,
            actorId,
            "Agent proposal denied.",
            now,
            new Dictionary<string, string> { ["proposalId"] = proposal.Id, ["reason"] = reason ?? string.Empty }), cancellationToken);

        return AgentResult<AgentActionProposal>.Success(denied);
    }

    public async Task<AgentResult<AgentProposalExecutionResult>> ExecuteAsync(string proposalId, string actorId, string? expectedRevision = null, CancellationToken cancellationToken = default)
    {
        if (!_proposals.TryGetValue(proposalId, out var proposal))
            return AgentResult<AgentProposalExecutionResult>.Failure("agent.proposal.not_found", $"Proposal '{proposalId}' was not found.", 404);

        if (proposal.RequiresApproval && proposal.Status != AgentActionProposalStatus.Approved)
            return AgentResult<AgentProposalExecutionResult>.Failure("agent.proposal.approval_required", $"Proposal '{proposalId}' requires approval before execution.", 409);

        if (!RevisionMatches(proposal, expectedRevision))
            return AgentResult<AgentProposalExecutionResult>.Failure("agent.proposal.revision_conflict", $"Proposal '{proposalId}' targets revision '{proposal.BaseRevision}', not requested revision '{expectedRevision}'.", 409);

        var result = await executor.ExecuteAsync(proposal, cancellationToken);
        if (!result.Succeeded)
            return result;

        var now = DateTimeOffset.UtcNow;
        _proposals[proposalId] = proposal with { Status = AgentActionProposalStatus.Executed, UpdatedAt = now };

        await auditSink.EmitAsync(new(
            NewId(),
            AgentAuditEventKind.ProposalExecuted,
            proposal.SessionId,
            actorId,
            "Agent proposal executed.",
            now,
            new Dictionary<string, string> { ["proposalId"] = proposal.Id }), cancellationToken);

        return result;
    }

    private static bool RevisionMatches(AgentActionProposal proposal, string? expectedRevision)
        => string.IsNullOrWhiteSpace(expectedRevision) || string.Equals(proposal.BaseRevision, expectedRevision, StringComparison.Ordinal);
}

public sealed class NoopAgentActionProposalExecutor : IAgentActionProposalExecutor
{
    public Task<AgentResult<AgentProposalExecutionResult>> ExecuteAsync(AgentActionProposal proposal, CancellationToken cancellationToken = default)
        => Task.FromResult(AgentResult<AgentProposalExecutionResult>.Success(new(
            proposal.Id,
            true,
            proposal.ResourceType ?? "workflow-definition",
            proposal.ResourceId ?? proposal.Id,
            "Proposal execution seam accepted the proposal; no concrete executor is registered.")));
}

public sealed class DefaultAgentStreamingService(
    IAgentSessionService sessions,
    IAgentProviderRegistry providers) : IAgentStreamingService
{
    public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(string sessionId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var session = await sessions.FindAsync(sessionId, cancellationToken);
        if (session is null)
        {
            yield return Error("agent.session.not_found", $"Agent session '{sessionId}' was not found.", 404);
            yield break;
        }

        var provider = providers.Find(session.ProviderId);
        if (provider is null)
        {
            yield return Error("agent.provider.not_found", $"Agent provider '{session.ProviderId}' is not registered.", 404);
            yield break;
        }

        await foreach (var item in provider.SendMessageAsync(new(session.Id, string.Empty, []), cancellationToken))
            yield return item;
    }

    private static AgentStreamEvent Error(string code, string message, int statusCode)
        => new(NewId(), AgentStreamEventKind.Error, null, null, new(code, message, statusCode), DateTimeOffset.UtcNow);
}

public sealed class InMemoryAgentFeedbackService(IAgentAuditSink auditSink) : IAgentFeedbackService
{
    private readonly ConcurrentDictionary<string, AgentFeedback> _feedback = new(StringComparer.OrdinalIgnoreCase);

    public async Task<AgentFeedback> AddAsync(AgentFeedback feedback, CancellationToken cancellationToken = default)
    {
        _feedback[feedback.Id] = feedback;
        await auditSink.EmitAsync(new(
            NewId(),
            AgentAuditEventKind.FeedbackReceived,
            feedback.SessionId,
            feedback.ActorId,
            "Agent feedback received.",
            feedback.CreatedAt,
            new Dictionary<string, string> { ["feedbackId"] = feedback.Id, ["messageId"] = feedback.MessageId ?? string.Empty, ["rating"] = feedback.Rating }), cancellationToken);

        return feedback;
    }
}

public sealed class InMemoryAgentAuditStore : IAgentAuditSink, IAgentAuditReader
{
    private readonly List<AgentAuditEvent> _events = [];

    public Task EmitAsync(AgentAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        lock (_events)
            _events.Add(auditEvent);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<AgentAuditEvent>> ListAsync(string? sessionId = null, int? take = null, CancellationToken cancellationToken = default)
    {
        lock (_events)
        {
            IEnumerable<AgentAuditEvent> query = _events
                .Where(x => sessionId is null || string.Equals(x.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.CreatedAt);

            if (take is > 0)
                query = query.Take(take.Value);

            return Task.FromResult<IReadOnlyCollection<AgentAuditEvent>>(query.ToList());
        }
    }
}

public sealed class DefaultAgentProviderRegistry(IEnumerable<IAgentProvider> providers) : IAgentProviderRegistry
{
    public IReadOnlyCollection<IAgentProvider> Providers { get; } = providers.ToList();

    public IAgentProvider? Find(string providerId)
        => Providers.FirstOrDefault(x => string.Equals(x.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
}

internal static class AgentIds
{
    public static string NewId() => Guid.NewGuid().ToString("N");
}
