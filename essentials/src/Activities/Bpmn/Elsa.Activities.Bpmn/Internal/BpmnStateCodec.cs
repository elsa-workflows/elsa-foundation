using System.Text.Json;
using System.Text.Json.Nodes;
using Bpmn.Model.State;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>
/// Maps the host's versioned wire document to library state. Reading version 1 never restamps it;
/// its writer retains activeChildren and producingActivityExecutionId. Semantic pruning is elsewhere.
/// </summary>
internal sealed class BpmnStateCodec
{
    internal const string StateAlias = "Elsa.Bpmn.ExecutionState";
    internal const int CurrentVersion = 2;
    private const string InvocationKey = "invocationCorrelation";
    private static readonly JsonSerializerOptions Web = new(Elsa.Serialization.Core.JsonBaseline.Options);

    public BpmnStateDocument Read(ActivityPrivateState? document, IReadOnlyDictionary<string, string>? invocation = null)
    {
        if (document is null)
            return new(new BpmnExecutionState(), CurrentVersion, invocation ?? new Dictionary<string, string>());
        if (document.StateVersion is not (1 or CurrentVersion) ||
            document.Value.Type.Alias != StateAlias ||
            document.Value.Type.SchemaVersion != document.StateVersion ||
            document.Value.InlineValue is not { } payload)
            throw new BpmnHostException("BPMN private state does not match a supported alias and schema version.");

        try
        {
            var shape = JsonNode.Parse(payload.GetRawText())?.AsObject()
                ?? throw new JsonException("BPMN state is null.");
            BpmnPersistedStateShape.Validate(shape, document.StateVersion);
            // Shape validation can inspect a DOM, but the persisted values come from the original JSON.
            var root = Properties(payload);
            var correlation = root.TryGetValue(InvocationKey, out var invocationValue)
                ? invocationValue.Deserialize<Dictionary<string, string>>(Web) ?? new Dictionary<string, string>()
                : new Dictionary<string, string>();
            root.Remove(InvocationKey);
            if (document.StateVersion == 1)
            {
                if (root["activeChildren"].ValueKind != JsonValueKind.Array)
                    throw new JsonException("Legacy BPMN active-child state is missing.");
                Rename(root, "activeChildren", "activeWork");
                RenameTokenMembers(root, "producingActivityExecutionId", "producingWorkHandle");
            }
            var state = JsonSerializer.SerializeToElement(root, Web).Deserialize<BpmnExecutionState>(Web)
                ?? throw new JsonException("BPMN state is null.");
            if (state.Tokens.Any(token => token is null) || state.ActiveWork.Any(work => work is null) || state.Diagnostics.Any(entry => entry is null) ||
                state.Tokens.Select(token => token.TokenId).Distinct(StringComparer.Ordinal).Count() != state.Tokens.Count ||
                state.ActiveWork.Select(work => work.TokenId).Distinct(StringComparer.Ordinal).Count() != state.ActiveWork.Count ||
                state.ActiveWork.Any(work => !state.Tokens.Any(token => token.TokenId == work.TokenId)))
                throw new JsonException("BPMN state work-token identities are inconsistent.");
            BpmnPersistedStateShape.ValidateRetainedCompensation(state);
            return new(state, document.StateVersion, correlation);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            throw new BpmnHostException("BPMN private state is invalid; its original document was not replaced.", exception);
        }
    }

    public ValueEnvelope Write(BpmnStateDocument document, BpmnExecutionState state)
    {
        try
        {
            var root = Properties(JsonSerializer.SerializeToElement(state, Web));
            if (document.Version == 1)
            {
                Rename(root, "activeWork", "activeChildren");
                RenameTokenMembers(root, "producingWorkHandle", "producingActivityExecutionId");
            }
            root[InvocationKey] = JsonSerializer.SerializeToElement(document.InvocationCorrelation, Web);
            return ValueEnvelope.Inline(new ValueTypeDescriptor(StateAlias, schemaVersion: document.Version),
                JsonSerializer.SerializeToElement(root, Web), ValueProtectionPolicy.InstanceInline);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            throw new BpmnHostException("BPMN private state could not be staged.", exception);
        }
    }

    private static Dictionary<string, JsonElement> Properties(JsonElement value) => value.EnumerateObject()
        .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);

    private static void RenameTokenMembers(Dictionary<string, JsonElement> root, string from, string to)
    {
        var tokens = root["tokens"].EnumerateArray().Select(token =>
        {
            var properties = Properties(token);
            Rename(properties, from, to);
            return JsonSerializer.SerializeToElement(properties, Web);
        }).ToArray();
        root["tokens"] = JsonSerializer.SerializeToElement(tokens, Web);
    }

    private static void Rename(Dictionary<string, JsonElement> root, string from, string to)
    {
        if (!root.TryGetValue(from, out var value))
            return;
        if (root.ContainsKey(to))
            throw new JsonException($"BPMN state contains conflicting '{from}' and '{to}' fields.");
        root.Remove(from);
        root[to] = value;
    }
}

internal sealed record BpmnStateDocument(
    BpmnExecutionState State,
    int Version,
    IReadOnlyDictionary<string, string> InvocationCorrelation);
