using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Compensation.Exceptions;

namespace Elsa.Activities.Compensation.Internal;

/// <summary>
/// Upgrades a version-1 (M1) scope state payload to version 2 (spec 251 data model), so a scope that started before
/// M2 resumes under M2 code. Statuses, ordinals and identities are kept. Version 2 is never downgraded.
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
