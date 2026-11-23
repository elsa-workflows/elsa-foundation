using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Api.StandaloneTriggers;

internal sealed partial class TriggerManagement
{
    private (JsonElement Value, IReadOnlyList<TriggerConfigurationRedaction> Redactions) RedactConfiguration(StandaloneTrigger trigger)
    {
        var value = JsonNode.Parse(trigger.Configuration.GetRawText())!.AsObject();
        var redactions = new List<TriggerConfigurationRedaction>();
        foreach (var path in ProtectedPaths(trigger))
            if (Parent(value, path) is { } parent && parent.Remove(path[^1])) redactions.Add(new(path));
        return (JsonSerializer.SerializeToElement(value), redactions.AsReadOnly());
    }

    private JsonElement PreserveProtectedConfiguration(StandaloneTrigger trigger, JsonElement proposed)
    {
        var value = JsonNode.Parse(proposed.GetRawText())!.AsObject();
        var original = JsonNode.Parse(trigger.Configuration.GetRawText())!.AsObject();
        foreach (var path in ProtectedPaths(trigger))
        {
            var previous = Parent(original, path);
            if (previous is null || !previous.TryGetPropertyValue(path[^1], out var retained)) continue;
            var parent = Parent(value, path, create: true);
            if (parent is not null && !parent.ContainsKey(path[^1])) parent[path[^1]] = retained?.DeepClone();
        }
        return JsonSerializer.SerializeToElement(value);
    }

    private IEnumerable<string[]> ProtectedPaths(StandaloneTrigger trigger) =>
        (types.Find(trigger.TypeKey)?.ConfigurationFields ?? []).Where(field => field.Sensitive)
            .SelectMany(field => field.Key.Contains('.') ? new[] { new[] { field.Key }, field.Key.Split('.') } : [new[] { field.Key }]);

    private static JsonObject? Parent(JsonObject root, IReadOnlyList<string> path, bool create = false)
    {
        var current = root;
        for (var index = 0; index < path.Count - 1; index++)
        {
            if (!current.TryGetPropertyValue(path[index], out var next))
            {
                if (!create) return null;
                next = new JsonObject(); current[path[index]] = next;
            }
            if (next is not JsonObject child) return null;
            current = child;
        }
        return current;
    }

    private async ValueTask RequireChangeReviewAsync(TriggerAggregate aggregate, JsonElement command, CancellationToken ct)
    {
        // Resource-owned stop/revoke operations need no authority to expose the target's private content.
        if (Text(command, "kind") is "PauseTrigger" or "PauseSubscription" or "RevokeInvocation" or "RemoveSubscription" or "DeleteTrigger") return;
        var kind = Text(command, "kind");
        var affected = kind is "SaveConfiguration" or "EnableTrigger" ? aggregate.Subscriptions :
            aggregate.Subscriptions.Where(row => TryProperty(command, "subscriptionId", out var id) && id.ValueKind == JsonValueKind.String && row.Id == id.GetString());
        var selectors = affected.Select(row => new TriggerTargetSelector(row.DefinitionId, row.SlotName)).ToList();
        if (TryProperty(command, "target", out var target)) selectors.Add(Selector(target));
        foreach (var selector in selectors.Distinct())
            if (!await CanReadTargetAsync(selector.DefinitionId, selector.SlotName, ct))
                throw Validation("This change cannot be reviewed without access to an affected workflow.");
    }
}
