using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

/// <summary>
/// Proves the Groundwork (document) <see cref="GroundworkAddWorkflowDefinitionCommand"/> atomically writes the
/// workflow definition and its first draft into one document store and that both read back through the matching
/// read ports — the document-store counterpart of the EF Core add command's single <c>SaveChangesAsync</c>.
/// </summary>
public class GroundworkAddWorkflowDefinitionCommandTests
{
    private static readonly FakePayloadSerializer Payloads = new();

    [Fact]
    public async Task Persists_definition_and_draft_readable_through_the_ports()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        var command = new GroundworkAddWorkflowDefinitionCommand(store, Payloads);

        var definition = new WorkflowDefinition { Id = "def-1", Name = "Onboarding", Description = "New hire flow" };
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft-1",
            WorkflowDefinitionId = "def-1",
            State = new WorkflowDefinitionState([], null, [], [], null, null),
        };

        await command.Execute(definition, draft, CancellationToken.None);

        var definitionStore = new GroundworkWorkflowDefinitionStore(store);
        var draftStore = new GroundworkWorkflowDefinitionDraftStore(store, Payloads);

        var readDefinition = await definitionStore.FindByIdAsync("def-1");
        var readDraft = await draftStore.FindByWorkflowDefinitionIdAsync("def-1");

        Assert.NotNull(readDefinition);
        Assert.Equal("Onboarding", readDefinition!.Name);
        Assert.NotNull(readDraft);
        Assert.Equal("draft-1", readDraft!.Id);
    }
}
