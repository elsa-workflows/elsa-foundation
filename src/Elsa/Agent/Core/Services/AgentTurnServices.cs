using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using static Elsa.Agent.Core.Services.AgentIds;

namespace Elsa.Agent.Core.Services;

public sealed class AgentTurnRegistry : IAgentTurnRegistry
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _turns = new(StringComparer.Ordinal);

    public CancellationToken Register(string turnId)
    {
        var cts = _turns.GetOrAdd(turnId, _ => new CancellationTokenSource());
        return cts.Token;
    }

    public bool Cancel(string turnId)
    {
        if (!_turns.TryGetValue(turnId, out var cts))
            return false;

        try
        {
            cts.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            // The turn completed and disposed its source between the lookup and the cancel; treat as a no-op.
            return false;
        }
    }

    public void Unregister(string turnId)
    {
        if (_turns.TryRemove(turnId, out var cts))
            cts.Dispose();
    }
}

public sealed class InMemoryAgentTurnStateStore : IAgentTurnStateStore
{
    private readonly ConcurrentDictionary<string, AgentPausedTurn> _states = new(StringComparer.OrdinalIgnoreCase);

    public void Save(AgentPausedTurn state) => _states[state.SessionId] = state;

    public AgentPausedTurn? Find(string sessionId)
    {
        _states.TryGetValue(sessionId, out var state);
        return state;
    }

    public void Remove(string sessionId) => _states.TryRemove(sessionId, out _);
}

/// <summary>
/// Applies policy to a tool invocation: read-only tools run inline; mutating tools become reviewable proposals
/// when the policy requires approval, otherwise run inline (full-auto).
/// </summary>
public sealed class DefaultAgentToolInvoker(IAgentToolRegistry registry, IAgentProposalService proposals, IAgentAuditSink auditSink) : IAgentToolInvoker
{
    public async Task<AgentToolDispatch> DispatchAsync(AgentToolInvocation invocation, AgentPolicy policy, CancellationToken cancellationToken = default)
    {
        var tool = registry.Find(invocation.ToolName);
        if (tool is null)
        {
            await AuditAsync(invocation, "denied", "Tool is not registered.", cancellationToken);
            return AgentToolDispatch.Denied(new("agent.tool.not_found", $"Tool '{invocation.ToolName}' is not registered.", 404));
        }

        var descriptor = tool.Descriptor;
        var requiresApproval = descriptor.Mutability == AgentToolMutability.Mutating && policy.RequiresApprovalForMutations;

        if (requiresApproval)
        {
            var proposal = await proposals.AddAsync(BuildProposal(invocation, descriptor), cancellationToken);
            await AuditAsync(invocation, "proposal-created", $"Mutating tool routed to proposal {proposal.Id}.", cancellationToken);
            return AgentToolDispatch.Proposed(proposal);
        }

        var dispatch = await InvokeAsync(tool, invocation, cancellationToken);
        await AuditAsync(invocation, dispatch.Outcome == AgentToolDispatchOutcome.Executed ? "executed" : "failed", dispatch.Result?.Content ?? dispatch.Error?.Message ?? "", cancellationToken);
        return dispatch;
    }

    private Task AuditAsync(AgentToolInvocation invocation, string outcome, string summary, CancellationToken cancellationToken)
        => auditSink.EmitAsync(new(
            NewId(),
            AgentAuditEventKind.ToolInvoked,
            invocation.SessionId,
            invocation.ActorId,
            $"Agent tool '{invocation.ToolName}' {outcome}.",
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>
            {
                ["toolCallId"] = invocation.ToolCallId,
                ["toolName"] = invocation.ToolName,
                ["outcome"] = outcome,
                ["summary"] = Truncate(summary, 256)
            }), cancellationToken);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static async Task<AgentToolDispatch> InvokeAsync(IAgentTool tool, AgentToolInvocation invocation, CancellationToken cancellationToken)
    {
        try
        {
            var result = await tool.InvokeAsync(invocation, cancellationToken);
            return result.Succeeded
                ? AgentToolDispatch.Executed(result.Value!)
                : AgentToolDispatch.Denied(result.Error!);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return AgentToolDispatch.Denied(new("agent.tool.invocation_failed", $"Tool '{invocation.ToolName}' failed: {ex.Message}", 500));
        }
    }

