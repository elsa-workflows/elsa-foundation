using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Microsoft.Extensions.Options;
using static Elsa.Agent.Services.AgentIds;

namespace Elsa.Agent.Services;

/// <summary>Process-lifetime foreground leases; a turn can only be cancelled through its owning session.</summary>
internal sealed class AgentTurnRegistry : IAgentTurnRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (string TurnId, CancellationTokenSource Source)> _active = new(StringComparer.Ordinal);

    public CancellationToken Register(string sessionId, string turnId)
    {
        lock (_gate)
        {
            if (_active.ContainsKey(sessionId))
                throw new AgentContributionException("A foreground turn is already active for this session.");
            var source = new CancellationTokenSource();
            _active.Add(sessionId, (turnId, source));
            return source.Token;
        }
    }

    public bool Cancel(string sessionId, string turnId)
    {
        CancellationTokenSource source;
        lock (_gate)
        {
            if (!_active.TryGetValue(sessionId, out var active) || active.TurnId != turnId)
                return false;
            source = active.Source;
        }
        try { source.Cancel(); return true; }
        catch (ObjectDisposedException) { return false; }
    }

    public void Unregister(string sessionId, string turnId)
    {
        CancellationTokenSource? source = null;
        lock (_gate)
            if (_active.TryGetValue(sessionId, out var active) && active.TurnId == turnId)
            {
                source = active.Source;
                _active.Remove(sessionId);
            }
        source?.Dispose();
    }

    public bool IsActive(string sessionId)
    {
        lock (_gate) return _active.ContainsKey(sessionId);
    }
}

internal sealed class InMemoryAgentTurnStateStore : IAgentTurnStateStore
{
    private readonly ConcurrentDictionary<string, AgentPausedTurn> _states = new(StringComparer.Ordinal);
    public void Save(AgentPausedTurn state) => _states[state.SessionId] = state;
    public AgentPausedTurn? Find(string sessionId) => _states.GetValueOrDefault(sessionId);
    public void Remove(string sessionId) => _states.TryRemove(sessionId, out _);
}

