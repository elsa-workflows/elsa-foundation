using System.Text.Json;
using System.Text.Json.Nodes;
using Bpmn.Model.State;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Checks qualified persisted writer layouts before library constructor defaults can erase responsibility.</summary>
internal static class BpmnPersistedStateShape
{
    public static void Validate(JsonObject root, int version)
    {
        Members(root);
        var workKey = version == 1 ? "activeChildren" : "activeWork";
        foreach (var key in new[] { "tokens", workKey, "diagnostics", "races", "loops", "compensables", "compensationRuns" })
            _ = Array(root, key);
        Integer(root, "sequence");
        Boolean(root, "terminated");
        Boolean(root, "cancelling");
        var pendingFault = Present(root, "pendingFault");
        if (pendingFault is not null)
        {
            var fault = Record(pendingFault);
            String(fault, "faultCode");
            String(fault, "message");
        }
        if (version == 2 || root.ContainsKey("invocationCorrelation"))
            StringMap(Object(Present(root, "invocationCorrelation")));

        foreach (var value in Array(root, "tokens"))
        {
            var token = Record(value);
            Text(token, "tokenId");
            Text(token, "atElementId");
            foreach (var key in new[] { "flowId", "parentTokenId", "iterationKey",
                         version == 1 ? "producingActivityExecutionId" : "producingWorkHandle" })
                NullableText(token, key);
            Enumeration<BpmnTokenStatus>(token, "status");
            Enumeration<BpmnTokenKind>(token, "kind", nullable: true);
            // The original v1 writer predates stoppedHandle. Continued v1 and fresh v2 write it.
            if (version == 2 || token.ContainsKey("stoppedHandle")) NullableText(token, "stoppedHandle");
        }
        foreach (var value in Array(root, workKey))
        {
            var work = Record(value);
            foreach (var key in new[] { "nodeId", "elementId", "tokenId", "schedulingCause" }) Text(work, key);
            NullableText(work, "iterationId");
        }
        foreach (var value in Array(root, "diagnostics"))
        {
            var diagnostic = Record(value);
            Text(diagnostic, "diagnosticId");
            Text(diagnostic, "message");
            Enumeration<BpmnDiagnosticKind>(diagnostic, "kind");
            foreach (var key in new[] { "elementId", "flowId", "tokenId" }) NullableText(diagnostic, key);
            StringMap(Object(Present(diagnostic, "details")));
        }
        foreach (var value in Array(root, "races"))
        {
            var race = Record(value);
            Text(race, "raceId");
            Text(race, "gatewayElementId");
            Identifiers(race, "memberTokenIds");
            Boolean(race, "resolved");
        }
        foreach (var value in Array(root, "loops"))
        {
            var loop = Record(value);
            foreach (var key in new[] { "loopId", "tokenId", "elementId" }) Text(loop, key);
            Boolean(loop, "isSequential");
            foreach (var key in new[] { "totalCount", "nextIndex", "completedCount" }) Integer(loop, key);
            if (Present(loop, "items") is { } items && items is not JsonArray)
                throw Invalid("Loop items must be an explicit array or null.");
            // Null inside a snapshotted JSON collection is a value, not a missing state record.
        }
        foreach (var value in Array(root, "compensables"))
        {
            var compensable = Record(value);
            foreach (var key in new[] { "compensableId", "hostElementId", "handlerElementId" }) Text(compensable, key);
            Enumeration<BpmnCompensableStatus>(compensable, "status");
        }
        foreach (var value in Array(root, "compensationRuns"))
        {
            var run = Record(value);
            Text(run, "runId");
            Text(run, "throwTokenId");
            Identifiers(run, "pendingCompensableIds");
        }
        foreach (var (collection, key) in new[] { ("tokens", "tokenId"), ("diagnostics", "diagnosticId"),
                     ("races", "raceId"), ("loops", "loopId"), ("compensables", "compensableId"), ("compensationRuns", "runId") })
        {
            var ids = Array(root, collection).Select(value => Object(value)[key]!.GetValue<string>()).ToArray();
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
                throw Invalid($"Persisted {collection} identities are duplicated.");
        }
    }

