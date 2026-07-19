using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Xunit;

namespace Elsa.Activities.Design.Persistence.Groundwork.Tests;

/// <summary>
/// Proves the Groundwork (document) <see cref="GroundworkAddActivityDefinitionCommand"/> atomically writes the
/// activity definition and its first version into one document store and that both read back through the matching
/// read ports — the document-store counterpart of the EF Core add command's single <c>SaveChangesAsync</c>.
/// </summary>
public class GroundworkAddActivityDefinitionCommandTests
{
    private static readonly FakePayloadSerializer Payloads = new();

    [Fact]
    public async Task Mismatched_version_tenant_rejects_the_complete_batch_before_staging()
    {
        var store = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var command = new GroundworkAddActivityDefinitionCommand(
            store,
            Payloads,
            GroundworkTestAccess.AccessContext("tenant-a"));
        var definition = new ActivityDefinition
        {
            Id = "def-1",
            ActivityTypeKey = "Acme.Send",
            Category = "General",
            TenantId = "tenant-a"
        };
        var version = Version("tenant-b");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            command.Execute(definition, version, CancellationToken.None));

        Assert.Equal(0, store.BeginCount);
        Assert.Empty(store.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind));
        Assert.Empty(store.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
    }

    [Fact]
    public async Task Add_version_rejects_explicit_wrong_tenant_before_store_io()
    {
        var store = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var command = new GroundworkAddActivityDefinitionVersionCommand(
            store,
            Payloads,
            GroundworkTestAccess.AccessContext("tenant-a"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => command.Add(Version("tenant-b")));

        Assert.Equal(0, store.SaveCount);
        Assert.Empty(store.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
    }

    [Fact]
    public async Task Persists_definition_and_version_readable_through_the_ports()
    {
        var store = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var command = new GroundworkAddActivityDefinitionCommand(
            store,
            Payloads,
            GroundworkTestAccess.DefaultAccessContextAccessor);

        var definition = new ActivityDefinition { Id = "def-1", ActivityTypeKey = "Acme.Send", Category = "General", DisplayName = "Send" };
        var version = Version();

        await command.Execute(definition, version, CancellationToken.None);

        var definitionStore = new GroundworkActivityDefinitionStore(store);
        var versionStore = new GroundworkActivityDefinitionVersionStore(store, definitionStore, Payloads);

        var readDefinition = await definitionStore.GetAsync("def-1");
        var readVersion = await versionStore.GetAsync("ver-1");

        Assert.Equal("Acme.Send", readDefinition.ActivityTypeKey);
        Assert.Equal("Acme.SendActivity", readVersion.DescriptorType);
        Assert.Equal("def-1", readVersion.DefinitionId);
    }

    private static ActivityDefinitionVersion Version(string? tenantId = null) => new("1.0.0", "def-1")
    {
        Id = "ver-1",
        DescriptorType = "Acme.SendActivity",
        DescriptorPayload = JsonSerializer.SerializeToElement(new { kind = "send" }),
        SourceKind = "Json",
        SourceId = "asset-1",
        DesignFacets = [],
        TenantId = tenantId
    };
}
