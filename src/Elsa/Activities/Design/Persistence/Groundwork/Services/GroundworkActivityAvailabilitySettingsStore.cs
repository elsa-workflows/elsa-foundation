using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Stores;
using Groundwork.Documents.Store;

namespace Elsa.Activities.Design.Persistence.Groundwork.Services;

public sealed class GroundworkActivityAvailabilitySettingsStore(IDocumentStore store) : IActivityAvailabilitySettingsStore
{
    public async Task<ActivityAvailabilitySettings?> LoadAsync(string scope, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        var envelope = await store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityAvailabilitySettingsDocumentKind,
            scope,
            cancellationToken);

        return envelope is null
            ? null
            : JsonSerializer.Deserialize<ActivityAvailabilitySettingsDocument>(envelope.ContentJson, GroundworkActivitiesDesignJson.Options)?.Settings;
    }

    public async Task SaveAsync(ActivityAvailabilitySettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Scope);

        var document = new ActivityAvailabilitySettingsDocument(
            ActivitiesDesignStorageManifest.ActivityAvailabilitySettingsCollection,
            settings);
        var content = JsonSerializer.Serialize(document, GroundworkActivitiesDesignJson.Options);

        await store.SaveAsync(
            new SaveDocumentRequest(
                ActivitiesDesignStorageManifest.ActivityAvailabilitySettingsDocumentKind,
                settings.Scope,
                ActivitiesDesignStorageManifest.SchemaVersion,
                content),
            cancellationToken);
    }

    private sealed record ActivityAvailabilitySettingsDocument(string Collection, ActivityAvailabilitySettings Settings);
}
