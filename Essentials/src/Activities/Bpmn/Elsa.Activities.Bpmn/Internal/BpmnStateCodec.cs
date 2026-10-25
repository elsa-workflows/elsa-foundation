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
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

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
            var root = JsonNode.Parse(payload.GetRawText())?.AsObject()
                ?? throw new JsonException("BPMN state is null.");
            // JsonConstructor defaults are useful for a new library state, but a persisted checkpoint
            // must not become a fresh empty execution merely because its payload was truncated.
            foreach (var key in new[] { "tokens", "diagnostics" })
                if (root[key] is not JsonArray)
                    throw new JsonException($"BPMN state collection '{key}' is missing.");
            if (root["sequence"] is not JsonValue sequence || !sequence.TryGetValue<int>(out var next) || next < 0 ||
                root["terminated"] is not JsonValue terminated || !terminated.TryGetValue<bool>(out _))
                throw new JsonException("BPMN state sequence or termination flag is invalid.");
            if (document.StateVersion == CurrentVersion &&
                (root["activeWork"] is not JsonArray || root[InvocationKey] is not JsonObject))
                throw new JsonException("BPMN state work or scope correlation is missing.");
            var correlation = root[InvocationKey]?.Deserialize<Dictionary<string, string>>(Web)
                ?? new Dictionary<string, string>();
            root.Remove(InvocationKey);
            if (document.StateVersion == 1)
            {
                if (root["activeChildren"] is not JsonArray)
                    throw new JsonException("Legacy BPMN active-child state is missing.");
                Rename(root, "activeChildren", "activeWork");
                foreach (var token in root["tokens"]?.AsArray() ?? [])
                    if (token is JsonObject entry)
                        Rename(entry, "producingActivityExecutionId", "producingWorkHandle");
            }
            var state = root.Deserialize<BpmnExecutionState>(Web) ?? throw new JsonException("BPMN state is null.");
            if (state.Tokens.Any(token => token is null) || state.ActiveWork.Any(work => work is null) || state.Diagnostics.Any(entry => entry is null) ||
                state.Tokens.Select(token => token.TokenId).Distinct(StringComparer.Ordinal).Count() != state.Tokens.Count ||
                state.ActiveWork.Select(work => work.TokenId).Distinct(StringComparer.Ordinal).Count() != state.ActiveWork.Count ||
                state.ActiveWork.Any(work => !state.Tokens.Any(token => token.TokenId == work.TokenId)))
                throw new JsonException("BPMN state work-token identities are inconsistent.");
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
            var root = JsonSerializer.SerializeToNode(state, Web)!.AsObject();
            if (document.Version == 1)
            {
                Rename(root, "activeWork", "activeChildren");
                foreach (var token in root["tokens"]!.AsArray())
                    Rename(token!.AsObject(), "producingWorkHandle", "producingActivityExecutionId");
            }
            root[InvocationKey] = JsonSerializer.SerializeToNode(document.InvocationCorrelation, Web);
            return ValueEnvelope.Inline(new ValueTypeDescriptor(StateAlias, schemaVersion: document.Version),
                JsonSerializer.SerializeToElement(root, Web), ValueProtectionPolicy.InstanceInline);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            throw new BpmnHostException("BPMN private state could not be staged.", exception);
        }
    }

    private static void Rename(JsonObject root, string from, string to)
    {
        if (!root.TryGetPropertyValue(from, out var value))
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
