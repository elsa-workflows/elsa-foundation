using Elsa.Primitives.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

public sealed class GroundworkMoveWorkflowDefinitionsCommandTests
{
    [Fact]
    public async Task Moves_every_definition_in_one_selection_without_touching_authored_or_lifecycle_state()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.CreatePhysicalized());
        var clock = new Clock();
        var access = GroundworkTestAccess.AccessContext("tenant-a");
        var definitions = new GroundworkSaveWorkflowDefinitionCommand(store, clock, access);
        var folders = new GroundworkWorkflowFolderStore(store, access, clock);
        await folders.CreateAsync(Folder("destination", "Destination"));
        var first = new WorkflowDefinition { Id = "one", Name = "One", Description = "Keep", DeletedReason = "reason", TenantId = "tenant-a" };
        var second = new WorkflowDefinition { Id = "two", Name = "Two", DeletedAt = DateTimeOffset.UnixEpoch, TenantId = "tenant-a" };
        await definitions.Execute(first);
        await definitions.Execute(second);
        var firstCreatedAt = first.CreatedAt;
        clock.Advance();

        await new GroundworkMoveWorkflowDefinitionsCommand(store, clock, access)
            .Execute(["one", "two"], "destination");

        var read = new GroundworkWorkflowDefinitionStore(store);
        var movedFirst = await read.GetAsync("one");
        var movedSecond = await read.GetAsync("two");
        Assert.Equal("destination", movedFirst.FolderId);
        Assert.Equal("destination", movedSecond.FolderId);
        Assert.Equal("One", movedFirst.Name);
        Assert.Equal("Keep", movedFirst.Description);
        Assert.Equal("reason", movedFirst.DeletedReason);
        Assert.Equal("tenant-a", movedFirst.TenantId);
        Assert.Equal(DateTimeOffset.UnixEpoch, movedSecond.DeletedAt);
        Assert.Equal(firstCreatedAt, movedFirst.CreatedAt);
        Assert.True(movedFirst.LastModifiedAt > firstCreatedAt);
        Assert.Equal(movedFirst.LastModifiedAt, movedSecond.LastModifiedAt);
    }

    [Fact]
    public async Task Missing_or_foreign_members_leave_the_complete_selection_unchanged()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.CreatePhysicalized());
        var clock = new Clock();
        var access = GroundworkTestAccess.AccessContext("tenant-a");
        var definitions = new GroundworkSaveWorkflowDefinitionCommand(store, clock, access);
        await definitions.Execute(new WorkflowDefinition { Id = "one", Name = "One", FolderId = "before", TenantId = "tenant-a" });

        var command = new GroundworkMoveWorkflowDefinitionsCommand(store, clock, access);
        await Assert.ThrowsAsync<EntityNotFoundException>(() => command.Execute(["one", "missing"], null));

        Assert.Equal("before", (await new GroundworkWorkflowDefinitionStore(store).GetAsync("one")).FolderId);
    }

    [Fact]
    public async Task Rejects_duplicate_selection_before_starting_a_unit_of_work()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.CreatePhysicalized());
        var command = new GroundworkMoveWorkflowDefinitionsCommand(store, new Clock(), GroundworkTestAccess.AccessContext("tenant-a"));

        await Assert.ThrowsAsync<ArgumentException>(() => command.Execute(["one", "one"], null));

        Assert.Equal(0, store.BeginCount);
    }

    [Fact]
    public async Task Foreign_definition_or_destination_is_non_disclosing_and_leaves_local_placement_untouched()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.CreatePhysicalized());
        var clock = new Clock();
        var tenantA = GroundworkTestAccess.AccessContext("tenant-a");
        var tenantB = GroundworkTestAccess.AccessContext("tenant-b");
        await new GroundworkSaveWorkflowDefinitionCommand(store, clock, tenantA).Execute(
            new WorkflowDefinition { Id = "local", Name = "Local", TenantId = "tenant-a", FolderId = "before" });
        await new GroundworkSaveWorkflowDefinitionCommand(store, clock, tenantB).Execute(
            new WorkflowDefinition { Id = "foreign", Name = "Foreign", TenantId = "tenant-b" });
        await new GroundworkWorkflowFolderStore(store, tenantB, clock).CreateAsync(Folder("foreign-folder", "Foreign"));
        var move = new GroundworkMoveWorkflowDefinitionsCommand(store, clock, tenantA);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => move.Execute(["local"], "foreign-folder"));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => move.Execute(["foreign"], null));

        Assert.Equal("before", (await new GroundworkWorkflowDefinitionStore(store).GetAsync("local")).FolderId);
    }

    [Fact]
    public async Task Soft_delete_and_restore_save_paths_preserve_the_moved_placement()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.CreatePhysicalized());
        var clock = new Clock();
        var access = GroundworkTestAccess.AccessContext("tenant-a");
        var save = new GroundworkSaveWorkflowDefinitionCommand(store, clock, access);
        await new GroundworkWorkflowFolderStore(store, access, clock).CreateAsync(Folder("destination", "Destination"));
        await save.Execute(new WorkflowDefinition { Id = "one", Name = "One", TenantId = "tenant-a" });
        await new GroundworkMoveWorkflowDefinitionsCommand(store, clock, access).Execute(["one"], "destination");

        var definition = await new GroundworkWorkflowDefinitionStore(store).GetAsync("one");
        definition.DeletedAt = DateTimeOffset.UnixEpoch;
        await save.Execute(definition);
        definition = await new GroundworkWorkflowDefinitionStore(store).GetAsync("one");
        Assert.Equal("destination", definition.FolderId);

        definition.DeletedAt = null;
        await save.Execute(definition);
        Assert.Equal("destination", (await new GroundworkWorkflowDefinitionStore(store).GetAsync("one")).FolderId);
    }

    [Fact]
    public async Task Legacy_null_tenant_definition_uses_the_ambient_scoped_document_session()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.CreatePhysicalized());
        var clock = new Clock();
        var access = GroundworkTestAccess.AccessContext("tenant-a");
        await new GroundworkSaveWorkflowDefinitionCommand(store, clock, access).Execute(
            new WorkflowDefinition { Id = "legacy", Name = "Legacy" });

        await new GroundworkMoveWorkflowDefinitionsCommand(store, clock, access)
            .Execute(["legacy"], null);

        Assert.Null((await new GroundworkWorkflowDefinitionStore(store).GetAsync("legacy")).FolderId);
    }

    [Fact]
    public async Task Post_read_deletion_conflict_preserves_the_atomic_write_exception()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.CreatePhysicalized());
        var clock = new Clock();
        var access = GroundworkTestAccess.AccessContext("tenant-a");
        await new GroundworkSaveWorkflowDefinitionCommand(store, clock, access).Execute(
            new WorkflowDefinition { Id = "one", Name = "One", FolderId = "before", TenantId = "tenant-a" });
        var failures = new GroundworkFailureController();
        var atomicWrite = new DocumentAtomicWriteException(
            DocumentWriteOperationKind.Save,
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            "one",
            DocumentStoreWriteStatus.NotFound);
        failures.FailAt(GroundworkFailureInjectingDocumentStore.BeforeUnderlyingCommit, () => atomicWrite);
        var failureStore = new GroundworkFailureInjectingDocumentStore(
            store,
            store,
            store.Access,
            failures);

        var exception = await Assert.ThrowsAsync<WorkflowDefinitionFolderMoveConflictException>(() =>
            new GroundworkMoveWorkflowDefinitionsCommand(failureStore, clock, access).Execute(["one"], null));

        Assert.Same(atomicWrite, exception.InnerException);
        Assert.Equal("before", (await new GroundworkWorkflowDefinitionStore(store).GetAsync("one")).FolderId);
    }

    private static WorkflowFolder Folder(string id, string name)
    {
        var normalized = Elsa.Workflows.Design.Persistence.Core.Models.WorkflowFolderNames.Normalize(name);
        return new WorkflowFolder { Id = id, Name = normalized.Name, NormalizedName = normalized.NormalizedName };
    }

    private sealed class Clock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; private set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public void Advance() => UtcNow = UtcNow.AddMinutes(1);
    }
}
