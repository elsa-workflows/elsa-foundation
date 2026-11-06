using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Design.Core.Models;
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
    public async Task Draft_read_observes_a_concurrent_source_save_in_the_same_operation_context_with_old_entities_tracked()
    {
        await using var database = new TemporarySqliteDatabase("activity-data-draft-freshness");
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer();
        await using (var seedConnection = new SqliteConnection($"Data Source={database.Path}"))
        {
            await seedConnection.OpenAsync();
            await using var seed = Create(seedConnection);
            await seed.Database.EnsureCreatedAsync();
            await new EfAddWorkflowDefinitionCommand(seed, access, new EfDesignAtomicWriter(seed, access), serializer, new TestIdentity())
                .Execute(new DesignOperationKey("seed-freshness"),
                    new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Fresh source" },
                    new WorkflowDefinitionDraft { Id = "draft", TenantId = "tenant-a", WorkflowDefinitionId = "definition",
                        State = new([], new("before", "test", [], []), [], [], null) }, []);
        }
        await using var readConnection = new SqliteConnection($"Data Source={database.Path}");
        await readConnection.OpenAsync();
        await using var read = Create(readConnection);
        var store = new EfWorkflowDefinitionDraftStore(read, serializer, access);
        // The ordinary editor read can already have tracked this draft and its auto-included source.
        var tracked = (await store.FindWithLayoutByIdAsync("draft"))!.Draft;
        var before = (await store.FindByIdAsync("draft"))!;
        var beforeRevision = before.Source!.Revision;
        Assert.NotSame(tracked, before);
        Assert.NotSame(tracked.Source, before.Source);
        Assert.Equal(EntityState.Detached, read.Entry(before).State);

        await using (var saveConnection = new SqliteConnection($"Data Source={database.Path}"))
        {
            await saveConnection.OpenAsync();
            await using var save = Create(saveConnection);
            await new EfUpdateDraftCommand(save, access, new EfDesignAtomicWriter(save, access), serializer,
                    new EmptyActivityStructureService(), new TestLockProvider())
                .Execute(new DesignOperationKey("concurrent-freshness-save"),
                    new UpdateDraftRequest("draft", new([], new("after", "test", [], []), [], [], null), []));
        }
        // Reuse the exact same reader/store/context; the old tracked Source must not mask the committed source.
        var after = (await store.FindByIdAsync("draft"))!;
        Assert.Equal("after", after.State.RootActivity!.NodeId);
        Assert.True(after.Source!.Revision > beforeRevision);
        Assert.NotEqual(before.Source.Content, after.Source.Content);
        Assert.NotSame(before.Source, after.Source);
        Assert.Equal("before", before.State.RootActivity!.NodeId);
        Assert.Equal("before", tracked.State.RootActivity!.NodeId);
        Assert.Equal(EntityState.Detached, read.Entry(after).State);
        Assert.Equal(EntityState.Detached, read.Entry(after.Source).State);
    }
}
