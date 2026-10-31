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
    IAgentContextCollector contextCollector,
    IAgentPolicyEvaluator policyEvaluator,
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
        // A custom store may canonicalize its lookup input. Keep lifecycle state on the returned
        // identity; the default store requires an exact opaque session ID.
        sessionId = session.Id;
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
        var observedResults = new List<AgentToolResult>();
        AgentError? disclosureStop = null;
        try
        {
            var preparationCancelled = false;
            try { prepared = await PrepareAsync(session, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { preparationCancelled = true; }
            if (preparationCancelled)
            {
                // An already accepted receipt remains resumable. A cancelled new prompt must not
                // strand the session's pending-message gate while an abandoned source finishes.
                paused = stateStore.Find(sessionId) is not null;
                if (!paused)
                {
                    var cancelledPrompt = (await sessions.ListMessagesAsync(sessionId, CancellationToken.None))
                        .LastOrDefault(x => x.Role == AgentRole.User && x.Status == AgentMessageStatus.Pending);
                    if (cancelledPrompt is not null)
                        await sessions.SetMessageStatusAsync(sessionId, cancelledPrompt.Id, AgentMessageStatus.Cancelled, CancellationToken.None);
                }
                yield return Lifecycle(AgentStreamEventKind.TurnCancelled, new AgentTurnStarted(turnId, Math.Max(1, options.Value.MaxSteps)));
                yield break;
            }
            if (prepared is null) yield break;
            if (prepared.Error is not null)
            {
                paused = prepared.KeepPause;
                yield return Lifecycle(AgentStreamEventKind.ContextAdmitted, new AgentContextContribution([], prepared.Diagnostics));
                yield return prepared.Error;
                yield break;
            }
            observedResults.AddRange(prepared.DisclosureResults);
            var source = prepared.Source!;
            await sessions.SetMessageStatusAsync(sessionId, source.Id, AgentMessageStatus.Streaming, token);
            var maxSteps = Math.Max(1, options.Value.MaxSteps);
            yield return Lifecycle(AgentStreamEventKind.Started, new AgentTurnStarted(turnId, maxSteps));
            yield return Lifecycle(AgentStreamEventKind.ContextAdmitted, new AgentContextContribution(prepared.Context, prepared.Diagnostics));

            var receiptOnly = prepared.IsContinuation;
            var guardedInvoker = new ProviderToolInvoker(toolInvoker, () => receiptOnly, async (invocation, dispatchToken) =>
            {
                // Delegated SDK functions capture their initial context, and a host provider can
                // request several calls in one batch. Neither snapshot admits a later operation.
                var historical = await AuthorizeHistoryAsync(session, await sessions.ListMessagesAsync(sessionId, dispatchToken), dispatchToken);
                if (historical is not null) return new(null, historical, true);
                var admission = await AdmitAsync(session, source.Context, observedResults, false, false, dispatchToken);
                if (!admission.Succeeded || admission.Value is null) return new(null, admission.Error, true);
                prepared = prepared with { Context = admission.Value.Attachments, Diagnostics = admission.Value.Diagnostics };
                var admittedTools = await GetAdmittedToolsAsync(session, prepared, dispatchToken);
                var descriptor = admittedTools.FirstOrDefault(x => string.Equals(x.Descriptor.Name, invocation.ToolName, StringComparison.OrdinalIgnoreCase))?.Descriptor;
                if (descriptor is null)
                    return new(null, new("agent.tool.not_admitted", "This tool is not admitted for the current turn.", 403));
                // An allowed projection can refresh evidence without authorizing this accepted
                // turn to move to another resource or revision. Fresh intent must establish that.
                if (OriginalTargetsMissing(descriptor.ModuleId, source.Context, prepared.Context)
                    || OriginalTargetsMissing(descriptor.ModuleId, invocation.Context, prepared.Context))
                    return new(null, new("agent.tool.context_changed", "The admitted resource context changed. Submit fresh context before another operation.", 409));
                return new(invocation with { Context = prepared.Context }, null);
            }, async (invocation, result, dispatchToken) =>
            {
                // Record the real observation before attempting another disclosure. Cancellation,
                // provider errors and an empty assistant response cannot erase its provenance.
                await RecordToolResultAsync(sessionId, invocation.Context, result);
                observedResults.Add(result);
                var historical = await AuthorizeHistoryAsync(session, await sessions.ListMessagesAsync(sessionId, dispatchToken), dispatchToken);
                if (historical is not null) return historical;
                var admission = await AdmitAsync(session, source.Context, observedResults,
                    receiptOnly || result.Receipt is not null, false, dispatchToken);
                if (!admission.Succeeded || admission.Value is null) return admission.Error;
                receiptOnly |= result.Receipt is not null;
                prepared = prepared with { Context = admission.Value.Attachments, Diagnostics = admission.Value.Diagnostics, IsContinuation = receiptOnly };
                return null;
            }, error =>
            {
                Interlocked.CompareExchange(ref disclosureStop, error, null);
                // SDKs may convert function exceptions into model-visible tool text. Cancel their
                // whole foreground loop before throwing; the genuine outcome remains recorded.
                try { linked.Cancel(); }
                catch (ObjectDisposedException) { }
            });

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
                    PendingToolResults = prepared.PendingResults, IsReceiptContinuation = prepared.IsContinuation, ToolInvoker = guardedInvoker
                };
                await using var enumerator = harness.RunTurnAsync(request, token).GetAsyncEnumerator(token);
                while (true)
                {
                    var next = await NextAsync(enumerator, token);
                    if (next.Cancelled)
                    {
                        yield return disclosureStop is { } denial ? Error(denial.Code, denial.Message, denial.StatusCode)
                            : Lifecycle(AgentStreamEventKind.TurnCancelled, new AgentTurnStarted(turnId, maxSteps));
                        yield break;
                    }
                    if (!next.Moved) break;
                    var item = next.Item!;
                    if (item.Kind is AgentStreamEventKind.Started or AgentStreamEventKind.Completed)
                        continue;
                    if (item.Kind == AgentStreamEventKind.MessageDelta) assistant.Append(item.Content);
                    if (item.Kind == AgentStreamEventKind.ContributedResult)
                        yield return Lifecycle(AgentStreamEventKind.ContextAdmitted, new AgentContextContribution(prepared.Context, prepared.Diagnostics));
                    if (item.Kind == AgentStreamEventKind.ProposalCreated && item.Payload is AgentActionProposal proposal)
                    {
                        SavePause(sessionId, turnId, source, proposal, prepared.History, prepared.Context, 0, observedResults);
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
                    yield return disclosureStop is { } denial ? Error(denial.Code, denial.Message, denial.StatusCode)
                        : Lifecycle(AgentStreamEventKind.TurnCancelled, new AgentTurnStarted(turnId, maxSteps));
                    yield break;
                }
                var historical = await AuthorizeHistoryAsync(session, await sessions.ListMessagesAsync(sessionId, token), token);
                var refreshed = historical is null
                    ? await AdmitAsync(session, source.Context, observedResults, prepared.IsContinuation, false, token)
                    : new AgentResult<AgentContextContribution>(null, historical);
                if (!refreshed.Succeeded || refreshed.Value is null)
                {
                    var denial = refreshed.Error!;
                    yield return Lifecycle(AgentStreamEventKind.ContextAdmitted, new AgentContextContribution([], [new("host", denial.Code, denial.Message, true)]));
                    yield return Error(denial.Code, denial.Message, denial.StatusCode);
                    yield break;
                }
                prepared = prepared with { Context = refreshed.Value.Attachments, Diagnostics = refreshed.Value.Diagnostics };
                tools = await GetAdmittedToolsAsync(session, prepared, token);
                yield return Lifecycle(AgentStreamEventKind.ContextAdmitted, new AgentContextContribution(prepared.Context, prepared.Diagnostics));
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
                        dispatch = AgentToolDispatch.Denied(new("agent.tool.invalid_arguments", "Tool arguments must be a JSON object with unique property names.", 400));
                    else
                    {
                        AgentToolDispatch? observedDispatch = null;
                        try { observedDispatch = await guardedInvoker.DispatchAsync(new(call.ToolCallId, call.ToolName, sessionId, session.ActorId, arguments, prepared.Context)
                        {
                            TurnId = turnId, TenantId = session.TenantId, Client = source.Client, AllowMutations = !prepared.IsContinuation
                        }, session.Policy, token); }
                        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                        if (observedDispatch is null)
                        {
                            yield return disclosureStop is { } denial ? Error(denial.Code, denial.Message, denial.StatusCode)
                                : Lifecycle(AgentStreamEventKind.TurnCancelled, new AgentTurnStarted(turnId, maxSteps));
                            yield break;
                        }
                        dispatch = observedDispatch;
                    }
                    if (dispatch.Outcome == AgentToolDispatchOutcome.ProposalCreated)
                    {
                        var proposal = dispatch.Proposal!;
                        SavePause(sessionId, turnId, source, proposal, history, prepared.Context, step + 1, observedResults);
                        paused = true;
                        yield return new(NewId(), AgentStreamEventKind.ProposalCreated, null, proposal.Id, null, DateTimeOffset.UtcNow, AgentResultKind.Proposal, proposal);
                        yield return Lifecycle(AgentStreamEventKind.ToolApprovalRequested, null, proposal.Id);
                        yield break;
                    }
                    var result = dispatch.Result ?? new AgentToolResult(call.ToolCallId, call.ToolName, false, dispatch.Error?.Message ?? "Tool refused.");
                    results.Add(result);
                    history = Append(history, new(AgentRole.Tool, JsonSerializer.Serialize(result, JsonOptions), result.ToolCallId, result.ToolName));
                    if (dispatch.Result is not null)
                        yield return Lifecycle(AgentStreamEventKind.ContextAdmitted, new AgentContextContribution(prepared.Context, prepared.Diagnostics));
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
                    completed ? AgentMessageStatus.Completed : disclosureStop is not null ? AgentMessageStatus.Failed
                        : token.IsCancellationRequested ? AgentMessageStatus.Cancelled : AgentMessageStatus.Failed,
                    CancellationToken.None);
                if (assistant.Length > 0)
                    await sessions.AddMessageAsync(sessionId, new(AgentRole.Assistant, assistant.ToString(), completed ? AgentMessageStatus.Completed : AgentMessageStatus.Failed,
                        null, source.ContextAttachmentIds, source.Context)
                    { ToolResults = observedResults.ToArray(), HasDisclosureProvenance = true }, CancellationToken.None);
            }
            turnRegistry.Unregister(sessionId, turnId);
        }
    }

    private async Task RecordToolResultAsync(string sessionId, IReadOnlyCollection<AgentContextAttachment> context, AgentToolResult result)
    {
        var identity = result.Access?.InvocationId;
        if (string.IsNullOrWhiteSpace(identity))
            throw new AgentContributionException("An observed tool outcome has no server preparation identity.");
        var fingerprint = AgentInvocationIntegrity.Digest(result);
        var recorded = await sessions.AddMessageAsync(sessionId, new(AgentRole.Tool, JsonSerializer.Serialize(result, JsonOptions),
            AgentMessageStatus.Completed, null, context.Select(x => x.Id).ToArray(), context)
        {
            RequestId = identity, DeliveryFingerprint = fingerprint,
            ToolResults = [result], HasDisclosureProvenance = true
        }, CancellationToken.None);
        static string ContextDigest(IReadOnlyCollection<AgentContextAttachment> values)
            => AgentInvocationIntegrity.Digest(values.OrderBy(x => x.ContributorId, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray());
        if (recorded.SessionId != sessionId || recorded.Role != AgentRole.Tool || !recorded.HasDisclosureProvenance
            || recorded.RequestId != identity || recorded.DeliveryFingerprint != fingerprint || recorded.ToolResults.Count != 1
            || AgentInvocationIntegrity.Digest(recorded.ToolResults[0]) != fingerprint || ContextDigest(recorded.Context) != ContextDigest(context))
            throw new AgentContributionException("The session store did not retain complete disclosure provenance for the observed outcome.");
    }

    private static bool OriginalTargetsMissing(string moduleId, IReadOnlyCollection<AgentContextAttachment> original,
        IReadOnlyCollection<AgentContextAttachment> current)
    {
        var before = original.Where(x => x.ModuleId == moduleId && x.Target is not null).Select(x => x.Target).ToArray();
        var after = current.Where(x => x.ModuleId == moduleId && x.Target is not null).Select(x => x.Target).ToArray();
        // Authorized detail may add targets without moving the operation's original binding.
        return before.Any(x => !after.Any(y => AgentInvocationIntegrity.SameTarget(x, y)));
    }

    private sealed record ToolInvocationAdmission(AgentToolInvocation? Invocation, AgentError? Error, bool StopProvider = false);

    /// <summary>One foreground dispatch boundary shared by host-driven and delegated provider loops.</summary>
    private sealed class ProviderToolInvoker(IAgentToolInvoker inner, Func<bool> receiptOnly,
        Func<AgentToolInvocation, CancellationToken, Task<ToolInvocationAdmission>> beforeDispatch,
        Func<AgentToolInvocation, AgentToolResult, CancellationToken, Task<AgentError?>> admit, Action<AgentError> stop) : IAgentToolInvoker
    {
        // The SDK may request concurrent calls. Serialize their effect/admission transitions; this
        // turn-owned gate never creates a WaitHandle and is not disposed beneath an active callback.
        private readonly SemaphoreSlim _gate = new(1, 1);

        public async Task<AgentToolDispatch> DispatchAsync(AgentToolInvocation invocation, AgentPolicy policy, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (receiptOnly()) return AgentToolDispatch.Denied(new("agent.turn.receipt_only", "The observed effect is recorded; this continuation cannot acquire another tool operation.", 403));
                ToolInvocationAdmission current;
                try { current = await beforeDispatch(invocation, cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception) { current = new(null, new("agent.tool.admission_unavailable", "Current tool admission could not be established.", 503), true); }
                if (current.Error is { } refusal)
                {
                    if (!current.StopProvider) return AgentToolDispatch.Denied(refusal);
                    stop(refusal);
                    throw new OperationCanceledException("Provider disclosure stopped before tool dispatch.", cancellationToken);
                }
                invocation = current.Invocation!;
                var dispatch = await inner.DispatchAsync(invocation, policy, cancellationToken);
                if (dispatch.Outcome == AgentToolDispatchOutcome.Executed && dispatch.Result is { } result)
                {
                    AgentError? denied;
                    try { denied = await admit(invocation, result, cancellationToken); }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch (Exception) { denied = new("agent.result.disclosure_unavailable", "The observed outcome could not be admitted for provider disclosure. Its effect is retained and will not be repeated.", 503); }
                    if (denied is not null)
                    {
                        stop(denied);
                        throw new OperationCanceledException("Provider disclosure stopped; the observed effect remains recorded.", cancellationToken);
                    }
                }
                return dispatch;
            }
            finally { _gate.Release(); }
        }
    }

    private async Task<PreparedTurn> PrepareAsync(AgentSession session, CancellationToken cancellationToken)
    {
        var messages = await sessions.ListMessagesAsync(session.Id, cancellationToken);
        if (stateStore.Find(session.Id) is { } paused)
        {
            var source = messages.FirstOrDefault(x => x.Id == paused.SourceMessageId);
            if (source is null) return PreparedTurn.Fail(Error("agent.turn.source_missing", "The pending turn's source message is unavailable.", 409));
            if (!source.HasDisclosureProvenance)
                return Rejected(source, "agent.history.provenance_unavailable", "The retained turn predates complete disclosure provenance. Start a fresh session.", 409);
            // Reading the result also advances an expired dispatched client request to explicit
            // uncertainty. Do that before checking pending state, including on stream reconnect.
            var storedResult = await proposals.FindResultAsync(paused.ProposalId, cancellationToken);
            var proposal = await proposals.FindAsync(paused.ProposalId, cancellationToken);
            if (proposal is null || proposal.Status is AgentActionProposalStatus.AwaitingApproval or AgentActionProposalStatus.Approved or AgentActionProposalStatus.Executing or AgentActionProposalStatus.AwaitingClient)
                return PreparedTurn.Fail(Error("agent.turn.awaiting_review", "The original turn remains pending; resolve its proposal before continuing.", 409));
            var result = storedResult
                ?? new AgentToolResult(paused.ToolCallId, paused.ToolName, false, $"The reviewed invocation has status {proposal.Status}; no applied result is available.");
            if (storedResult is not null)
            {
                if (proposal.PreparedInvocation is not { } original || original.ToolName != storedResult.ToolName
                    || original.ToolCallId != storedResult.ToolCallId || original.ActorId != session.ActorId || original.TenantId != session.TenantId)
                    return Rejected(source, "agent.result.provenance_unavailable", "The retained result's original preparation cannot be established.", 409);
                result = AgentResultProvenance.Stamp(storedResult, original);
                try { await RecordToolResultAsync(session.Id, paused.Context, result); }
                catch (Exception)
                {
                    return Rejected(source, "agent.result.provenance_unavailable", "The observed receipt is retained, but its session provenance could not be recorded. Retry after storage is available.", 503) with { KeepPause = true };
                }
            }
            var results = paused.DisclosureResults.Append(result).ToArray();
            var historyError = await AuthorizeHistoryAsync(session, messages.Where(x => x.Id != source.Id), cancellationToken);
            if (historyError is not null) return Rejected(source, historyError) with { KeepPause = historyError.StatusCode >= 500 };
            var admission = await AdmitAsync(session, paused.Context, results, true, false, cancellationToken);
            if (!admission.Succeeded || admission.Value is null) return Rejected(source, admission.Error!) with { KeepPause = admission.Error!.StatusCode >= 500 };
            var history = Append(paused.History, new(AgentRole.Tool, JsonSerializer.Serialize(result, JsonOptions), result.ToolCallId, result.ToolName));
            return new(source, history, [result], 0, admission.Value.Attachments, true, null)
            { Diagnostics = admission.Value.Diagnostics, DisclosureResults = results };
        }
        var latest = messages.LastOrDefault(x => x.Role == AgentRole.User && x.Status == AgentMessageStatus.Pending);
        if (latest is null) return PreparedTurn.Fail(Error("agent.message.no_pending", "There is no new prompt or retained receipt continuation to run.", 409));
        if (!latest.HasDisclosureProvenance)
            return Rejected(latest, "agent.history.provenance_unavailable", "The current message store does not preserve complete disclosure provenance. Start a fresh supported session.", 409);
        var priorError = await AuthorizeHistoryAsync(session, messages.Where(x => x.Id != latest.Id), cancellationToken);
        if (priorError is not null) return Rejected(latest, priorError);
        var current = await AdmitAsync(session, latest.Context, [], false, false, cancellationToken);
        if (!current.Succeeded || current.Value is null) return Rejected(latest, current.Error!);
        var initial = messages.Where(x => x.Role is AgentRole.User or AgentRole.Assistant or AgentRole.System or AgentRole.Tool)
            .Select(x => x.Role == AgentRole.Tool && x.ToolResults.Count == 1
                ? new AgentTurnMessage(x.Role, JsonSerializer.Serialize(x.ToolResults[0], JsonOptions), x.ToolResults[0].ToolCallId, x.ToolResults[0].ToolName)
                : new AgentTurnMessage(x.Role, x.Content)).ToList();
        return new(latest, initial, [], 0, current.Value.Attachments, false, null) { Diagnostics = current.Value.Diagnostics };
    }

    private async Task<AgentError?> AuthorizeHistoryAsync(AgentSession session, IEnumerable<AgentMessage> messages, CancellationToken token)
    {
        var protectedHistory = false;
        foreach (var message in messages)
        {
            token.ThrowIfCancellationRequested();
            if (message.Role == AgentRole.User && !message.HasDisclosureProvenance
                || message.Role == AgentRole.Tool && message.ToolResults.Count == 0)
                return new("agent.history.provenance_unavailable", "Historical provider evidence has incomplete provenance. Start a fresh supported session.", 409);
            if (message.Role == AgentRole.Assistant && protectedHistory && !message.HasDisclosureProvenance && message.Context.Count == 0 && message.ToolResults.Count == 0)
                return new("agent.history.provenance_unavailable", "Historical resource-derived text has no disclosure provenance. Start a fresh session.", 409);
            if (message.Context.Count == 0 && message.ToolResults.Count == 0) continue;
            protectedHistory = true;
            var admitted = await AdmitAsync(session, message.Context, message.ToolResults, false, true, token);
            if (!admitted.Succeeded) return admitted.Error;
        }
        return null;
    }

    private async Task<AgentResult<AgentContextContribution>> AdmitAsync(AgentSession session,
        IReadOnlyCollection<AgentContextAttachment> context, IReadOnlyList<AgentToolResult> results,
        bool continuation, bool historical, CancellationToken token)
    {
        try
        {
            foreach (var result in results)
            {
                // Engine-created refusal text has no successful resource payload to disclose.
                if (!result.Succeeded && result.Payload is null && result.Receipt is null && result.Access is null) continue;
                var tool = await toolRegistry.FindAsync(result.ToolName, token);
                if (result.Access is not { } access || tool is null || access.ModuleId != tool.Descriptor.ModuleId
                    || access.ContractVersion != tool.Descriptor.ContractVersion
                    || string.IsNullOrEmpty(access.DisclosureSourceId) || access.DisclosureSourceId != tool.Descriptor.DisclosureSourceId
                    || string.IsNullOrEmpty(access.InvocationId)
                    || result.Receipt is { } receipt && (!AgentInvocationIntegrity.SameTarget(receipt.Before, access.Target)
                        || receipt.After.ModuleId != access.ModuleId || string.IsNullOrEmpty(receipt.After.ResourceType)
                        || receipt.InvocationId != access.InvocationId))
                    return AgentResult<AgentContextContribution>.Failure("agent.result.provenance_unavailable", "The original installed result owner or contract is unavailable.", 409);
                var authorized = await authorizer.AuthorizeAsync(tool.Descriptor, session.ActorId, session.TenantId, access.Target, token);
                var policy = await policyEvaluator.EvaluateCapabilityAsync(session.Policy, tool.Descriptor.Name, token);
                if (!authorized.Allowed || !policy.Allowed)
                    return AgentResult<AgentContextContribution>.Failure("agent.result.disclosure_denied", "Current authority or host policy withholds the retained outcome details. Its accepted effect remains recorded and will not be repeated.", 403);
            }

            return await contextCollector.CollectAsync(session.Policy, new(session.Id, string.Empty, new Dictionary<string, string>())
            { Disclosure = new(session.Id, context, results.Where(x => x.Access is not null || x.Succeeded || x.Payload is not null || x.Receipt is not null).ToArray(), continuation) { IsHistorical = historical } }, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return AgentResult<AgentContextContribution>.Failure("agent.context.disclosure_unavailable", "Current disclosure authority could not be established. The recorded outcome is retained; retry after authority is available.", 503);
        }
    }

    private static PreparedTurn Rejected(AgentMessage source, AgentError error)
        => new(source, [], [], 0, [], false, Error(error.Code, error.Message, error.StatusCode))
        { Diagnostics = [new("host", error.Code, error.Message, true)] };

    private static PreparedTurn Rejected(AgentMessage source, string code, string message, int status)
        => Rejected(source, new AgentError(code, message, status));

    private async Task<IReadOnlyCollection<IAgentTool>> GetAdmittedToolsAsync(AgentSession session, PreparedTurn prepared, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var tools = new List<IAgentTool>();
        if (prepared.IsContinuation) return tools;
        var scopes = prepared.Context.SelectMany(x => x.AgentScopes).ToHashSet(StringComparer.Ordinal);
        foreach (var tool in await toolRegistry.ListAsync(token))
        {
            var descriptor = tool.Descriptor;
            if (descriptor.AgentScopes.Count > 0 && !descriptor.AgentScopes.Any(scopes.Contains)) continue;
            if (!(await policyEvaluator.EvaluateCapabilityAsync(session.Policy, descriptor.Name, token)).Allowed) continue;
            var targets = prepared.Context.Where(x => x.ModuleId == descriptor.ModuleId && x.Target is not null)
                .Select(x => x.Target).Distinct().ToList();
            if (targets.Count == 0) targets.Add(null);
            foreach (var target in targets)
                if ((await AgentToolAccess.AuthorizeAsync(tool, authorizer, session.ActorId, session.TenantId, target, token)).Allowed)
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
        IReadOnlyList<AgentTurnMessage> history, IReadOnlyCollection<AgentContextAttachment> context, int step, IReadOnlyList<AgentToolResult> results)
        => stateStore.Save(new(sessionId, turnId, proposal.Id, proposal.PreparedInvocation?.ToolCallId ?? string.Empty,
            proposal.PreparedInvocation?.ToolName ?? proposal.CapabilityId, history, step)
        { SourceMessageId = source.Id, Context = context, Client = source.Client, DisclosureResults = results.ToArray() });

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
        catch (Exception error) when (error is JsonException or ArgumentException) { }
        arguments = new Dictionary<string, object?>();
        return false;
    }

    private static IReadOnlyList<AgentTurnMessage> Append(IReadOnlyList<AgentTurnMessage> history, AgentTurnMessage message) => [.. history, message];
    private static AgentStreamEvent Lifecycle(AgentStreamEventKind kind, object? payload, string? proposalId = null) => new(NewId(), kind, null, proposalId, null, DateTimeOffset.UtcNow, null, payload);
    private static AgentStreamEvent Error(string code, string message, int status) => new(NewId(), AgentStreamEventKind.Error, null, null, new(code, message, status), DateTimeOffset.UtcNow, AgentResultKind.Error);

    private sealed record PreparedTurn(AgentMessage? Source, IReadOnlyList<AgentTurnMessage> History,
        IReadOnlyList<AgentToolResult> PendingResults, int StartStep, IReadOnlyCollection<AgentContextAttachment> Context, bool IsContinuation, AgentStreamEvent? Error)
    {
        public bool KeepPause { get; init; }
        public IReadOnlyCollection<AgentContributionDiagnostic> Diagnostics { get; init; } = [];
        public IReadOnlyList<AgentToolResult> DisclosureResults { get; init; } = [];
        public static PreparedTurn Fail(AgentStreamEvent error) => new(null, [], [], 0, [], false, error);
    }
}
