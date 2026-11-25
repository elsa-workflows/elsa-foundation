using System.Text.Json;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Api.StandaloneTriggers;

internal sealed partial class TriggerManagement
{
    private (JsonElement Value, IReadOnlyList<TriggerConfigurationRedaction> Redactions) RedactConfiguration(StandaloneTrigger trigger)
    {
        var value = trigger.Configuration;
        var redactions = new List<TriggerConfigurationRedaction>();
        foreach (var path in ProtectedPaths(trigger))
        {
            value = EditProtectedValue(value, path, null, out var changed);
            if (changed) redactions.Add(new(path));
        }
        return (value.Clone(), redactions.AsReadOnly());
    }

    private JsonElement PreserveProtectedConfiguration(StandaloneTrigger trigger, JsonElement proposed)
    {
        var value = proposed;
        foreach (var path in ProtectedPaths(trigger))
        {
            if (TryProtectedValue(trigger.Configuration, path, out var retained))
                value = EditProtectedValue(value, path, retained, out _);
        }
        return value.Clone();
    }

    private IEnumerable<string[]> ProtectedPaths(StandaloneTrigger trigger) =>
        (types.Find(trigger.TypeKey)?.ConfigurationFields ?? []).Where(field => field.Sensitive)
            .SelectMany(field => field.Key.Contains('.') ? new[] { new[] { field.Key }, field.Key.Split('.') } : [new[] { field.Key }]);

    private static bool TryProtectedValue(JsonElement value, IReadOnlyList<string> path, out JsonElement retained)
    {
        foreach (var segment in path)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out var next))
            { retained = default; return false; }
            value = next;
        }
        retained = value;
        return true;
    }

    // A null nullable element removes a protected field; a present JSON null restores its explicit null value.
    // Rebuild only changed ancestors. Baseline serialization writes every untouched JsonElement as its raw text.
    private static JsonElement EditProtectedValue(JsonElement value, IReadOnlyList<string> path, JsonElement? retained,
        out bool changed, int index = 0)
    {
        changed = false;
        if (value.ValueKind != JsonValueKind.Object) return value;
        var key = path[index];
        var present = value.TryGetProperty(key, out var child);
        JsonElement? replacement = null;
        if (index == path.Count - 1)
        {
            if (present == retained.HasValue) return value; // Keep an explicitly supplied value, including null.
            replacement = retained;
        }
        else
        {
            if (!present)
            {
                if (!retained.HasValue) return value;
                child = JsonSerializer.SerializeToElement(new Dictionary<string, JsonElement>(), JsonBaseline.Options);
            }
            var nested = EditProtectedValue(child, path, retained, out var nestedChanged, index + 1);
            if (!nestedChanged) return value;
            replacement = nested;
        }

        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (property.Name != key) properties.Add(property.Name, property.Value);
            else if (replacement is { } updated) properties.Add(property.Name, updated);
        }
        if (!present && replacement is { } added) properties.Add(key, added);
        changed = true;
        return JsonSerializer.SerializeToElement(properties, JsonBaseline.Options);
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
