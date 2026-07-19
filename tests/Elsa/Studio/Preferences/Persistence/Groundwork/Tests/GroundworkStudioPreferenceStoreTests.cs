using System.Text.Json;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Studio.Preferences.Core.Models;
using Elsa.Studio.Preferences.Persistence.Groundwork;
using Xunit;

namespace Elsa.Studio.Preferences.Persistence.Groundwork.Tests;

public sealed class GroundworkStudioPreferenceStoreTests
{
    [Fact]
    public async Task RoundTripsScopedDocumentAndEnforcesCas()
    {
        var store = new GroundworkStudioPreferenceStore(new InMemoryDocumentStore(StudioPreferencesStorageManifest.Create()));
        var key = new StudioPreferenceKey("user-1", "tenant-1", "studio-1", "dashboard");

        var created = await store.WriteAsync(key, new(1, Json("{\"size\":\"wide\"}")), StudioPreferenceWriteCondition.MustNotExist, DateTimeOffset.UtcNow);
        Assert.Equal(StudioPreferenceStoreWriteStatus.Saved, created.Status);
        Assert.Equal("rev-1", created.Document!.Revision);

        var stale = await store.WriteAsync(key, new(1, Json("{}")), StudioPreferenceWriteCondition.Matches("rev-0"), DateTimeOffset.UtcNow);
        Assert.Equal(StudioPreferenceStoreWriteStatus.Conflict, stale.Status);

        var updated = await store.WriteAsync(key, new(1, Json("{}")), StudioPreferenceWriteCondition.Matches("rev-1"), DateTimeOffset.UtcNow);
        Assert.Equal("rev-2", updated.Document!.Revision);
        Assert.Equal("rev-2", (await store.FindAsync(key))!.Revision);
    }

    [Fact]
    public async Task CompositeIdentityDoesNotCollideAcrossScopeBoundaries()
    {
        var store = new GroundworkStudioPreferenceStore(new InMemoryDocumentStore(StudioPreferencesStorageManifest.Create()));
        var first = new StudioPreferenceKey("ab", "c", "host", "dashboard");
        var second = new StudioPreferenceKey("a", "bc", "host", "dashboard");

        await store.WriteAsync(first, new(1, Json("{\"owner\":1}")), StudioPreferenceWriteCondition.MustNotExist, DateTimeOffset.UtcNow);
        Assert.Null(await store.FindAsync(second));
    }

    [Fact]
    public void UnifiedManifestIncludesStudioPreferences()
    {
        Assert.Contains(
            new GroundworkAllFeaturesDeploymentSchema().CreateManifest().StorageUnits,
            x => x.Identity.Value == StudioPreferencesStorageManifest.DocumentKind);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
