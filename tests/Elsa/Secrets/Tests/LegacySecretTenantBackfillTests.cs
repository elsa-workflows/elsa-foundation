using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.Groundwork;
using Elsa.Secrets.Persistence.Groundwork.Stores;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class LegacySecretTenantBackfillTests
{
    [Fact]
    public async Task Legacy_rows_are_invisible_until_an_explicit_tenant_is_supplied()
    {
        var store = Store();
        await SeedLegacyAsync(store, "payments.api");
        var repository = new GroundworkSecretRepository(store);
        var backfill = new LegacySecretTenantBackfill(store);

        Assert.Null(await repository.FindAsync("tenant-1", "payments.api"));
        await Assert.ThrowsAsync<ArgumentException>(async () => await backfill.BackfillAsync(""));

        Assert.Equal(1, await backfill.BackfillAsync("tenant-1"));
        Assert.Equal("tenant-1", (await repository.FindAsync("tenant-1", "payments.api"))!.TenantId);
        Assert.Null(await store.LoadAsync(SecretsStorageManifest.SecretDocumentKind, "payments.api"));
    }

    [Fact]
    public async Task Backfill_refuses_to_overwrite_an_existing_tenant_secret_and_keeps_the_legacy_row()
    {
        var store = Store();
        await SeedLegacyAsync(store, "payments.api");
        var repository = new GroundworkSecretRepository(store);
        await repository.SaveAsync(Secret("tenant-1", "payments.api", "current"));
        var backfill = new LegacySecretTenantBackfill(store);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await backfill.BackfillAsync("tenant-1"));

        Assert.Equal("current", (await repository.FindAsync("tenant-1", "payments.api"))!.LatestActiveVersion!.Payload.Value);
        Assert.NotNull(await store.LoadAsync(SecretsStorageManifest.SecretDocumentKind, "payments.api"));
    }

    [Fact]
    public async Task Conflicting_persisted_tenant_identifiers_are_rejected_as_corrupt_state()
    {
        var store = Store();
        var content = JsonSerializer.Serialize(
            new
            {
                collection = SecretsStorageManifest.SecretCollection,
                tenantId = "tenant-1",
                secret = Secret("tenant-2", "payments.api", "value")
            },
            JsonOptions());
        await store.SaveAsync(new SaveDocumentRequest(
            SecretsStorageManifest.SecretDocumentKind,
            "8:tenant-1payments.api",
            SecretsStorageManifest.SchemaVersion,
            content));

        var repository = new GroundworkSecretRepository(store);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await repository.FindAsync("tenant-1", "payments.api"));
    }

    private static InMemoryDocumentStore Store() => new(SecretsStorageManifest.Create());

    private static async Task SeedLegacyAsync(InMemoryDocumentStore store, string name)
    {
        var content = JsonSerializer.Serialize(
            new { collection = SecretsStorageManifest.SecretCollection, secret = Secret("", name, "legacy") },
            JsonOptions());
        await store.SaveAsync(new SaveDocumentRequest(
            SecretsStorageManifest.SecretDocumentKind,
            name,
            "1.0.0",
            content));
    }

    private static JsonSerializerOptions JsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static Secret Secret(string tenantId, string name, string value) => new()
    {
        TenantId = tenantId,
        Name = name,
        DisplayName = name,
        Versions =
        [
            new SecretVersion
            {
                Version = 1,
                Payload = SecretPayload.FromValue(value)
            }
        ]
    };
}
