using System.Text.Json;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>Pure stop-only safety predicate. It never creates or expands invocation capability.</summary>
internal static class TriggerStopOnlyChange
{
    internal static bool IsSafe(TriggerAggregate current, TriggerAggregate proposed)
    {
        var old = current.Trigger; var next = proposed.Trigger;
        if (old.Id != next.Id || old.TypeKey != next.TypeKey || old.SchemaVersion != next.SchemaVersion || old.Name != next.Name ||
            old.Description != next.Description || old.TenantId != next.TenantId || old.ConfigurationRevision != next.ConfigurationRevision ||
            old.CreatedAt != next.CreatedAt || old.CreatedBy != next.CreatedBy || !Equal(old.Origin, next.Origin) || old.HasActivated && !next.HasActivated ||
            !JsonElement.DeepEquals(old.Configuration, next.Configuration) ||
            next.DesiredState != old.DesiredState && next.DesiredState is not (TriggerDesiredState.Paused or TriggerDesiredState.Deleted)) return false;
        foreach (var row in proposed.Subscriptions)
        {
            var previous = current.Subscriptions.SingleOrDefault(item => item.Id == row.Id);
            if (previous is null || row.DefinitionId != previous.DefinitionId || row.SlotName != previous.SlotName || row.ModelKey != previous.ModelKey ||
                row.TriggerId != previous.TriggerId || row.MappingRevision != previous.MappingRevision || !previous.Enabled && row.Enabled ||
                !Equal(row.Mappings, previous.Mappings) || !Equal(row.Origin, previous.Origin)) return false;
            if (Equal(row.Grant, previous.Grant) || row.Grant is null) continue;
            if (previous.Grant is not { } prior || row.Grant.Status != TriggerGrantStatus.Revoked || row.Grant.Id != prior.Id ||
                row.Grant.TriggerId != prior.TriggerId || row.Grant.SubscriptionId != prior.SubscriptionId || row.Grant.DefinitionId != prior.DefinitionId ||
                row.Grant.SlotName != prior.SlotName || row.Grant.ModelKey != prior.ModelKey || row.Grant.SourceTypeKey != prior.SourceTypeKey ||
                row.Grant.GrantedBy != prior.GrantedBy || row.Grant.GrantedAt != prior.GrantedAt) return false;
        }
        return true;
    }
    private static bool Equal<T>(T left, T right) => JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left), JsonSerializer.SerializeToElement(right));
}
