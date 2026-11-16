using System.Text;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.JsonSchema;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Elsa.Agent.Services;

internal sealed record AgentTurnRequest(AgentInvocationIdentity Identity, AgentRuntimeDescriptor Descriptor, ActivityContract Contract, JsonElement Inputs, string BindingFingerprint,
    AgentSessionReference? PreparedSession = null, AgentSessionTarget? SessionTarget = null)
{
    public AgentConversationAdmission? Continuation { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public AgentTurnLimits? EffectiveLimits { get; init; }
}
internal sealed record AgentTurnResult(JsonElement Result, long? InputTokens, long? OutputTokens, long? TotalTokens, int ModelCalls, int ConnectionVersion,
    AgentSessionReference? Session = null)
{
    public IReadOnlyList<AgentModelRequestReservation>? ModelRequests { get; init; }
    public AgentModelResponseObservation? ResponseObservation { get; init; }
    public bool IsOutputUsageKnown { get; init; }
    public long? AccountedOutputTokens { get; init; }
}
internal sealed class AgentTurnException(string code, string message, Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
    public bool OutcomeUnknown { get; init; }
}

internal sealed record AgentStreamingPolicy(bool Enabled);

internal sealed class AgentModelRunner(IEnumerable<IAgentModelClientFactory> factories, AgentSchemaValidator schemas, AgentSessionCodec sessions,
    AgentStreamingPolicy? streaming = null)
{
    public PreparedAgentSchema ValidateRequest(AgentTurnRequest request)
    {
        var descriptor = request.Descriptor;
        AgentNativeControls.ValidateContract(descriptor, request.Contract);
        if ((request.PreparedSession is not null || request.Continuation is not null) && !descriptor.AllowContinuity)
            throw Failure("agent.session.disallowed", "This published agent does not allow conversation continuity.");
        if (request.Continuation is { } continuation && (!AgentTurnDocumentRules.IsToken(continuation.ConversationId) || continuation.ExpectedRevision is < 1 or long.MaxValue))
            throw Failure("agent.session.admission", "Explicit continuation requires a conversation and a positive retained base revision.");
        if (request.Contract.Inputs.Values.Any(x => x.Policy.RequiresEncryption || x.IsCredential) || request.Contract.Result.Policy.RequiresEncryption ||
            request.Contract.Result.Projections.Values.Any(x => x.Policy.RequiresEncryption))
            throw Failure("agent.value.encryption", "This execution profile cannot recover native encryption-required business values.");
        if (descriptor.SchemaProfile != AgentRuntimeIds.SchemaProfile || descriptor.Limits is null ||
            descriptor.Limits.MaximumModelCalls is < 1 or > 32 || descriptor.Limits.TimeoutMilliseconds is < 1_000 or > 1_800_000 || descriptor.Limits.MaximumOutputTokens is <= 0)
            throw Failure("agent.descriptor.invalid", "The agent descriptor is outside the supported execution profile.");
        _ = AgentEffectiveLimits.For(request);
        var inputSchema = schemas.Prepare(descriptor.InputSchema).Schema;
        var resultSchema = schemas.Prepare(descriptor.ResultSchema).Schema;
        if (inputSchema is null || resultSchema is null || inputSchema.Validate(request.Inputs).Count != 0 ||
            AgentWireTypes.Validate(request.Inputs, request.Contract.Inputs.Values.Where(x => !AgentNativeControls.Enabled(descriptor) || x.Key != AgentInvocationControls.InputKey)
                .Select(x => new AgentJsonMember(x.Name, x.Type.Alias, x.Type.CollectionKind, x.IsRequired, x.IsNullable))).Count != 0)
            throw Failure("agent.input.invalid", "The agent input does not satisfy its published business contract.");
        return resultSchema;
    }

    public async ValueTask<AgentTurnResult> RunAsync(AgentTurnRequest request, CancellationToken cancellationToken, AgentTurnBudgetAuthority? authority = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resultSchema = ValidateRequest(request);
        var limits = AgentEffectiveLimits.For(request);
        var descriptor = request.Descriptor;
        // Entry validation precedes journal staging. Only the admitted worker requires this target;
        // a fresh continuity-enabled definition must still be able to enter its native checkpoint.
        if (descriptor.AllowContinuity && (request.SessionTarget is null || request.PreparedSession is null && request.SessionTarget.Revision != 1 || request.PreparedSession is { } previous &&
            (previous.ConversationId != request.SessionTarget.ConversationId || previous.Revision == long.MaxValue || previous.Revision + 1 != request.SessionTarget.Revision)))
            throw Failure("agent.session.admission", "Conversation continuity requires an exact admitted conversation revision.");
        var prior = request.PreparedSession is null ? Array.Empty<ChatMessage>() : sessions.Restore(request.PreparedSession, request.Identity);
        if (descriptor.AllowContinuity) sessions.EnsureCapacity(prior, request.Inputs.GetRawText(), request.Identity, request.SessionTarget!);
        var owners = factories.Where(x => x.ProviderKey == descriptor.Model.ProviderKey).ToArray();
        if (owners.Length != 1)
            throw Failure("agent.model.provider", "The selected model provider must have exactly one registered owner.");
        await using var lease = await owners[0].OpenAsync(new(descriptor, request.Identity) { DispatchAuthority = authority }, cancellationToken);
        if (lease.ConnectionVersion < 1 || lease.CapabilitiesFingerprint != descriptor.CapabilitiesFingerprint)
            throw Failure("agent.model.changed", "The model connection or capabilities no longer satisfy the published agent.");
        if (authority is not null && !lease.EnforcesDispatchAuthority)
            throw Failure("agent.budget.provider", "This model lease cannot enforce physical request reservations.");
        if (streaming?.Enabled == true && !lease.SupportsStreaming)
            throw Failure("agent.streaming.unavailable", "The selected model contribution has no qualified streaming profile.");
        // Every invocation gets a new SDK session. Only an explicitly admitted protected snapshot
        // restores earlier accepted messages; MAF session serialization is never persisted.
        var options = lease.Options.Clone();
        options.ModelId = descriptor.Model.ModelId;
        options.ResponseFormat = ChatResponseFormat.ForJsonSchema(descriptor.ResultSchema, "agent_result");
        options.MaxOutputTokens = limits.MaximumOutputTokens is { } published
            ? Math.Min(published, options.MaxOutputTokens ?? published) : options.MaxOutputTokens;
        authority?.BindOutputLimit(options.MaxOutputTokens);
        options.Tools = null;
        options.ToolMode = ChatToolMode.None;
        options.Instructions = null;
        using var bounded = new BoundedClient(lease.Client, limits.MaximumModelCalls, authority);
        var history = new InMemoryChatHistoryProvider(new InMemoryChatHistoryProviderOptions
        {
            StorageInputResponseMessageFilter = AgentSessionCodec.VisibleMessages
        });
        var agent = new ChatClientAgent(bounded, new ChatClientAgentOptions
        {
            Name = "Agent", ChatOptions = new() { Instructions = descriptor.Instructions },
            ChatHistoryProvider = history, UseProvidedChatClientAsIs = true, ThrowOnChatHistoryProviderConflict = true
        });
        var session = await agent.CreateSessionAsync(cancellationToken);
        if (prior.Count > 0) history.SetMessages(session, prior.ToList());
        var response = streaming?.Enabled == true
            ? await agent.RunStreamingAsync(request.Inputs.GetRawText(), session, new ChatClientAgentRunOptions(options), cancellationToken).ToAgentResponseAsync(cancellationToken)
            : await agent.RunAsync(request.Inputs.GetRawText(), session, new ChatClientAgentRunOptions(options), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (Encoding.UTF8.GetByteCount(response.Text) > AgentSchemaValidator.MaximumValueBytes)
            throw Failure("agent.result.bounds", "The model result exceeds the bounded JSON profile.");
        JsonElement result;
        try
        {
            using var document = JsonDocument.Parse(response.Text, new JsonDocumentOptions { MaxDepth = AgentSchemaValidator.MaximumDepth });
            result = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw Failure("agent.result.json", "The model result is not valid bounded JSON.", exception);
        }
        if (resultSchema.Validate(result).Count != 0 || AgentWireTypes.Validate(result,
                request.Contract.Result.Projections.Values.Where(x => !AgentNativeControls.Enabled(descriptor) || x.Key != AgentInvocationControls.OutputKey).Select(x => new AgentJsonMember(x.Path, x.Type.Alias, x.Type.CollectionKind, x.IsRequired,
                    // The schema owns nullability; the runtime projection contract has no nullable flag.
                    true))).Count != 0)
            throw Failure("agent.result.invalid", "The model result does not satisfy its published business contract.");
        // No candidate history escapes on refusal, invalid JSON/schema or cancellation. The journal
        // must accept this snapshot and business result in one transaction before either is reusable.
        if (authority is not null && authority.Reservations < 1)
            throw Failure("agent.budget.protocol", "The model provider returned a result without its required physical reservation.");
        var acceptedSession = descriptor.AllowContinuity ? sessions.Capture(history.GetMessages(session), request.Identity, request.SessionTarget!) : null;
        return new(result, response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount, response.Usage?.TotalTokenCount, authority?.Reservations ?? bounded.Calls, lease.ConnectionVersion, acceptedSession)
        {
            ModelRequests = authority?.ModelRequests, ResponseObservation = authority?.ResponseObservation,
            IsOutputUsageKnown = authority?.IsOutputUsageKnown ?? false, AccountedOutputTokens = authority?.AccountedOutputTokens
        };
    }

    internal static AgentTurnException Failure(string code, string message, Exception? innerException = null) => new(code, message, innerException);

    private sealed class BoundedClient(IChatClient inner, int maximumCalls, AgentTurnBudgetAuthority? authority) : IChatClient
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);
        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref calls) > maximumCalls)
                throw Failure("agent.budget.calls", "The agent model-call budget is exhausted.");
            var response = await inner.GetResponseAsync(messages, options, cancellationToken);
            if (authority is not null) await authority.CompleteResponseAsync(response.Usage, response.ModelId, cancellationToken);
            return AgentModelResponseFilter.VisibleResult(response);
        }
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref calls) > maximumCalls)
                throw Failure("agent.budget.calls", "The agent model-call budget is exhausted.");
            var filter = new AgentStreamingResponseFilter();
            await foreach (var update in inner.GetStreamingResponseAsync(messages, options, cancellationToken).WithCancellation(cancellationToken))
                yield return filter.Filter(update);
            cancellationToken.ThrowIfCancellationRequested();
            filter.Complete();
            if (authority is not null) await authority.CompleteResponseAsync(filter.Usage, filter.ModelId, cancellationToken);
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => serviceType == typeof(IChatClient) && serviceKey is null ? this : null;
        // The provider lease owns the underlying client; the SDK wrapper owns no additional resource.
        public void Dispose() { }
    }
}