    public static void ValidateRetainedCompensation(BpmnExecutionState state)
    {
        var registrations = state.Compensables.ToDictionary(item => item.CompensableId, StringComparer.Ordinal);
        foreach (var run in state.CompensationRuns)
        {
            if (!state.Tokens.Any(token => token.TokenId == run.ThrowTokenId) ||
                run.PendingCompensableIds.Any(id => !registrations.TryGetValue(id, out var item) || item.Status != BpmnCompensableStatus.Claimed))
                throw Invalid("Persisted compensation responsibility has no exact claimed registration or coordinator.");
            if (state.PendingFault is not null || run.PendingCompensableIds.Count == 0) continue;
            var head = registrations[run.PendingCompensableIds[0]];
            if (state.Tokens.Count(token => token.ParentTokenId == run.ThrowTokenId && token.AtElementId == head.HandlerElementId &&
                    token.Status is not (BpmnTokenStatus.Consumed or BpmnTokenStatus.Canceled)) != 1)
                throw Invalid("The retained compensation head does not match its original live handler token.");
        }
        // Resolved race members, ordinary ancestors and original compensated hosts may legitimately be pruned.
        // Do not reconstruct their identities or demand that historical references still have live tokens.
    }

    private static JsonException Invalid(string message) => new(message);
    private static JsonNode? Present(JsonObject owner, string key) => owner.TryGetPropertyValue(key, out var value)
        ? value : throw Invalid($"Persisted BPMN member '{key}' is missing.");
    private static JsonObject Object(JsonNode? value) => value as JsonObject ?? throw Invalid("A persisted BPMN record is null or not an object.");
    private static JsonObject Record(JsonNode? value)
    {
        var record = Object(value);
        Members(record);
        return record;
    }
    private static void Members(JsonObject record)
    {
        // Web deserialization binds record members without regard to case. A second spelling must
        // not override the member whose presence/kind was validated. Opaque maps/items retain exact casing.
        if (record.Select(entry => entry.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != record.Count)
            throw Invalid("Persisted BPMN record members have conflicting case-insensitive names.");
    }
    private static JsonArray Array(JsonObject owner, string key) => Present(owner, key) as JsonArray
        ?? throw Invalid($"Persisted BPMN collection '{key}' is null or not an array.");
    private static void Text(JsonObject owner, string key)
    {
        if (Present(owner, key) is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))
            throw Invalid($"Persisted BPMN identity/text '{key}' is invalid.");
    }
    private static void NullableText(JsonObject owner, string key)
    {
        if (Present(owner, key) is { } value && (value is not JsonValue scalar || !scalar.TryGetValue<string>(out _)))
            throw Invalid($"Persisted BPMN member '{key}' must be a string or explicit null.");
    }
    private static void String(JsonObject owner, string key)
    {
        if (Present(owner, key) is not JsonValue value || !value.TryGetValue<string>(out _))
            throw Invalid($"Persisted BPMN member '{key}' must be a string.");
    }
    private static void Boolean(JsonObject owner, string key)
    {
        if (Present(owner, key) is not JsonValue value || !value.TryGetValue<bool>(out _))
            throw Invalid($"Persisted BPMN flag '{key}' is invalid.");
    }
    private static void Integer(JsonObject owner, string key)
    {
        if (Present(owner, key) is not JsonValue value || !value.TryGetValue<int>(out var number) || number < 0)
            throw Invalid($"Persisted BPMN counter '{key}' is invalid.");
    }
    private static void Enumeration<T>(JsonObject owner, string key, bool nullable = false) where T : struct, Enum
    {
        var node = Present(owner, key);
        if (node is null && nullable) return;
        if (node is not JsonValue value || !value.TryGetValue<int>(out var number) || !Enum.IsDefined(typeof(T), number))
            throw Invalid($"Persisted BPMN enum '{key}' is invalid.");
    }
    private static void Identifiers(JsonObject owner, string key)
    {
        var ids = Array(owner, key);
        if (ids.Any(node => node is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text)) ||
            ids.Select(node => node!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count() != ids.Count)
            throw Invalid($"Persisted BPMN identity list '{key}' is invalid.");
    }
    private static void StringMap(JsonObject owner)
    {
        if (owner.Any(entry => entry.Value is not JsonValue value || !value.TryGetValue<string>(out _)))
            throw Invalid("A persisted BPMN string map contains a null or non-string value.");
    }
}
