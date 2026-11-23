using Elsa.Persistence.Core;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Commands;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfWorkflowDesignPersistenceTests
{
    [Fact]
    public async Task Workflow_commands_store_one_shared_payload_and_capture_its_exact_revision()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer();
        var atomic = new EfDesignAtomicWriter(db, access);
        var draft = new WorkflowDefinitionDraft { Id = "draft", TenantId = "tenant-a", WorkflowDefinitionId = "flow", State = State() };
        await new EfAddWorkflowDefinitionCommand(db, access, atomic, serializer, new TestIdentity()).Execute(
            new DesignOperationKey("create"), new WorkflowDefinition { Id = "flow", TenantId = "tenant-a", Name = "Flow" }, draft);
        Assert.Equal(1, await db.Set<SourceWorkingCopy>().CountAsync());
        Assert.Null(db.Model.FindEntityType(typeof(WorkflowDefinitionDraft))!.FindProperty(nameof(WorkflowDefinitionDraft.StateSource)));
        var adapter = new WorkflowSourceRevisions(db, access, serializer);
        var captured = await adapter.CaptureAsync("flow", "draft", 0, "review");
        await new EfUpdateDraftCommand(db, access, atomic, serializer, new EmptyActivityStructureService(), new TestLockProvider()).Execute(
            new DesignOperationKey("edit"), new UpdateDraftRequest("draft", State(), []));
        Assert.Equal(1, (await db.Set<SourceWorkingCopy>().SingleAsync()).Revision);
        Assert.Equal(captured.Key, (await adapter.CaptureAsync("flow", "draft", 0, "review")).Key);
        Assert.Equal("revision-conflict", (await Assert.ThrowsAsync<SourceRevisionException>(() => adapter.CaptureAsync("flow", "draft", 0, "stale"))).Code);
        Assert.NotNull(await adapter.ReadAsync("FLOW", "draft"));
        Assert.Single(await adapter.HistoryAsync("FLOW", "draft"));
        Assert.NotNull(await adapter.ReadSnapshotAsync("FLOW", "draft", captured.Key));
        await new EfDiscardDraftCommand(db, access, atomic, new TestLockProvider()).Execute(new DesignOperationKey("discard"), "draft");
        Assert.Null(await adapter.ReadAsync("flow", "draft"));
        Assert.True((await db.Set<SourceWorkingCopy>().SingleAsync()).Discarded);
        Assert.Equal(captured.Key, (await adapter.CaptureAsync("FLOW", "draft", 0, "review")).Key);
        Assert.Single(await adapter.HistoryAsync("flow", "draft"));
    }
}
