using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.UnifiedExecution.WorkProof;

namespace Elsa.AgentCompositionProof;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(MemoryReference), "memory")]
[JsonDerivedType(typeof(KnowledgeReference), "knowledge")]
internal abstract record ResourceReference(
    string TenantId,
    string PersistenceScope,
    string Authority,
    string ResourceId,
    string Revision);

internal sealed record MemoryReference(
    string TenantId,
    string PersistenceScope,
    string Authority,
    string ResourceId,
    string Revision,
    MemoryVisibility Visibility = MemoryVisibility.ExternalShared,
    string? OwnerId = null,
    string? ConversationId = null,
    MemoryPermission Permission = MemoryPermission.Read,
    MemoryConsistency Consistency = MemoryConsistency.Snapshot)
    : ResourceReference(TenantId, PersistenceScope, Authority, ResourceId, Revision);

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum MemoryVisibility { Private, ConversationShared, ExternalShared }
[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum MemoryPermission { Read, ReadWrite }
[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum MemoryConsistency { Snapshot }

internal sealed record KnowledgeReference(
    string TenantId,
    string PersistenceScope,
    string Authority,
    string ResourceId,
    string Revision)
    : ResourceReference(TenantId, PersistenceScope, Authority, ResourceId, Revision);

internal sealed record ResolvedReferenceSnapshot(string Kind, string StableIdentity, string ContentSha256);

internal sealed class ResourceReferenceResolver
{
    private readonly Dictionary<string, string> _content = new(StringComparer.Ordinal)
    {
        ["memory|tenant-red|shared-persistence|agent-host|agent-context|r7"] = "private context alpha",
        ["memory|tenant-red|shared-persistence|agent-host|researcher-private|r2"] = "researcher only observation",
        ["memory|tenant-red|shared-persistence|agent-host|specialist-private|r2"] = "specialist only observation",
        ["memory|tenant-red|shared-persistence|agent-host|reviewer-private|r2"] = "reviewer only observation",
        ["memory|tenant-red|shared-persistence|agent-host|shared-conversation|r5"] = "shared conversation observation",
        ["knowledge|tenant-red|shared-persistence|agent-host|dataset-runbook|r3"] = "curated fact omega"
    };

    public ResolvedReferenceSnapshot ResolveMemory(ResourceReference reference, WorkScope scope, string? requestingAgentId = null, string? conversationId = null)
    {
        if (reference is not MemoryReference memory)
            throw new InvalidOperationException("Memory adapter received a non-memory resource reference.");
        if (memory.Permission != MemoryPermission.Read)
            throw new UnauthorizedAccessException("The deterministic adapter supports read-only memory references.");
        if (memory.Consistency != MemoryConsistency.Snapshot)
            throw new NotSupportedException("The deterministic adapter requires a versioned memory snapshot.");
        if (memory.Visibility == MemoryVisibility.Private && memory.OwnerId != requestingAgentId)
            throw new UnauthorizedAccessException("Private memory is visible only to its owner.");
        if (memory.Visibility == MemoryVisibility.ConversationShared && memory.ConversationId != conversationId)
            throw new UnauthorizedAccessException("Conversation-shared memory is visible only inside its bound conversation.");
        return Resolve(reference, scope, "memory", memory.Visibility == MemoryVisibility.ExternalShared
            ? "external-shared"
            : $"{memory.Visibility.ToString().ToLowerInvariant()}:{memory.OwnerId ?? memory.ConversationId}");
    }

    public ResolvedReferenceSnapshot ResolveKnowledge(ResourceReference reference, WorkScope scope)
    {
        if (reference is not KnowledgeReference)
            throw new InvalidOperationException("Knowledge adapter received a non-knowledge resource reference.");
        return Resolve(reference, scope, "knowledge", "versioned");
    }

    public IReadOnlyList<ResolvedReferenceSnapshot> ResolveAll(IReadOnlyList<ResourceReference> references, WorkScope scope,
        string? requestingAgentId = null, string? conversationId = null)
        => references.Select(reference => reference switch
        {
            MemoryReference => ResolveMemory(reference, scope, requestingAgentId, conversationId),
            KnowledgeReference => ResolveKnowledge(reference, scope),
            _ => throw new InvalidOperationException($"Unsupported resource reference type {reference.GetType().Name}.")
        }).ToArray();

    private ResolvedReferenceSnapshot Resolve(ResourceReference reference, WorkScope scope, string kind, string visibility)
    {
        if (reference.TenantId != scope.TenantId
            || reference.PersistenceScope != scope.PersistenceScope
            || reference.Authority != scope.Authority)
            throw new UnauthorizedAccessException("Resource reference belongs to a different execution scope.");

        string key = Key(kind, reference);
        if (!_content.TryGetValue(key, out string? content))
            throw new KeyNotFoundException($"{kind} resource '{reference.ResourceId}@{reference.Revision}' was not found.");

        string stableIdentity = $"{kind}:{reference.TenantId}:{reference.PersistenceScope}:{reference.Authority}:{visibility}:{reference.ResourceId}@{reference.Revision}";
        return new ResolvedReferenceSnapshot(kind, stableIdentity, Sha256(content));
    }

    private static string Key(string kind, ResourceReference reference)
        => $"{kind}|{reference.TenantId}|{reference.PersistenceScope}|{reference.Authority}|{reference.ResourceId}|{reference.Revision}";

    internal static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

/// <summary>Control routing fields and resource references are deliberately separate data.</summary>
internal sealed record AgentEnvelope(
    string ReceiverId,
    int RoundId,
    string InvocationId,
    string MessageId,
    string Payload,
    IReadOnlyList<ResourceReference> ResourceReferences,
    AgentEmission? Emission = null,
    string? ConversationId = null,
    string[]? ContextMessageIds = null,
    AgentConfigurationReference? Configuration = null)
{
    [JsonIgnore]
    public AgentRoute Route => new(ReceiverId, RoundId, InvocationId, MessageId);
}

internal sealed record AgentRoute(string ReceiverId, int RoundId, string InvocationId, string MessageId);

internal sealed record AgentEmission(string SourceId, AgentEnvelope Message);

internal sealed record AgentExecutionResult(
    string InvocationId,
    string ReceiverId,
    int RoundId,
    string MessageId,
    string OutputFingerprint,
    IReadOnlyList<ResolvedReferenceSnapshot> ResolvedResources,
    AgentEmission? Emission,
    AgentConfigurationReference? Configuration = null,
    UsageAccounting? Usage = null,
    string[]? ContextMessageIds = null);

internal sealed record AgentConfigurationReference(string AgentId, string PromptVersion, string ModelVersion, string ToolProfile);
internal sealed record UsageAccounting(int InputUnits, int OutputUnits, int TotalUnits, string AccountingKind);

internal sealed record MailboxStartInput(IReadOnlyList<AgentEnvelope> Messages);
internal sealed record RoundStartInput(IReadOnlyList<AgentEnvelope> Messages);
internal sealed record FanInStartInput(IReadOnlyList<AgentEnvelope> SourceWork);
internal sealed record FanInBufferedMessage(AgentEmission Emission, IReadOnlyList<ResolvedReferenceSnapshot> ResolvedResources, string MessageFingerprint);

/// <summary>A deterministic test adapter; it resolves scoped references without an LLM or network call.</summary>
internal sealed class DeterministicFakeAgentAdapter(ResourceReferenceResolver resolver)
{
    public AgentExecutionResult Execute(AgentEnvelope envelope, WorkScope scope)
    {
        IReadOnlyList<ResolvedReferenceSnapshot> resolved = resolver.ResolveAll(envelope.ResourceReferences, scope, envelope.ReceiverId, envelope.ConversationId);
        string material = string.Join("|", new[] { envelope.ReceiverId, envelope.RoundId.ToString(), envelope.MessageId, envelope.Payload }
            .Concat(resolved.Select(x => $"{x.Kind}:{x.StableIdentity}:{x.ContentSha256}")));
        return new AgentExecutionResult(envelope.InvocationId, envelope.ReceiverId, envelope.RoundId, envelope.MessageId,
            ResourceReferenceResolver.Sha256(material), resolved, envelope.Emission, envelope.Configuration,
            new UsageAccounting(envelope.Payload.Length, 1, 1, "deterministic-test-units"), envelope.ContextMessageIds);
    }
}

/// <summary>Candidate native mailbox semantics over the unchanged WorkProof work/result substrate.</summary>
internal sealed class ReceiverMailboxPolicy : ICompositionPolicy
{
    public string Id => "candidate-agent-mailbox-v1";

    public CompositionPlan Start(string inputJson)
    {
        AgentEnvelope[] messages = ProofJson.Deserialize<MailboxStartInput>(inputJson).Messages.ToArray();
        if (messages.Select(x => x.InvocationId).Distinct(StringComparer.Ordinal).Count() != messages.Length)
            throw new InvalidOperationException("Mailbox invocation identities must be unique.");

        if (messages.Length == 0) throw new InvalidOperationException("A mailbox cannot start without a message.");
        var queues = new Dictionary<string, AgentEnvelope[]>(StringComparer.Ordinal);
        var active = new Dictionary<string, AgentEnvelope>(StringComparer.Ordinal);
        var work = new List<WorkDefinition>();
        foreach (IGrouping<string, AgentEnvelope> receiver in messages.GroupBy(x => x.ReceiverId, StringComparer.Ordinal))
        {
            AgentEnvelope[] queue = receiver.ToArray();
            if (queue.Length == 0) continue;
            active.Add(queue[0].InvocationId, queue[0]);
            work.Add(Work(queue[0]));
            if (queue.Length > 1) queues.Add(receiver.Key, queue[1..]);
        }
        var state = new MailboxState(queues, active, [], false);
        return Plan(state, work);
    }

    public CompositionPlan ApplyResult(string stateJson, WorkReceipt receipt)
    {
        MailboxState state = ProofJson.Deserialize<MailboxState>(stateJson);
        if (state.ProcessedInvocationIds.Contains(receipt.InvocationId, StringComparer.Ordinal))
            return new CompositionPlan(stateJson, [], [], state.Completed);
        if (receipt.Status != "Succeeded") throw new InvalidOperationException("Mailbox will not advance on a non-success result.");
        AgentExecutionResult result = ProofJson.Deserialize<AgentExecutionResult>(receipt.ResultJson);
        if (!state.ActiveMessages.TryGetValue(receipt.InvocationId, out AgentEnvelope? expected)
            || !MatchesRoute(expected, result))
            throw new InvalidOperationException("Mailbox result does not match an active invocation.");

        var active = new Dictionary<string, AgentEnvelope>(state.ActiveMessages, StringComparer.Ordinal);
        active.Remove(receipt.InvocationId);
        string[] processed = state.ProcessedInvocationIds.Append(receipt.InvocationId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var queues = new Dictionary<string, AgentEnvelope[]>(state.Queues, StringComparer.Ordinal);
        var work = new List<WorkDefinition>();
        if (queues.TryGetValue(result.ReceiverId, out AgentEnvelope[]? queue))
        {
            AgentEnvelope next = queue[0];
            queues.Remove(result.ReceiverId);
            if (queue.Length > 1) queues[result.ReceiverId] = queue[1..];
            active.Add(next.InvocationId, next);
            work.Add(Work(next));
        }

        bool completed = active.Count == 0 && queues.Count == 0;
        return Plan(new MailboxState(queues, active, processed, completed), work);
    }

    public CompositionPlan Continue(string stateJson, string continuationId)
        => throw new InvalidOperationException($"Mailbox has no continuation '{continuationId}'.");

    private static CompositionPlan Plan(MailboxState state, IReadOnlyList<WorkDefinition> work)
        => new(ProofJson.Serialize(state), work, [], state.Completed);

    private static WorkDefinition Work(AgentEnvelope envelope)
        => new(envelope.InvocationId, AgentWorker.HandlerId, AgentWorker.HandlerVersion, ProofJson.Serialize(envelope));

    private static bool MatchesRoute(AgentEnvelope expected, AgentExecutionResult result)
        => result.InvocationId == expected.InvocationId
            && result.ReceiverId == expected.ReceiverId
            && result.RoundId == expected.RoundId
            && result.MessageId == expected.MessageId;

    private sealed record MailboxState(Dictionary<string, AgentEnvelope[]> Queues, Dictionary<string, AgentEnvelope> ActiveMessages,
        string[] ProcessedInvocationIds, bool Completed);
}

/// <summary>Candidate superstep policy: no next-round dispatch until every current-round receipt is accepted.</summary>
internal sealed class RoundBarrierPolicy : ICompositionPolicy
{
    public string Id => "candidate-agent-round-barrier-v1";

    public CompositionPlan Start(string inputJson)
    {
        AgentEnvelope[] messages = ProofJson.Deserialize<RoundStartInput>(inputJson).Messages.ToArray();
        if (messages.Length == 0 || messages.Select(x => x.RoundId).Distinct().Count() != 1)
            throw new InvalidOperationException("A round must begin with at least one message from the same round.");
        EnsureOneMessagePerReceiver(messages);
        var state = new RoundState(messages[0].RoundId, messages, [], [], false);
        return new CompositionPlan(ProofJson.Serialize(state), messages.Select(Work).ToArray(), [], false);
    }

    public CompositionPlan ApplyResult(string stateJson, WorkReceipt receipt)
    {
        RoundState state = ProofJson.Deserialize<RoundState>(stateJson);
        if (state.ProcessedInvocationIds.Contains(receipt.InvocationId, StringComparer.Ordinal))
            return new CompositionPlan(stateJson, [], [], state.Completed);
        if (receipt.Status != "Succeeded") throw new InvalidOperationException("Round cannot advance on a non-success result.");
        AgentExecutionResult result = ProofJson.Deserialize<AgentExecutionResult>(receipt.ResultJson);
        AgentEnvelope? expected = state.ActiveMessages.SingleOrDefault(x => x.InvocationId == receipt.InvocationId);
        if (expected is null || !MatchesRoute(expected, result))
            throw new InvalidOperationException("Round result does not match an active invocation.");

        AgentEnvelope[] active = state.ActiveMessages.Where(x => x.InvocationId != receipt.InvocationId).ToArray();
        AgentEnvelope[] nextRound = state.NextRoundMessages;
        if (result.Emission is { } emission)
        {
            if (emission.Message.RoundId != state.CurrentRound + 1)
                throw new InvalidOperationException("Emitted control message must target the next round.");
            nextRound = nextRound.Append(emission.Message).ToArray();
        }
        string[] processed = state.ProcessedInvocationIds.Append(receipt.InvocationId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        if (active.Length > 0)
            return new CompositionPlan(ProofJson.Serialize(state with { ActiveMessages = active, NextRoundMessages = nextRound, ProcessedInvocationIds = processed }), [], [], false);

        if (nextRound.Length == 0)
            return new CompositionPlan(ProofJson.Serialize(state with { ActiveMessages = [], NextRoundMessages = [], ProcessedInvocationIds = processed, Completed = true }), [], [], true);

        int nextRoundId = state.CurrentRound + 1;
        if (nextRound.Any(x => x.RoundId != nextRoundId)) throw new InvalidOperationException("Round transition contains a message for a different round.");
        EnsureOneMessagePerReceiver(nextRound);
        var advanced = state with
        {
            CurrentRound = nextRoundId,
            ActiveMessages = nextRound,
            NextRoundMessages = [],
            ProcessedInvocationIds = processed
        };
        return new CompositionPlan(ProofJson.Serialize(advanced), nextRound.Select(Work).ToArray(), [], false);
    }

    public CompositionPlan Continue(string stateJson, string continuationId)
        => throw new InvalidOperationException($"Round policy has no continuation '{continuationId}'.");

    private static WorkDefinition Work(AgentEnvelope envelope)
        => new(envelope.InvocationId, AgentWorker.HandlerId, AgentWorker.HandlerVersion, ProofJson.Serialize(envelope));

    private static bool MatchesRoute(AgentEnvelope expected, AgentExecutionResult result)
        => result.InvocationId == expected.InvocationId
            && result.ReceiverId == expected.ReceiverId
            && result.RoundId == expected.RoundId
            && result.MessageId == expected.MessageId;

    private static void EnsureOneMessagePerReceiver(IEnumerable<AgentEnvelope> messages)
    {
        if (messages.GroupBy(x => x.ReceiverId, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new InvalidOperationException("RoundBarrierPolicy allows at most one concurrent message per receiver in each round; use ReceiverMailboxPolicy for queued same-receiver work.");
    }

    internal sealed record RoundState(int CurrentRound, AgentEnvelope[] ActiveMessages, AgentEnvelope[] NextRoundMessages,
        string[] ProcessedInvocationIds, bool Completed);
}

/// <summary>Candidate durable fan-in. Distinct source messages buffer; exact duplicate message identities are ignored.</summary>
internal sealed class BufferedFanInPolicy : ICompositionPolicy
{
    public string Id => "candidate-agent-buffered-fanin-v1";

    public CompositionPlan Start(string inputJson)
    {
        AgentEnvelope[] sourceWork = ProofJson.Deserialize<FanInStartInput>(inputJson).SourceWork.ToArray();
        if (sourceWork.Length == 0) throw new InvalidOperationException("Fan-in requires at least one source invocation.");
        if (sourceWork.Select(x => x.InvocationId).Distinct(StringComparer.Ordinal).Count() != sourceWork.Length)
            throw new InvalidOperationException("Fan-in source work invocation identities must be unique.");
        string[] expectedSources = ["A", "B"];
        FanInExpectedSource[] expectedEmissions = sourceWork.Select(source =>
        {
            AgentEmission emission = source.Emission ?? throw new InvalidOperationException($"Fan-in source '{source.InvocationId}' must declare its expected emitted message.");
            if (!expectedSources.Contains(emission.SourceId, StringComparer.Ordinal))
                throw new InvalidOperationException($"Fan-in source '{source.InvocationId}' used unknown source id '{emission.SourceId}'.");
            if (source.RoundId != 0 || emission.Message.RoundId != 0 || emission.Message.ReceiverId != "fan-in")
                throw new InvalidOperationException($"Fan-in source '{source.InvocationId}' emitted to an unsupported receiver or round.");
            return new FanInExpectedSource(source.InvocationId, source.ReceiverId, source.RoundId, source.MessageId, emission);
        }).ToArray();
        var state = new FanInState(expectedSources, expectedEmissions, [], [], [], [], 0, false);
        return new CompositionPlan(ProofJson.Serialize(state), sourceWork.Select(Work).ToArray(), [], false);
    }

    public CompositionPlan ApplyResult(string stateJson, WorkReceipt receipt)
    {
        FanInState state = ProofJson.Deserialize<FanInState>(stateJson);
        if (state.ProcessedInvocationIds.Contains(receipt.InvocationId, StringComparer.Ordinal))
            return new CompositionPlan(stateJson, [], [], state.Joined);
        if (receipt.Status != "Succeeded") throw new InvalidOperationException("Fan-in will not buffer a non-success result.");
        AgentExecutionResult result = ProofJson.Deserialize<AgentExecutionResult>(receipt.ResultJson);
        FanInExpectedSource? expected = state.ExpectedEmissions.SingleOrDefault(x => x.InvocationId == receipt.InvocationId);
        if (expected is null || !MatchesRoute(expected, result) || result.Emission is not { } emission
            || emission.SourceId != expected.Emission.SourceId || !SameEnvelope(expected.Emission.Message, emission.Message))
            throw new InvalidOperationException("Fan-in result does not match its expected source, route, round, or emitted message.");

        FanInBufferedMessage[] buffered = state.BufferedMessages;
        string[] messageIds = state.SeenMessageIds;
        string[] sources = state.SeenSourceIds;
        int duplicateCount = state.DuplicateMessageDeliveries;
        if (messageIds.Contains(emission.Message.MessageId, StringComparer.Ordinal))
        {
            FanInBufferedMessage prior = buffered.Single(x => x.Emission.Message.MessageId == emission.Message.MessageId);
            string fingerprint = MessageFingerprint(emission.Message);
            if (prior.MessageFingerprint != fingerprint || prior.Emission.SourceId != emission.SourceId)
                throw new InvalidOperationException($"MessageId '{emission.Message.MessageId}' was redelivered with different source or content.");
            duplicateCount++;
        }
        else
        {
            buffered = buffered.Append(new FanInBufferedMessage(emission, result.ResolvedResources, MessageFingerprint(emission.Message))).ToArray();
            messageIds = messageIds.Append(emission.Message.MessageId).Order(StringComparer.Ordinal).ToArray();
            sources = sources.Append(emission.SourceId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        }

        string[] processed = state.ProcessedInvocationIds.Append(receipt.InvocationId).Order(StringComparer.Ordinal).ToArray();
        bool joined = state.ExpectedSources.All(x => sources.Contains(x, StringComparer.Ordinal));
        var next = state with
        {
            BufferedMessages = buffered,
            SeenMessageIds = messageIds,
            SeenSourceIds = sources,
            ProcessedInvocationIds = processed,
            DuplicateMessageDeliveries = duplicateCount,
            Joined = joined
        };
        return new CompositionPlan(ProofJson.Serialize(next), [], [], joined);
    }

    public CompositionPlan Continue(string stateJson, string continuationId)
        => throw new InvalidOperationException($"Fan-in has no continuation '{continuationId}'.");

    private static WorkDefinition Work(AgentEnvelope envelope)
        => new(envelope.InvocationId, AgentWorker.HandlerId, AgentWorker.HandlerVersion, ProofJson.Serialize(envelope));

    private static bool MatchesRoute(FanInExpectedSource expected, AgentExecutionResult result)
        => result.InvocationId == expected.InvocationId
            && result.ReceiverId == expected.ReceiverId
            && result.RoundId == expected.RoundId
            && result.MessageId == expected.MessageId;

    private static bool SameEnvelope(AgentEnvelope left, AgentEnvelope right)
        => left.ReceiverId == right.ReceiverId
            && left.RoundId == right.RoundId
            && left.InvocationId == right.InvocationId
            && left.MessageId == right.MessageId
            && left.Payload == right.Payload
            && left.ResourceReferences.SequenceEqual(right.ResourceReferences);

    private static string MessageFingerprint(AgentEnvelope envelope)
        => ResourceReferenceResolver.Sha256(ProofJson.Serialize(envelope));

    internal sealed record FanInExpectedSource(string InvocationId, string ReceiverId, int RoundId, string MessageId, AgentEmission Emission);
    internal sealed record FanInState(string[] ExpectedSources, FanInExpectedSource[] ExpectedEmissions, FanInBufferedMessage[] BufferedMessages, string[] SeenSourceIds,
        string[] SeenMessageIds, string[] ProcessedInvocationIds, int DuplicateMessageDeliveries, bool Joined);
}

internal static class ProofJson
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidDataException($"Could not deserialize {typeof(T).Name}.");
}

