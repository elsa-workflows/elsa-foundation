using System.Text;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.JsonSchema;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Elsa.Agent.Specialists.Services;

internal sealed record SpecialistTurnRequest(SpecialistInvocationIdentity Identity, SpecialistRuntimeDescriptor Descriptor, ActivityContract Contract, JsonElement Inputs, string BindingFingerprint);
internal sealed record SpecialistTurnResult(JsonElement Result, long? InputTokens, long? OutputTokens, long? TotalTokens, int ModelCalls, int ConnectionVersion);
internal sealed class SpecialistTurnException(string code, string message, Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

internal sealed class SpecialistModelRunner(IEnumerable<ISpecialistModelClientFactory> factories, SpecialistSchemaValidator schemas)
{
    public PreparedSpecialistSchema ValidateRequest(SpecialistTurnRequest request)
    {
        var descriptor = request.Descriptor;
        if (request.Contract.Inputs.Values.Any(x => x.Policy.RequiresEncryption || x.IsCredential) || request.Contract.Result.Policy.RequiresEncryption ||
            request.Contract.Result.Projections.Values.Any(x => x.Policy.RequiresEncryption))
            throw Failure("specialist.value.encryption", "This execution profile cannot recover native encryption-required business values.");
        if (descriptor.SchemaProfile != SpecialistRuntimeIds.SchemaProfile || descriptor.Limits is null ||
            descriptor.Limits.MaximumModelCalls is < 1 or > 32 || descriptor.Limits.TimeoutMilliseconds is < 1_000 or > 1_800_000 || descriptor.Limits.MaximumOutputTokens is <= 0)
            throw Failure("specialist.descriptor.invalid", "The specialist descriptor is outside the supported execution profile.");
        var inputSchema = schemas.Prepare(descriptor.InputSchema).Schema;
        var resultSchema = schemas.Prepare(descriptor.ResultSchema).Schema;
        if (inputSchema is null || resultSchema is null || inputSchema.Validate(request.Inputs).Count != 0 ||
            SpecialistWireTypes.Validate(request.Inputs, request.Contract.Inputs.Values.Select(x => new SpecialistJsonMember(x.Name, x.Type.Alias, x.Type.CollectionKind, x.IsRequired, x.IsNullable))).Count != 0)
            throw Failure("specialist.input.invalid", "The specialist input does not satisfy its published business contract.");
        return resultSchema;
    }

    public async ValueTask<SpecialistTurnResult> RunAsync(SpecialistTurnRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resultSchema = ValidateRequest(request);
        var descriptor = request.Descriptor;
        var owners = factories.Where(x => x.ProviderKey == descriptor.Model.ProviderKey).ToArray();
        if (owners.Length != 1)
            throw Failure("specialist.model.provider", "The selected model provider must have exactly one registered owner.");
        await using var lease = await owners[0].OpenAsync(new(descriptor, request.Identity), cancellationToken);
        if (lease.ConnectionVersion < 1 || lease.CapabilitiesFingerprint != descriptor.CapabilitiesFingerprint)
            throw Failure("specialist.model.changed", "The model connection or capabilities no longer satisfy the published specialist.");
        // A new SDK session for every fresh turn. Tool admission and continuity are separate explicit profiles.
        var options = lease.Options.Clone();
        options.ModelId = descriptor.Model.ModelId;
        options.ResponseFormat = ChatResponseFormat.ForJsonSchema(descriptor.ResultSchema, "specialist_result");
        options.MaxOutputTokens = descriptor.Limits.MaximumOutputTokens is { } published
            ? Math.Min(published, options.MaxOutputTokens ?? published) : options.MaxOutputTokens;
        options.Tools = null;
        options.ToolMode = ChatToolMode.None;
        options.Instructions = null;
        using var bounded = new BoundedClient(lease.Client, descriptor.Limits.MaximumModelCalls);
        var agent = new ChatClientAgent(bounded, instructions: descriptor.Instructions, name: "Specialist");
        var response = await agent.RunAsync(request.Inputs.GetRawText(), await agent.CreateSessionAsync(cancellationToken),
            new ChatClientAgentRunOptions(options), cancellationToken);
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
                request.Contract.Result.Projections.Values.Select(x => new SpecialistJsonMember(x.Path, x.Type.Alias, x.Type.CollectionKind, x.IsRequired,
                    // The schema owns nullability; the runtime projection contract has no nullable flag.
                    true))).Count != 0)
            throw Failure("specialist.result.invalid", "The model result does not satisfy its published business contract.");
        return new(result, response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount, response.Usage?.TotalTokenCount, bounded.Calls, lease.ConnectionVersion);
    }

    internal static SpecialistTurnException Failure(string code, string message, Exception? innerException = null) => new(code, message, innerException);

    private sealed class BoundedClient(IChatClient inner, int maximumCalls) : IChatClient
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);
        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref calls) > maximumCalls)
                throw Failure("specialist.budget.calls", "The specialist model-call budget is exhausted.");
            var response = await inner.GetResponseAsync(messages, options, cancellationToken);
            if (response.Messages.SelectMany(x => x.Contents).Any(x => x is not TextContent))
                throw Failure("specialist.result.content", "This specialist profile accepts text containing its JSON business result.");
            return response;
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw Failure("specialist.streaming.unavailable", "This specialist profile requires an atomic model response.");
        public object? GetService(Type serviceType, object? serviceKey = null) => serviceType == typeof(IChatClient) && serviceKey is null ? this : null;
        // The provider lease owns the underlying client; the SDK wrapper owns no additional resource.
        public void Dispose() { }
    }
}
