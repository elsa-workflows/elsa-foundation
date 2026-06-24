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
    public async Task Persists_definition_and_version_readable_through_the_ports()
    {
        var store = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var command = new GroundworkAddActivityDefinitionCommand(store, Payloads);

        var definition = new ActivityDefinition { Id = "def-1", ActivityTypeKey = "Acme.Send", Category = "General", DisplayName = "Send" };
        var version = new ActivityDefinitionVersion("1.0.0", "def-1")
        {
            Id = "ver-1",
            DescriptorType = "Acme.SendActivity",
            DescriptorPayload = JsonSerializer.SerializeToElement(new { kind = "send" }),
            SourceKind = "Json",
            SourceId = "asset-1",
            DesignFacets = [],
        };

        await command.Execute(definition, version, CancellationToken.None);

        var definitionStore = new GroundworkActivityDefinitionStore(store);
        var versionStore = new GroundworkActivityDefinitionVersionStore(store, definitionStore, Payloads);

        var readDefinition = await definitionStore.GetAsync("def-1");
        var readVersion = await versionStore.GetAsync("ver-1");

        Assert.Equal("Acme.Send", readDefinition.ActivityTypeKey);
        Assert.Equal("Acme.SendActivity", readVersion.DescriptorType);
        Assert.Equal("def-1", readVersion.DefinitionId);
    }
}
