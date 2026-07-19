using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Handlers;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Services;
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
    public async Task Api_handler_creates_a_definition_in_an_ambient_tenant_folder_when_factories_omit_tenant()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.CreatePhysicalized());
        var access = GroundworkTestAccess.AccessContext("tenant-a");
        var clock = new FakeSystemClock();
        var folders = new GroundworkWorkflowFolderStore(store, access, clock);
        await folders.CreateAsync(Folder("folder-1", "Folder"));
        var identities = new SequentialIdentityGenerator();
        var handler = new AddDefinitionCommandHandler(
            new WorkflowDefinitionFactory(identities),
            new WorkflowDefinitionDraftFactory(identities),
            new GroundworkAddWorkflowDefinitionCommand(store, Payloads, clock, access));

        var result = await handler.Handle(
            new AddDefinition("Filed workflow", null, FolderId: "folder-1"),
            CancellationToken.None);

        var definition = await new GroundworkWorkflowDefinitionStore(store).FindByIdAsync(result.Definition.Id);
        var draft = await new GroundworkWorkflowDefinitionDraftStore(store, Payloads, access)
            .FindByWorkflowDefinitionIdAsync(result.Definition.Id);
        Assert.Equal("folder-1", definition!.FolderId);
        Assert.Equal("tenant-a", definition.TenantId);
        Assert.Equal("tenant-a", draft!.TenantId);
    }

    [Fact]
    public async Task Explicit_foreign_definition_and_draft_tenants_are_rejected_without_reassignment()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        var command = new GroundworkAddWorkflowDefinitionCommand(
            store,
            Payloads,
            new FakeSystemClock(),
            GroundworkTestAccess.AccessContext("tenant-a"));
        var definition = new WorkflowDefinition { Id = "def-1", Name = "Forged", TenantId = "tenant-b" };
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft-1",
            WorkflowDefinitionId = definition.Id,
            TenantId = "tenant-b",
            State = WorkflowDefinitionState.Empty
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            command.Execute(definition, draft, [], CancellationToken.None));

        Assert.Equal("tenant-b", definition.TenantId);
        Assert.Equal("tenant-b", draft.TenantId);
        Assert.Equal(0, store.BeginCount);
    }

    [Fact]
    public async Task Mismatched_draft_tenant_rejects_the_complete_batch_before_staging()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        var command = new GroundworkAddWorkflowDefinitionCommand(
            store,
            Payloads,
            new FakeSystemClock(),
            GroundworkTestAccess.AccessContext("tenant-a"));
        var definition = new WorkflowDefinition { Id = "def-1", Name = "Onboarding", TenantId = "tenant-a" };
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft-1",
            WorkflowDefinitionId = "def-1",
            TenantId = "tenant-b",
            State = WorkflowDefinitionState.Empty
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            command.Execute(definition, draft, [], CancellationToken.None));

        Assert.Equal(0, store.BeginCount);
        Assert.Empty(store.Snapshot(WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind));
        Assert.Empty(store.Snapshot(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind));
    }

    [Fact]
    public async Task Add_version_rejects_explicit_wrong_tenant_before_store_io()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        var command = new GroundworkAddWorkflowDefinitionVersionCommand(
            store,
            Payloads,
            GroundworkTestAccess.AccessContext("tenant-a"));
        var version = new WorkflowDefinitionVersion("def-1", "1.0.0")
        {
            Id = "version-1",
            TenantId = "tenant-b",
            State = WorkflowDefinitionState.Empty
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => command.Add(version));

        Assert.Equal(0, store.SaveCount);
        Assert.Empty(store.Snapshot(WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind));
    }

    [Fact]
    public async Task Persists_definition_and_draft_readable_through_the_ports()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        var command = new GroundworkAddWorkflowDefinitionCommand(
            store,
            Payloads,
            new FakeSystemClock(),
            GroundworkTestAccess.DefaultAccessContextAccessor);

        var definition = new WorkflowDefinition { Id = "def-1", Name = "Onboarding", Description = "New hire flow" };
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft-1",
            WorkflowDefinitionId = "def-1",
            State = new WorkflowDefinitionState([], null, [], [], null),
        };

        var layout = new[] { new DesignMetadataRecord("node-1", 10, 20, 100, 80) };
        await command.Execute(definition, draft, layout, CancellationToken.None);

        var definitionStore = new GroundworkWorkflowDefinitionStore(store);
        var draftStore = new GroundworkWorkflowDefinitionDraftStore(
            store,
            Payloads,
            GroundworkTestAccess.DefaultAccessContextAccessor);

        var readDefinition = await definitionStore.FindByIdAsync("def-1");
        var readDraft = await draftStore.FindByWorkflowDefinitionIdAsync("def-1");
        var readLayout = await draftStore.FindLayoutByDraftIdAsync("draft-1");

        Assert.NotNull(readDefinition);
        Assert.Equal("Onboarding", readDefinition!.Name);
        Assert.NotNull(readDraft);
        Assert.Equal("draft-1", readDraft!.Id);
        Assert.Equal(layout, readLayout);
    }

    private static WorkflowFolder Folder(string id, string name)
    {
        var normalized = WorkflowFolderNames.Normalize(name);
        return new WorkflowFolder
        {
            Id = id,
            Name = normalized.Name,
            NormalizedName = normalized.NormalizedName
        };
    }

    private sealed class SequentialIdentityGenerator : IIdentityGenerator
    {
        private int _next;
        public string Generate() => $"id-{++_next}";
    }

    private sealed class FakeSystemClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
