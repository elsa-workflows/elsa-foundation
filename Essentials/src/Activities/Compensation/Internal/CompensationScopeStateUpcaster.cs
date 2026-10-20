using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Compensation.Exceptions;

namespace Elsa.Activities.Compensation.Internal;

/// <summary>
/// Upgrades older scope state payloads to the current version: version 1 (M1) to version 2 (spec 251 data model), and
/// version 2 to version 3 (spec 258 data model), so a scope that started under an older release resumes under the
/// current code. Statuses, ordinals and identities are kept. No version is ever downgraded.
/// </summary>
internal static class CompensationScopeStateUpcaster
{
    public static JsonElement FromVersion1(JsonElement payload)
    {
        if (JsonNode.Parse(payload.GetRawText()) is not JsonObject state)
            throw new CompensationScopeExecutionException("Compensation scope private state (version 1) is not an object.");

        if (state["entries"] is JsonArray entries)
        {
            foreach (var entry in entries.OfType<JsonObject>())
                UpgradeEntry(entry);
        }

        return JsonSerializer.SerializeToElement(state);
    }

    /// <summary>
    /// Version 2 to 3: every field is kept, and the scope gets no confirmations and no received hand-overs. Entries get
    /// no origin and no handler node: they are the scope's own, whose handlers resolve from their steps.
    /// </summary>
    public static JsonElement FromVersion2(JsonElement payload)
    {
        if (JsonNode.Parse(payload.GetRawText()) is not JsonObject state)
            throw new CompensationScopeExecutionException("Compensation scope private state (version 2) is not an object.");

        state["confirmations"] ??= new JsonArray();
        state["receivedHandOvers"] ??= new JsonArray();
        return JsonSerializer.SerializeToElement(state);
    }

    private static void UpgradeEntry(JsonObject entry)
    {
        var forward = entry["forwardActivityExecutionId"]?.GetValue<string>()
                      ?? throw new CompensationScopeExecutionException("A version-1 compensation entry has no forward activity execution id.");
        var status = entry["status"]?.GetValue<string>();

        // M1 participants received the forward invocation id, which is the forward activity execution id.
        entry["operationKey"] = forward;
        entry["forwardOutcome"] = nameof(CompensationForwardOutcome.Completed);

        var attempts = new JsonArray();
        if (entry["handlerActivityExecutionIds"] is JsonArray handlerIds)
        {
            foreach (var id in handlerIds)
                attempts.Add(new JsonObject { ["result"] = nameof(CompensationAttemptResult.Completed), ["activityExecutionId"] = id?.GetValue<string>() });
        }

        // An M1 entry that is compensating has a scheduled handler whose execution id is recorded only on completion.
        if (status == nameof(CompensationEntryStatus.Compensating))
            attempts.Add(new JsonObject { ["result"] = nameof(CompensationAttemptResult.Running) });

        entry.Remove("handlerActivityExecutionIds");
        entry["handlerAttempts"] = attempts;
    }
}
