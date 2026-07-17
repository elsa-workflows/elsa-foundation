using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Persistence.Groundwork.Querying;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Activities.Design.Persistence.Groundwork.Services;

public sealed class GroundworkActivityUpgradePlanStore(IDocumentStore store) : IActivityUpgradePlanStore
{
    private static readonly JsonSerializerOptions JsonOptions = GroundworkActivitiesDesignJson.Options;

    public async Task<ActivityUpgradePlan?> FindAsync(string planId, CancellationToken cancellationToken = default)
    {
        var envelope = await store.LoadAsync(ActivitiesDesignStorageManifest.ActivityUpgradePlanDocumentKind, planId, cancellationToken);
        if (envelope is null)
            return null;
        var document = JsonSerializer.Deserialize<UpgradePlanDocument>(envelope.ContentJson, JsonOptions);
        return document?.Plan ?? throw new InvalidOperationException($"Activity upgrade plan '{planId}' is unreadable.");
    }

    public async Task SaveAsync(ActivityUpgradePlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var existing = await store.LoadAsync(ActivitiesDesignStorageManifest.ActivityUpgradePlanDocumentKind, plan.PlanId, cancellationToken);
        if (existing is not null)
        {
            var document = JsonSerializer.Deserialize<UpgradePlanDocument>(existing.ContentJson, JsonOptions);
            if (document?.Plan is { } existingPlan && SamePlan(existingPlan, plan))
                return;
            throw new InvalidOperationException($"Activity upgrade plan '{plan.PlanId}' is immutable.");
        }
        var save = JsonDocumentStoreExtensions.ToSaveDocumentRequest(
            ActivitiesDesignStorageManifest.ActivityUpgradePlanDocumentKind,
            plan.PlanId,
            ActivitiesDesignStorageManifest.SchemaVersion,
            new UpgradePlanDocument(ActivitiesDesignStorageManifest.ActivityUpgradePlanCollection, plan),
            JsonOptions);
        await store.SaveAllAsync(
            DocumentCommitScope.Of(ActivitiesDesignStorageManifest.ActivityUpgradePlanDocumentKind),
            [save],
            cancellationToken);
    }

    private sealed record UpgradePlanDocument(string Collection, ActivityUpgradePlan Plan);

    private static bool SamePlan(ActivityUpgradePlan left, ActivityUpgradePlan right) =>
        JsonNode.DeepEquals(
            JsonNode.Parse(JsonSerializer.Serialize(left, JsonOptions)),
            JsonNode.Parse(JsonSerializer.Serialize(right, JsonOptions)));
}
