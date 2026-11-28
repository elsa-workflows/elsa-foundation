using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>Reads the exact enabled source cohort without selecting workflow targets or enacting delivery.</summary>
internal static class WorkflowSubscriptionSourceReader
{
    internal static bool Matches(StandaloneTrigger trigger, RuntimeWorkflowSubscriptionSource source)
    {
        if (trigger.TypeKey != source.TriggerTypeKey || trigger.DesiredState != TriggerDesiredState.Enabled) return false;
        if (trigger.SchemaVersion != source.TriggerSchemaVersion) throw new NotSupportedException("An eligible source resource has an unsupported schema.");
        if (!trigger.Configuration.TryGetProperty(source.ConfigurationField, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("A source resource has invalid canonical matching configuration.");
        return value.GetString() == source.ConfigurationValue;
    }

    internal static async ValueTask<IReadOnlyList<TriggerAggregate>> ReadMatchingAsync(IStandaloneTriggerStore resources, TriggerScope scope,
        RuntimeWorkflowSubscriptionSource source, CancellationToken token)
    {
        var result = new List<TriggerAggregate>();
        string? after = null; var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var page = await resources.ListAsync(scope, new(100, after, TypeKey: source.TriggerTypeKey, DesiredState: TriggerDesiredState.Enabled), token);
            foreach (var trigger in page.Items)
            {
                if (!Matches(trigger, source)) continue;
                var aggregate = await resources.FindAsync(scope, trigger.Id, token) ?? throw new TriggerOperationException("conflict", "A matching resource changed during discovery.");
                if (aggregate.Trigger.Revision != trigger.Revision || !Matches(aggregate.Trigger, source))
                    throw new TriggerOperationException("conflict", "A matching resource changed during discovery.");
                result.Add(aggregate);
            }
            if (page.Next is null) break;
            if (!seen.Add(page.Next)) throw new InvalidDataException("The resource discovery cursor did not advance.");
            after = page.Next;
        }
        return Array.AsReadOnly(result.OrderBy(resource => resource.Trigger.Id, StringComparer.Ordinal).ToArray());
    }
}