    private static AgentActionProposal BuildProposal(AgentToolInvocation invocation, AgentToolDescriptor descriptor)
    {
        var now = DateTimeOffset.UtcNow;
        return new AgentActionProposal(
            NewId(),
            invocation.SessionId,
            null,
            descriptor.Name,
            "tool-invocation",
            descriptor.DisplayName,
            descriptor.Description,
            descriptor.Risk,
            null,
            [],
            [invocation.Arguments],
            [],
            null,
            descriptor.RequiredPermissions,
            "tool",
            invocation.ToolName,
            RequiresApproval: true,
            AgentActionProposalStatus.AwaitingApproval,
            null,
            null,
            now,
            now);
    }
}

/// <summary>
/// Drives a multi-step agent turn: it calls the provider for the next step, executes any tool calls the provider
/// requests, feeds their results back, and repeats until the provider stops requesting tools, a terminal event
/// occurs, a mutating tool pauses for approval, the turn is cancelled, or the step budget is exhausted.
/// </summary>
public sealed class DefaultAgentTurnOrchestrator(
    IAgentSessionService sessions,
    IAgentProviderRegistry providers,
    IAgentToolRegistry toolRegistry,
    IAgentToolInvoker toolInvoker,
    IAgentTurnRegistry turnRegistry,
    IAgentTurnStateStore stateStore,
    IAgentProposalService proposals,
    AgentTurnOptions options) : IAgentStreamingService
{
    public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(string sessionId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var session = await sessions.FindAsync(sessionId, cancellationToken);
        if (session is null)
        {
            yield return Error("agent.session.not_found", $"Agent session '{sessionId}' was not found.", 404);
            yield break;
        }

        var provider = providers.Active;
        if (provider is null)
        {
            yield return Error("agent.provider.not_found", "No agent harness is enabled.", 404);
            yield break;
        }

        var prepared = await PrepareAsync(session, cancellationToken);
        if (prepared.Error is not null)
        {
            yield return prepared.Error;
            yield break;
        }

        var turnId = NewId();
        var turnToken = turnRegistry.Register(turnId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, turnToken);
        var token = linked.Token;
        var maxSteps = options.MaxSteps;

        try
        {
            yield return Lifecycle(AgentStreamEventKind.Started, new AgentTurnStarted(turnId, maxSteps));

            // Loop-owning providers (e.g. the GitHub Copilot harness) run the whole turn themselves; we just forward
            // their events and own the turn-level lifecycle (Started/Completed/TurnCancelled) for cancellation.
            if (provider is IAgentHarness harness)
            {
                var request = new AgentHarnessTurnRequest(session.Id, session.ActorId, prepared.History, prepared.Context, toolRegistry.Tools, session.Policy);
                var harnessErrored = false;
                var harnessCancelled = false;

                await using (var harnessEnumerator = harness.RunTurnAsync(request, token).GetAsyncEnumerator(token))
                {
                    while (true)
                    {
                        bool moved;
                        try
                        {
                            moved = await harnessEnumerator.MoveNextAsync();
                        }
                        catch (OperationCanceledException) when (turnToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                        {
                            harnessCancelled = true;
                            break;
                        }

                        if (!moved)
                            break;

                        var harnessEvent = harnessEnumerator.Current;
                        if (harnessEvent.Kind is AgentStreamEventKind.Started or AgentStreamEventKind.Completed)
                            continue; // The orchestrator owns turn lifecycle.

                        if (harnessEvent.Kind == AgentStreamEventKind.Error)
                            harnessErrored = true;

                        yield return harnessEvent;
                    }
                }

                if (harnessCancelled)
                {
                    yield return Lifecycle(AgentStreamEventKind.TurnCancelled, new AgentTurnStarted(turnId, maxSteps));
                    yield break;
                }

                if (!harnessErrored)
                    yield return Lifecycle(AgentStreamEventKind.Completed, null);

                yield break;
            }

            var history = prepared.History;
            var pendingResults = prepared.PendingResults;
            var tools = toolRegistry.Descriptors;
            var seenMessageIds = new HashSet<string>(prepared.SeenMessageIds, StringComparer.OrdinalIgnoreCase);

            for (var step = prepared.StartStep; step < maxSteps; step++)
            {
                if (token.IsCancellationRequested)
                {
                    yield return Lifecycle(AgentStreamEventKind.TurnCancelled, new AgentTurnStarted(turnId, maxSteps));
                    yield break;
                }

                // Steering: fold in any user messages queued since the turn (or last step) started before the next step.
                if (step > prepared.StartStep)
                {
                    foreach (var queued in await CollectQueuedUserMessagesAsync(session.Id, seenMessageIds, token))
                    {
                        history = Append(history, new AgentTurnMessage(AgentRole.User, queued.Content));
                        yield return new AgentStreamEvent(NewId(), AgentStreamEventKind.Progress, $"Incorporating follow-up: {queued.Content}", null, null, DateTimeOffset.UtcNow, null, new AgentStepDescriptor(step, maxSteps));
                    }
                }

                yield return StepEvent(AgentStreamEventKind.StepStarted, step, maxSteps);

                var context = new AgentTurnContext(session.Id, session.Id, history, pendingResults, tools, prepared.Context, step, maxSteps);
                var toolCalls = new List<AgentToolCallRequest>();
                var assistantText = new StringBuilder();
                var cancelled = false;
                var clarified = false;
                var errored = false;

                await using (var enumerator = provider.ContinueTurnAsync(context, token).GetAsyncEnumerator(token))
                {
                    while (true)
                    {
                        bool moved;
                        try
                        {
                            moved = await enumerator.MoveNextAsync();
                        }
                        catch (OperationCanceledException) when (turnToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                        {
                            cancelled = true;
                            break;
                        }

                        if (!moved)
                            break;

                        var streamEvent = enumerator.Current;
                        switch (streamEvent.Kind)
                        {
                            case AgentStreamEventKind.Started:
                            case AgentStreamEventKind.Completed:
                                // The orchestrator owns turn lifecycle; ignore provider lifecycle markers.
                                break;
                            case AgentStreamEventKind.ToolCallRequested:
                                if (streamEvent.Payload is AgentToolCallRequest request)
                                {
                                    toolCalls.Add(request);
                                    yield return streamEvent;
                                }
                                break;
                            case AgentStreamEventKind.ClarificationRequested:
                                clarified = true;
                                yield return streamEvent;
                                break;
                            case AgentStreamEventKind.Error:
                                errored = true;
                                yield return streamEvent;
                                break;
                            case AgentStreamEventKind.MessageDelta:
                                if (!string.IsNullOrEmpty(streamEvent.Content))
                                    assistantText.Append(streamEvent.Content);
                                yield return streamEvent;
                                break;
                            default:
                                yield return streamEvent;
                                break;
                        }

                        if (clarified || errored)
                            break;
                    }
                }

                if (cancelled)
                {
                    yield return Lifecycle(AgentStreamEventKind.TurnCancelled, new AgentTurnStarted(turnId, maxSteps));
                    yield break;
                }

                yield return StepEvent(AgentStreamEventKind.StepCompleted, step, maxSteps);

                if (assistantText.Length > 0)
                    history = Append(history, new AgentTurnMessage(AgentRole.Assistant, assistantText.ToString()));

                if (errored)
                    yield break;

                if (clarified)
                {
                    yield return Lifecycle(AgentStreamEventKind.Completed, null);
                    yield break;
                }

                if (toolCalls.Count == 0)
                {
                    yield return Lifecycle(AgentStreamEventKind.Completed, null);
                    yield break;
                }

                var nextResults = new List<AgentToolResult>();
                var paused = false;

                foreach (var toolCall in toolCalls)
                {
                    if (token.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }

                    yield return ToolEvent(AgentStreamEventKind.ToolCallStarted, toolCall.ToolCallId, toolCall);

                    var invocation = new AgentToolInvocation(
                        toolCall.ToolCallId,
                        toolCall.ToolName,
                        session.Id,
                        session.ActorId,
                        ParseArguments(toolCall.Arguments),
                        prepared.Context);

                    AgentToolDispatch dispatch;
                    try
                    {
                        dispatch = await toolInvoker.DispatchAsync(invocation, session.Policy, token);
                    }
                    catch (OperationCanceledException) when (turnToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }

                    switch (dispatch.Outcome)
                    {
                        case AgentToolDispatchOutcome.Executed:
                            var result = dispatch.Result!;
                            nextResults.Add(result);
                            history = Append(history, new AgentTurnMessage(AgentRole.Tool, result.Content, result.ToolCallId, result.ToolName));
                            yield return ToolCompleted(toolCall, true, result.Content, result.Payload);
                            break;
                        case AgentToolDispatchOutcome.ProposalCreated:
                            var proposal = dispatch.Proposal!;
                            yield return new AgentStreamEvent(NewId(), AgentStreamEventKind.ProposalCreated, null, proposal.Id, null, DateTimeOffset.UtcNow, AgentResultKind.Proposal, proposal);
                            yield return new AgentStreamEvent(NewId(), AgentStreamEventKind.ToolApprovalRequested, null, proposal.Id, null, DateTimeOffset.UtcNow);
                            stateStore.Save(new AgentPausedTurn(session.Id, turnId, proposal.Id, toolCall.ToolCallId, toolCall.ToolName, history, step + 1));
                            paused = true;
                            break;
                        case AgentToolDispatchOutcome.Denied:
                            var error = dispatch.Error!;
                            nextResults.Add(new AgentToolResult(toolCall.ToolCallId, toolCall.ToolName, false, error.Message));
                            history = Append(history, new AgentTurnMessage(AgentRole.Tool, error.Message, toolCall.ToolCallId, toolCall.ToolName));
                            yield return ToolCompleted(toolCall, false, error.Message, null);
                            break;
                    }

                    if (paused)
                        break;
                }

                if (cancelled)
                {
                    yield return Lifecycle(AgentStreamEventKind.TurnCancelled, new AgentTurnStarted(turnId, maxSteps));
                    yield break;
                }

                if (paused)
                    yield break;

                pendingResults = nextResults;
            }

            // Step budget exhausted without a natural completion.
            yield return new AgentStreamEvent(NewId(), AgentStreamEventKind.MessageDelta, "The assistant reached its step limit before finishing. Send a follow-up to continue.", null, null, DateTimeOffset.UtcNow, AgentResultKind.Message);
            yield return Lifecycle(AgentStreamEventKind.Completed, null);
        }
        finally
        {
            turnRegistry.Unregister(turnId);
        }
    }

    private async Task<PreparedTurn> PrepareAsync(AgentSession session, CancellationToken cancellationToken)
    {
        var messages = await sessions.ListMessagesAsync(session.Id, cancellationToken);
        var latestUser = messages.LastOrDefault(x => x.Role == AgentRole.User);
        if (latestUser is null)
            return PreparedTurn.Fail(Error("agent.message.not_found", $"Agent session '{session.Id}' does not have a user message to stream.", 404));

        var context = latestUser.Context;
        var seenMessageIds = messages.Select(x => x.Id).ToList();

        var paused = stateStore.Find(session.Id);
        if (paused is not null)
        {
            var proposal = await proposals.FindAsync(paused.ProposalId, cancellationToken);
            if (proposal is not null && IsResolved(proposal.Status))
            {
                stateStore.Remove(session.Id);
                var succeeded = proposal.Status == AgentActionProposalStatus.Executed;
                var content = succeeded
                    ? $"The '{paused.ToolName}' change was approved and applied."
                    : $"The '{paused.ToolName}' change did not complete (status: {proposal.Status}). Continue without it.";
                var resumeResult = new AgentToolResult(paused.ToolCallId, paused.ToolName, succeeded, content);
                var resumeHistory = Append(paused.History, new AgentTurnMessage(AgentRole.Tool, content, paused.ToolCallId, paused.ToolName));
                return new PreparedTurn(resumeHistory, [resumeResult], paused.NextStepIndex, context, seenMessageIds, null);
            }

            // Proposal is gone or still awaiting approval/execution: drop stale state and start a fresh turn.
            stateStore.Remove(session.Id);
        }

        var history = messages
            .Where(x => x.Role is AgentRole.User or AgentRole.Assistant or AgentRole.System or AgentRole.Tool)
            .Select(x => new AgentTurnMessage(x.Role, x.Content))
            .ToList();

        return new PreparedTurn(history, [], 0, context, seenMessageIds, null);
    }

    private async Task<IReadOnlyList<AgentMessage>> CollectQueuedUserMessagesAsync(string sessionId, HashSet<string> seenMessageIds, CancellationToken cancellationToken)
    {
        var messages = await sessions.ListMessagesAsync(sessionId, cancellationToken);
        return messages
            .Where(x => x.Role == AgentRole.User && seenMessageIds.Add(x.Id))
            .ToList();
    }

    private static IReadOnlyList<AgentTurnMessage> Append(IReadOnlyList<AgentTurnMessage> history, AgentTurnMessage message)
        => [.. history, message];

    // A paused turn resumes once its proposal reaches a terminal state. Executed means apply succeeded;
    // the rest mean the change won't happen, so the loop continues with a failed tool result.
    private static bool IsResolved(AgentActionProposalStatus status)
        => status is AgentActionProposalStatus.Executed
            or AgentActionProposalStatus.Denied
            or AgentActionProposalStatus.Failed
            or AgentActionProposalStatus.Cancelled
            or AgentActionProposalStatus.Expired;

    private static IReadOnlyDictionary<string, object?> ParseArguments(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return new Dictionary<string, object?>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(arguments) ?? new Dictionary<string, object?>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, object?> { ["raw"] = arguments };
        }
    }

    private static AgentStreamEvent Lifecycle(AgentStreamEventKind kind, AgentTurnStarted? payload)
        => new(NewId(), kind, null, null, null, DateTimeOffset.UtcNow, null, payload);

    private static AgentStreamEvent StepEvent(AgentStreamEventKind kind, int step, int maxSteps)
        => new(NewId(), kind, null, null, null, DateTimeOffset.UtcNow, null, new AgentStepDescriptor(step, maxSteps));

    private static AgentStreamEvent ToolEvent(AgentStreamEventKind kind, string toolCallId, AgentToolCallRequest request)
        => new(NewId(), kind, null, toolCallId, null, DateTimeOffset.UtcNow, null, request);

    private static AgentStreamEvent ToolCompleted(AgentToolCallRequest request, bool succeeded, string summary, object? result)
        => new(NewId(), AgentStreamEventKind.ToolCallCompleted, null, request.ToolCallId, null, DateTimeOffset.UtcNow, null, new AgentToolCallOutcome(request.ToolCallId, request.ToolName, succeeded, summary, result));

    private static AgentStreamEvent Error(string code, string message, int statusCode)
        => new(NewId(), AgentStreamEventKind.Error, null, null, new(code, message, statusCode), DateTimeOffset.UtcNow, AgentResultKind.Error);

    private sealed record PreparedTurn(
        IReadOnlyList<AgentTurnMessage> History,
        IReadOnlyList<AgentToolResult> PendingResults,
        int StartStep,
        IReadOnlyCollection<AgentContextAttachment> Context,
        IReadOnlyCollection<string> SeenMessageIds,
        AgentStreamEvent? Error)
    {
        public static PreparedTurn Fail(AgentStreamEvent error) => new([], [], 0, [], [], error);
    }
}