/// <summary>The active generic engine. Modules own preparation/effects; providers own their SDK protocol.</summary>
internal sealed class DefaultAgentTurnOrchestrator(
    IAgentSessionService sessions,
    IAgentProviderRegistry providers,
    IAgentToolRegistry toolRegistry,
    IAgentToolInvoker toolInvoker,
    IAgentTurnRegistry turnRegistry,
    IAgentTurnStateStore stateStore,
    IAgentProposalService proposals,
    IAgentToolAuthorizer authorizer,
    IOptions<AgentTurnOptions> options) : IAgentStreamingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(string sessionId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var session = await sessions.FindAsync(sessionId, cancellationToken);
        if (session is null || session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            yield return Error("agent.session.unavailable", "The session is missing or expired.", 404);
            yield break;
        }
        var provider = providers.Active;
        if (provider is null)
        {
            yield return Error("agent.provider.not_found", "No agent harness is enabled.", 404);
            yield break;
        }

        var turnId = stateStore.Find(sessionId)?.TurnId ?? NewId();
        CancellationToken turnToken = default;
        AgentStreamEvent? leaseError = null;
        try { turnToken = turnRegistry.Register(sessionId, turnId); }
        catch (AgentContributionException) { leaseError = Error("agent.turn.busy", "This session already has an active foreground turn.", 409); }
        if (leaseError is not null)
        {
            yield return leaseError;
            yield break;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, turnToken);
        var token = linked.Token;
        PreparedTurn? prepared = null;
        var paused = false;
        var completed = false;
        var assistant = new StringBuilder();
        try
        {
            prepared = await PrepareAsync(session, token);
            if (prepared.Error is not null)
            {
                yield return prepared.Error;
                yield break;
            }
            var source = prepared.Source!;
            await sessions.SetMessageStatusAsync(sessionId, source.Id, AgentMessageStatus.Streaming, token);
            var maxSteps = Math.Max(1, options.Value.MaxSteps);
            yield return Lifecycle(AgentStreamEventKind.Started, new AgentTurnStarted(turnId, maxSteps));

            // A receipt continuation only reports the observed outcome. It cannot replay the original
            // request, use the old client snapshot as a fresh read, or acquire another mutation.
            IReadOnlyCollection<IAgentTool> tools = [];
            var admissionCancelled = false;
            try { tools = await GetAdmittedToolsAsync(session, prepared, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { admissionCancelled = true; }
            if (admissionCancelled)
            {
                yield return Lifecycle(AgentStreamEventKind.TurnCancelled, new AgentTurnStarted(turnId, maxSteps));
                yield break;
            }

            if (provider is IAgentHarness harness)
            {
                var request = new AgentHarnessTurnRequest(sessionId, session.ActorId, prepared.History, prepared.Context, tools, session.Policy)
                {
                    TurnId = turnId, TenantId = session.TenantId, Client = source.Client,
                    PendingToolResults = prepared.PendingResults, IsReceiptContinuation = prepared.IsContinuation
                };
                await using var enumerator = harness.RunTurnAsync(request, token).GetAsyncEnumerator(token);
                while (true)
                {
                    var next = await NextAsync(enumerator, token);
                    if (next.Cancelled)
                    {
                        yield return Lifecycle(AgentStreamEventKind.TurnCancelled, new AgentTurnStarted(turnId, maxSteps));
                        yield break;
                    }
                    if (!next.Moved) break;
                    var item = next.Item!;
                    if (item.Kind is AgentStreamEventKind.Started or AgentStreamEventKind.Completed)
                        continue;
                    if (item.Kind == AgentStreamEventKind.MessageDelta) assistant.Append(item.Content);
                    if (item.Kind == AgentStreamEventKind.ProposalCreated && item.Payload is AgentActionProposal proposal)
                    {
                        SavePause(sessionId, turnId, source, proposal, prepared.History, prepared.Context, 0);
                        paused = true;
                        // Stop this SDK loop before another tool can be requested. Its pending
                        // result is explicit, and a later stream receives the actual stored receipt.
                        linked.Cancel();
                        yield return item;
                        yield return Lifecycle(AgentStreamEventKind.ToolApprovalRequested, null, proposal.Id);
                        yield break;
                    }
                    yield return item;
                    if (item.Kind == AgentStreamEventKind.Error) yield break;
                }
                completed = true;
                yield return Lifecycle(AgentStreamEventKind.Completed, null);
                yield break;
            }

            var history = prepared.History;
            var pending = prepared.PendingResults;
            for (var step = prepared.StartStep; step < maxSteps; step++)
            {
                if (token.IsCancellationRequested)
                {
                    yield return Lifecycle(AgentStreamEventKind.TurnCancelled, new AgentTurnStarted(turnId, maxSteps));
                    yield break;
                }
                yield return Lifecycle(AgentStreamEventKind.StepStarted, new AgentStepDescriptor(step, maxSteps));
                var context = new AgentTurnContext(sessionId, sessionId, history, pending, tools.Select(x => x.Descriptor).ToList(), prepared.Context, step, maxSteps);
                var calls = new List<AgentToolCallRequest>();
                var stepText = new StringBuilder();
                await using (var enumerator = provider.ContinueTurnAsync(context, token).GetAsyncEnumerator(token))
                    while (true)
                    {
                        var next = await NextAsync(enumerator, token);
                        if (next.Cancelled)
                        {
                            yield return Lifecycle(AgentStreamEventKind.TurnCancelled, new AgentTurnStarted(turnId, maxSteps));
                            yield break;
                        }
                        if (!next.Moved) break;
                        var item = next.Item!;
                        if (item.Kind is AgentStreamEventKind.Started or AgentStreamEventKind.Completed) continue;
                        if (item.Kind == AgentStreamEventKind.ToolCallRequested && item.Payload is AgentToolCallRequest call) calls.Add(call);
                        if (item.Kind == AgentStreamEventKind.MessageDelta) { stepText.Append(item.Content); assistant.Append(item.Content); }
                        yield return item;
                        if (item.Kind == AgentStreamEventKind.Error) yield break;
                        if (item.Kind == AgentStreamEventKind.ClarificationRequested)
                        {
                            completed = true;
                            yield return Lifecycle(AgentStreamEventKind.Completed, null);
                            yield break;
                        }
                    }
                yield return Lifecycle(AgentStreamEventKind.StepCompleted, new AgentStepDescriptor(step, maxSteps));
                if (stepText.Length > 0) history = Append(history, new(AgentRole.Assistant, stepText.ToString()));
                if (calls.Count == 0)
                {
                    completed = true;
                    yield return Lifecycle(AgentStreamEventKind.Completed, null);
                    yield break;
                }
                var results = new List<AgentToolResult>();
                foreach (var call in calls)
                {
                    yield return Lifecycle(AgentStreamEventKind.ToolCallStarted, call);
                    AgentToolDispatch dispatch;
                    if (!tools.Any(x => string.Equals(x.Descriptor.Name, call.ToolName, StringComparison.OrdinalIgnoreCase)))
                        dispatch = AgentToolDispatch.Denied(new("agent.tool.not_admitted", "This tool is not admitted for the current turn.", 403));
                    else if (!TryParseArguments(call.Arguments, out var arguments))
                        dispatch = AgentToolDispatch.Denied(new("agent.tool.invalid_arguments", "Tool arguments are not a JSON object.", 400));
                    else
                        dispatch = await toolInvoker.DispatchAsync(new(call.ToolCallId, call.ToolName, sessionId, session.ActorId, arguments, prepared.Context)
                        {
                            TurnId = turnId, TenantId = session.TenantId, Client = source.Client, AllowMutations = !prepared.IsContinuation
                        }, session.Policy, token);
                    if (dispatch.Outcome == AgentToolDispatchOutcome.ProposalCreated)
                    {
                        var proposal = dispatch.Proposal!;
                        SavePause(sessionId, turnId, source, proposal, history, prepared.Context, step + 1);
                        paused = true;
                        yield return new(NewId(), AgentStreamEventKind.ProposalCreated, null, proposal.Id, null, DateTimeOffset.UtcNow, AgentResultKind.Proposal, proposal);
                        yield return Lifecycle(AgentStreamEventKind.ToolApprovalRequested, null, proposal.Id);
                        yield break;
                    }
                    var result = dispatch.Result ?? new AgentToolResult(call.ToolCallId, call.ToolName, false, dispatch.Error?.Message ?? "Tool refused.");
                    results.Add(result);
                    history = Append(history, new(AgentRole.Tool, JsonSerializer.Serialize(result, JsonOptions), result.ToolCallId, result.ToolName));
                    yield return Lifecycle(AgentStreamEventKind.ToolCallCompleted, new AgentToolCallOutcome(result.ToolCallId, result.ToolName, result.Succeeded, result.Content, result));
                    yield return new(NewId(), AgentStreamEventKind.ContributedResult, result.Content, null, null, DateTimeOffset.UtcNow, AgentResultKind.ModuleResult, result);
                }
                pending = results;
            }
            completed = true;
            yield return new(NewId(), AgentStreamEventKind.MessageDelta, "The assistant reached its step limit before finishing. Send a new prompt to continue.", null, null, DateTimeOffset.UtcNow, AgentResultKind.Message);
            yield return Lifecycle(AgentStreamEventKind.Completed, null);
        }
        finally
        {
            if (prepared?.Source is { } source && !paused)
            {
                stateStore.Remove(sessionId);
                await sessions.SetMessageStatusAsync(sessionId, source.Id,
                    completed ? AgentMessageStatus.Completed : token.IsCancellationRequested ? AgentMessageStatus.Cancelled : AgentMessageStatus.Failed,
                    CancellationToken.None);
                if (assistant.Length > 0)
                    await sessions.AddMessageAsync(sessionId, new(AgentRole.Assistant, assistant.ToString(), completed ? AgentMessageStatus.Completed : AgentMessageStatus.Failed, null, [], []), CancellationToken.None);
            }
            turnRegistry.Unregister(sessionId, turnId);
        }
    }

    private async Task<PreparedTurn> PrepareAsync(AgentSession session, CancellationToken cancellationToken)
    {
        var messages = await sessions.ListMessagesAsync(session.Id, cancellationToken);
        if (stateStore.Find(session.Id) is { } paused)
        {
            var source = messages.FirstOrDefault(x => x.Id == paused.SourceMessageId);
            if (source is null) return PreparedTurn.Fail(Error("agent.turn.source_missing", "The pending turn's source message is unavailable.", 409));
            var proposal = await proposals.FindAsync(paused.ProposalId, cancellationToken);
            if (proposal is null || proposal.Status is AgentActionProposalStatus.AwaitingApproval or AgentActionProposalStatus.Approved or AgentActionProposalStatus.Executing or AgentActionProposalStatus.AwaitingClient)
                return PreparedTurn.Fail(Error("agent.turn.awaiting_review", "The original turn remains pending; resolve its proposal before continuing.", 409));
            var result = await proposals.FindResultAsync(paused.ProposalId, cancellationToken)
                ?? new AgentToolResult(paused.ToolCallId, paused.ToolName, false, $"The reviewed invocation has status {proposal.Status}; no applied result is available.");
            var history = Append(paused.History, new(AgentRole.Tool, JsonSerializer.Serialize(result, JsonOptions), result.ToolCallId, result.ToolName));
            return new(source, history, [result], 0, paused.Context, true, null);
        }
        var latest = messages.LastOrDefault(x => x.Role == AgentRole.User && x.Status == AgentMessageStatus.Pending);
        if (latest is null) return PreparedTurn.Fail(Error("agent.message.no_pending", "There is no new prompt or retained receipt continuation to run.", 409));
        var initial = messages.Where(x => x.Role is AgentRole.User or AgentRole.Assistant or AgentRole.System or AgentRole.Tool)
            .Select(x => new AgentTurnMessage(x.Role, x.Content)).ToList();
        return new(latest, initial, [], 0, latest.Context, false, null);
    }

    private async Task<IReadOnlyCollection<IAgentTool>> GetAdmittedToolsAsync(AgentSession session, PreparedTurn prepared, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var tools = new List<IAgentTool>();
        if (prepared.IsContinuation) return tools;
        var scopes = prepared.Context.SelectMany(x => x.AgentScopes).ToHashSet(StringComparer.Ordinal);
        foreach (var tool in await toolRegistry.ListAsync(token))
        {
            var descriptor = tool.Descriptor;
            if (descriptor.AgentScopes.Any(x => !scopes.Contains(x))) continue;
            var targets = prepared.Context.Where(x => x.ModuleId == descriptor.ModuleId && x.Target is not null)
                .Select(x => x.Target).Distinct().ToList();
            if (targets.Count == 0) targets.Add(null);
            foreach (var target in targets)
                if ((await authorizer.AuthorizeAsync(descriptor, session.ActorId, session.TenantId, target, token)).Allowed)
                {
                    // Availability is a preflight hint. Preparation/execution recheck the exact
                    // target; ordering a guidance attachment first cannot suppress a valid target.
                    tools.Add(tool);
                    break;
                }
        }
        return tools;
    }

    private void SavePause(string sessionId, string turnId, AgentMessage source, AgentActionProposal proposal,
        IReadOnlyList<AgentTurnMessage> history, IReadOnlyCollection<AgentContextAttachment> context, int step)
        => stateStore.Save(new(sessionId, turnId, proposal.Id, proposal.PreparedInvocation?.ToolCallId ?? string.Empty,
            proposal.PreparedInvocation?.ToolName ?? proposal.CapabilityId, history, step)
        { SourceMessageId = source.Id, Context = context, Client = source.Client });

    private static async Task<(bool Moved, AgentStreamEvent? Item, bool Cancelled)> NextAsync(IAsyncEnumerator<AgentStreamEvent> enumerator, CancellationToken token)
    {
        try { return await enumerator.MoveNextAsync() ? (true, enumerator.Current, false) : (false, null, false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return (false, null, true); }
    }

    private static bool TryParseArguments(string json, out IReadOnlyDictionary<string, object?> arguments)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                arguments = document.RootElement.EnumerateObject().ToDictionary(x => x.Name, x => (object?)x.Value.Clone(), StringComparer.Ordinal);
                return true;
            }
        }
        catch (JsonException) { }
        arguments = new Dictionary<string, object?>();
        return false;
    }

    private static IReadOnlyList<AgentTurnMessage> Append(IReadOnlyList<AgentTurnMessage> history, AgentTurnMessage message) => [.. history, message];
    private static AgentStreamEvent Lifecycle(AgentStreamEventKind kind, object? payload, string? proposalId = null) => new(NewId(), kind, null, proposalId, null, DateTimeOffset.UtcNow, null, payload);
    private static AgentStreamEvent Error(string code, string message, int status) => new(NewId(), AgentStreamEventKind.Error, null, null, new(code, message, status), DateTimeOffset.UtcNow, AgentResultKind.Error);

    private sealed record PreparedTurn(AgentMessage? Source, IReadOnlyList<AgentTurnMessage> History,
        IReadOnlyList<AgentToolResult> PendingResults, int StartStep, IReadOnlyCollection<AgentContextAttachment> Context, bool IsContinuation, AgentStreamEvent? Error)
    {
        public static PreparedTurn Fail(AgentStreamEvent error) => new(null, [], [], 0, [], false, error);
    }
}
