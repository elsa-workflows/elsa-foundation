using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
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

    [Fact]
    public async Task Add_accepts_the_portable_128_character_definition_identity_and_name()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        var command = new GroundworkAddWorkflowDefinitionCommand(
            store,
            Payloads,
            new FakeSystemClock(),
            GroundworkTestAccess.DefaultAccessContextAccessor);
        var definition = new WorkflowDefinition
        {
            Id = new string('i', WorkflowDefinitionConstraints.MaximumIdLength),
            Name = new string('n', WorkflowDefinitionConstraints.MaximumNameLength)
        };
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft-128",
            WorkflowDefinitionId = definition.Id,
            State = WorkflowDefinitionState.Empty
        };

        await command.Execute(definition, draft, [], CancellationToken.None);

        Assert.Single(store.Snapshot(WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Add_rejects_values_larger_than_the_portable_limit_before_staging(bool oversizedId)
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        var command = new GroundworkAddWorkflowDefinitionCommand(
            store,
            Payloads,
            new FakeSystemClock(),
            GroundworkTestAccess.DefaultAccessContextAccessor);
        var definition = new WorkflowDefinition
        {
            Id = oversizedId ? new string('i', 129) : "definition-1",
            Name = oversizedId ? "Name" : new string('n', 129)
        };
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft-over-limit",
            WorkflowDefinitionId = definition.Id,
            State = WorkflowDefinitionState.Empty
        };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => command.Execute(definition, draft, [], CancellationToken.None));
        Assert.Equal(0, store.BeginCount);
    }

    private sealed class FakeSystemClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
