using System.Collections.Concurrent;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Primitives.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static Elsa.Agent.Services.AgentIds;

namespace Elsa.Agent.Services;

internal sealed class InMemoryAgentSessionService(IAgentAuditSink auditSink) : IAgentSessionService
{
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<AgentMessage>> _messages = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<AgentContextAttachment>> _contexts = new(StringComparer.Ordinal);

    public async Task<AgentSession> CreateAsync(AgentSessionCreateRequest request, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new AgentSession(
            NewId(),
            request.Title,
            request.TenantId,
            request.ActorId,
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
        _contexts[session.Id] = [];

        await auditSink.EmitAsync(new(
            NewId(),
            AgentAuditEventKind.SessionCreated,
            session.Id,
            session.ActorId,
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
        if (!_sessions.TryGetValue(sessionId, out var session))
            throw new InvalidOperationException($"Agent session '{sessionId}' was not found.");
        sessionId = session.Id;

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
            request.Context)
        {
            RequestId = request.RequestId,
            Source = request.Source,
            Client = request.Client,
            PromptMode = request.PromptMode,
            DeliveryFingerprint = request.DeliveryFingerprint
        };
        var messages = _messages.GetOrAdd(sessionId, _ => []);

        lock (messages)
        {
            if (request.Role == AgentRole.User && !string.IsNullOrWhiteSpace(request.RequestId))
            {
                var existing = messages.FirstOrDefault(x => x.Role == AgentRole.User && x.RequestId == request.RequestId);
                if (existing is not null)
                {
                    if (!string.IsNullOrEmpty(request.DeliveryFingerprint) && existing.DeliveryFingerprint == request.DeliveryFingerprint)
                        return existing;
                    throw new AgentContributionException("A prompt delivery identity was reused with different input.");
                }
            }
            if (request.Role == AgentRole.User && messages.Any(x => x.Role == AgentRole.User && x.Status is AgentMessageStatus.Pending or AgentMessageStatus.Streaming))
                throw new AgentContributionException("Another prompt remains pending in this session.");
            messages.Add(message);
        }

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

    public Task<AgentMessage?> FindLatestMessageAsync(string sessionId, AgentRole? role = null, CancellationToken cancellationToken = default)
    {
        if (!_messages.TryGetValue(sessionId, out var messages))
            return Task.FromResult<AgentMessage?>(null);

        lock (messages)
        {
            return Task.FromResult(messages.LastOrDefault(x => role is null || x.Role == role));
        }
    }

    public Task SetMessageStatusAsync(string sessionId, string messageId, AgentMessageStatus status, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_messages.TryGetValue(sessionId, out var messages))
            lock (messages)
            {
                var index = messages.FindIndex(x => x.Id == messageId);
                if (index >= 0)
                    messages[index] = messages[index] with { Status = status, CompletedAt = status is AgentMessageStatus.Completed or AgentMessageStatus.Failed or AgentMessageStatus.Cancelled ? DateTimeOffset.UtcNow : null };
            }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<AgentMessage>> ListMessagesAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (!_messages.TryGetValue(sessionId, out var messages))
            return Task.FromResult<IReadOnlyCollection<AgentMessage>>([]);

        lock (messages)
            return Task.FromResult<IReadOnlyCollection<AgentMessage>>(messages.ToList());
    }

    public Task<AgentMessage?> FindMessageAsync(string sessionId, string messageId, CancellationToken cancellationToken = default)
    {
        if (!_messages.TryGetValue(sessionId, out var messages))
            return Task.FromResult<AgentMessage?>(null);

        lock (messages)
            return Task.FromResult(messages.FirstOrDefault(x => string.Equals(x.Id, messageId, StringComparison.Ordinal)));
    }

    public Task AddContextAsync(string sessionId, IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default)
    {
        if (attachments.Count == 0)
            return Task.CompletedTask;

        if (!_sessions.ContainsKey(sessionId))
            throw new InvalidOperationException($"Agent session '{sessionId}' was not found.");

        var contexts = _contexts.GetOrAdd(sessionId, _ => []);
        lock (contexts)
        {
            var existingIds = contexts.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var attachment in attachments.Where(x => existingIds.Add(x.Id)))
                contexts.Add(attachment);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<AgentContextAttachment>> ListContextAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (!_contexts.TryGetValue(sessionId, out var contexts))
            return Task.FromResult<IReadOnlyCollection<AgentContextAttachment>>([]);

        lock (contexts)
            return Task.FromResult<IReadOnlyCollection<AgentContextAttachment>>(contexts.ToList());
    }
}

internal sealed class InMemoryAgentFeedbackService(IAgentAuditSink auditSink) : IAgentFeedbackService
{
    private readonly ConcurrentDictionary<string, AgentFeedback> _feedback = new(StringComparer.Ordinal);

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

internal sealed class InMemoryAgentAuditStore : IAgentAuditSink, IAgentAuditReader
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
                .Where(x => sessionId is null || string.Equals(x.SessionId, sessionId, StringComparison.Ordinal))
                .OrderByDescending(x => x.CreatedAt);

            if (take is > 0)
                query = query.Take(take.Value);

            return Task.FromResult<IReadOnlyCollection<AgentAuditEvent>>(query.ToList());
        }
    }
}

internal sealed class DefaultAgentProviderRegistry : IAgentProviderRegistry
{
    public DefaultAgentProviderRegistry(IEnumerable<IAgentProvider> providers)
    {
        var registered = providers.ToList();
        if (registered.Count > 1)
            throw new AgentHarnessConflictException(registered.Select(x => x.ProviderId).ToList());

        Active = registered.SingleOrDefault();
    }

    public IAgentProvider? Active { get; }
}

internal static class AgentIds
{
    public static string NewId() => ShortIdentityGenerator.Generate(DateTimeOffset.UtcNow);
}
