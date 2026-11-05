using System.Text;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.JsonSchema;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Elsa.Agent.Specialists.Services;

internal sealed record SpecialistTurnRequest(SpecialistInvocationIdentity Identity, SpecialistRuntimeDescriptor Descriptor, ActivityContract Contract, JsonElement Inputs, string BindingFingerprint,
    SpecialistSessionReference? PreparedSession = null, SpecialistSessionTarget? SessionTarget = null)
{
    public SpecialistConversationAdmission? Continuation { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SpecialistTurnLimits? EffectiveLimits { get; init; }
}
internal sealed record SpecialistTurnResult(JsonElement Result, long? InputTokens, long? OutputTokens, long? TotalTokens, int ModelCalls, int ConnectionVersion,
    SpecialistSessionReference? Session = null)
{
    public IReadOnlyList<SpecialistModelRequestReservation>? ModelRequests { get; init; }
    public SpecialistModelResponseObservation? ResponseObservation { get; init; }
    public bool IsOutputUsageKnown { get; init; }
    public long? AccountedOutputTokens { get; init; }
}
internal sealed class SpecialistTurnException(string code, string message, Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
    public bool OutcomeUnknown { get; init; }
}

internal sealed record SpecialistStreamingPolicy(bool Enabled);

internal sealed class SpecialistModelRunner(IEnumerable<ISpecialistModelClientFactory> factories, SpecialistSchemaValidator schemas, SpecialistSessionCodec sessions,
    SpecialistStreamingPolicy? streaming = null)
{
    public PreparedSpecialistSchema ValidateRequest(SpecialistTurnRequest request)
    {
        var descriptor = request.Descriptor;
        SpecialistNativeControls.ValidateContract(descriptor, request.Contract);
        if ((request.PreparedSession is not null || request.Continuation is not null) && !descriptor.AllowContinuity)
            throw Failure("specialist.session.disallowed", "This published specialist does not allow conversation continuity.");
        if (request.Continuation is { } continuation && (!SpecialistTurnDocumentRules.IsToken(continuation.ConversationId) || continuation.ExpectedRevision is < 1 or long.MaxValue))
            throw Failure("specialist.session.admission", "Explicit continuation requires a conversation and a positive retained base revision.");
        if (request.Contract.Inputs.Values.Any(x => x.Policy.RequiresEncryption || x.IsCredential) || request.Contract.Result.Policy.RequiresEncryption ||
            request.Contract.Result.Projections.Values.Any(x => x.Policy.RequiresEncryption))
            throw Failure("specialist.value.encryption", "This execution profile cannot recover native encryption-required business values.");
        if (descriptor.SchemaProfile != SpecialistRuntimeIds.SchemaProfile || descriptor.Limits is null ||
            descriptor.Limits.MaximumModelCalls is < 1 or > 32 || descriptor.Limits.TimeoutMilliseconds is < 1_000 or > 1_800_000 || descriptor.Limits.MaximumOutputTokens is <= 0)
            throw Failure("specialist.descriptor.invalid", "The specialist descriptor is outside the supported execution profile.");
        _ = SpecialistEffectiveLimits.For(request);
        var inputSchema = schemas.Prepare(descriptor.InputSchema).Schema;
        var resultSchema = schemas.Prepare(descriptor.ResultSchema).Schema;
        if (inputSchema is null || resultSchema is null || inputSchema.Validate(request.Inputs).Count != 0 ||
            SpecialistWireTypes.Validate(request.Inputs, request.Contract.Inputs.Values.Where(x => !SpecialistNativeControls.Enabled(descriptor) || x.Key != SpecialistInvocationControls.InputKey)
                .Select(x => new SpecialistJsonMember(x.Name, x.Type.Alias, x.Type.CollectionKind, x.IsRequired, x.IsNullable))).Count != 0)
            throw Failure("specialist.input.invalid", "The specialist input does not satisfy its published business contract.");
        return resultSchema;
    }

    public async ValueTask<SpecialistTurnResult> RunAsync(SpecialistTurnRequest request, CancellationToken cancellationToken, SpecialistTurnBudgetAuthority? authority = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resultSchema = ValidateRequest(request);
        var limits = SpecialistEffectiveLimits.For(request);
        var descriptor = request.Descriptor;
        // Entry validation precedes journal staging. Only the admitted worker requires this target;
        // a fresh continuity-enabled definition must still be able to enter its native checkpoint.
        if (descriptor.AllowContinuity && (request.SessionTarget is null || request.PreparedSession is null && request.SessionTarget.Revision != 1 || request.PreparedSession is { } previous &&
            (previous.ConversationId != request.SessionTarget.ConversationId || previous.Revision == long.MaxValue || previous.Revision + 1 != request.SessionTarget.Revision)))
            throw Failure("specialist.session.admission", "Conversation continuity requires an exact admitted conversation revision.");
        var prior = request.PreparedSession is null ? Array.Empty<ChatMessage>() : sessions.Restore(request.PreparedSession, request.Identity);
        if (descriptor.AllowContinuity) sessions.EnsureCapacity(prior, request.Inputs.GetRawText(), request.Identity, request.SessionTarget!);
        var owners = factories.Where(x => x.ProviderKey == descriptor.Model.ProviderKey).ToArray();
        if (owners.Length != 1)
            throw Failure("specialist.model.provider", "The selected model provider must have exactly one registered owner.");
        await using var lease = await owners[0].OpenAsync(new(descriptor, request.Identity) { DispatchAuthority = authority }, cancellationToken);
        if (lease.ConnectionVersion < 1 || lease.CapabilitiesFingerprint != descriptor.CapabilitiesFingerprint)
            throw Failure("specialist.model.changed", "The model connection or capabilities no longer satisfy the published specialist.");
        if (authority is not null && !lease.EnforcesDispatchAuthority)
            throw Failure("specialist.budget.provider", "This model lease cannot enforce physical request reservations.");
        if (streaming?.Enabled == true && !lease.SupportsStreaming)
            throw Failure("specialist.streaming.unavailable", "The selected model contribution has no qualified streaming profile.");
        // Every invocation gets a new SDK session. Only an explicitly admitted protected snapshot
        // restores earlier accepted messages; MAF session serialization is never persisted.
        var options = lease.Options.Clone();
        options.ModelId = descriptor.Model.ModelId;
        options.ResponseFormat = ChatResponseFormat.ForJsonSchema(descriptor.ResultSchema, "specialist_result");
        options.MaxOutputTokens = limits.MaximumOutputTokens is { } published
            ? Math.Min(published, options.MaxOutputTokens ?? published) : options.MaxOutputTokens;
        authority?.BindOutputLimit(options.MaxOutputTokens);
        options.Tools = null;
        options.ToolMode = ChatToolMode.None;
        options.Instructions = null;
        using var bounded = new BoundedClient(lease.Client, limits.MaximumModelCalls, authority);
        var history = new InMemoryChatHistoryProvider(new InMemoryChatHistoryProviderOptions
        {
            StorageInputResponseMessageFilter = SpecialistSessionCodec.VisibleMessages
        });
        var agent = new ChatClientAgent(bounded, new ChatClientAgentOptions
        {
            Name = "Specialist", ChatOptions = new() { Instructions = descriptor.Instructions },
            ChatHistoryProvider = history, UseProvidedChatClientAsIs = true, ThrowOnChatHistoryProviderConflict = true
        });
        var session = await agent.CreateSessionAsync(cancellationToken);
        if (prior.Count > 0) history.SetMessages(session, prior.ToList());
        var response = streaming?.Enabled == true
            ? await agent.RunStreamingAsync(request.Inputs.GetRawText(), session, new ChatClientAgentRunOptions(options), cancellationToken).ToAgentResponseAsync(cancellationToken)
            : await agent.RunAsync(request.Inputs.GetRawText(), session, new ChatClientAgentRunOptions(options), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (Encoding.UTF8.GetByteCount(response.Text) > SpecialistSchemaValidator.MaximumValueBytes)
            throw Failure("specialist.result.bounds", "The model result exceeds the bounded JSON profile.");
        JsonElement result;
        try
        {
            using var document = JsonDocument.Parse(response.Text, new JsonDocumentOptions { MaxDepth = SpecialistSchemaValidator.MaximumDepth });
            result = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw Failure("specialist.result.json", "The model result is not valid bounded JSON.", exception);
        }
        if (resultSchema.Validate(result).Count != 0 || SpecialistWireTypes.Validate(result,
                request.Contract.Result.Projections.Values.Where(x => !SpecialistNativeControls.Enabled(descriptor) || x.Key != SpecialistInvocationControls.OutputKey).Select(x => new SpecialistJsonMember(x.Path, x.Type.Alias, x.Type.CollectionKind, x.IsRequired,
                    // The schema owns nullability; the runtime projection contract has no nullable flag.
                    true))).Count != 0)
            throw Failure("specialist.result.invalid", "The model result does not satisfy its published business contract.");
        // No candidate history escapes on refusal, invalid JSON/schema or cancellation. The journal
        // must accept this snapshot and business result in one transaction before either is reusable.
        if (authority is not null && authority.Reservations < 1)
            throw Failure("specialist.budget.protocol", "The model provider returned a result without its required physical reservation.");
        var acceptedSession = descriptor.AllowContinuity ? sessions.Capture(history.GetMessages(session), request.Identity, request.SessionTarget!) : null;
        return new(result, response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount, response.Usage?.TotalTokenCount, authority?.Reservations ?? bounded.Calls, lease.ConnectionVersion, acceptedSession)
        {
            ModelRequests = authority?.ModelRequests, ResponseObservation = authority?.ResponseObservation,
            IsOutputUsageKnown = authority?.IsOutputUsageKnown ?? false, AccountedOutputTokens = authority?.AccountedOutputTokens
        };
    }

    internal static SpecialistTurnException Failure(string code, string message, Exception? innerException = null) => new(code, message, innerException);

    private sealed class BoundedClient(IChatClient inner, int maximumCalls, SpecialistTurnBudgetAuthority? authority) : IChatClient
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);
        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref calls) > maximumCalls)
                throw Failure("specialist.budget.calls", "The specialist model-call budget is exhausted.");
            var response = await inner.GetResponseAsync(messages, options, cancellationToken);
            if (authority is not null) await authority.CompleteResponseAsync(response.Usage, response.ModelId, cancellationToken);
            return SpecialistModelResponseFilter.VisibleResult(response);
        }
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref calls) > maximumCalls)
                throw Failure("specialist.budget.calls", "The specialist model-call budget is exhausted.");
            var filter = new SpecialistStreamingResponseFilter();
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
