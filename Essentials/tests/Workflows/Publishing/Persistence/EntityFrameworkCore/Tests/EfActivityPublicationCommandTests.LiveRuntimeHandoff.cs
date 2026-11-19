using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

using static Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests.ActivityPublicationTestMaterial;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfActivityPublicationCommandTests
{
    [Theory]
    [InlineData(false, "expired")] [InlineData(false, "expiry-change")] [InlineData(false, "layout-change")]
    [InlineData(true, "expired")] [InlineData(true, "expiry-change")] [InlineData(true, "layout-change")]
    public async Task Production_publication_commands_refuse_nonlive_or_changed_runtime_roots_before_design_commit(bool sourceOwned, string defect)
    {
        var draft = Commit(); var source = SourceCommit(Published);
        var proposed = sourceOwned ? source.SourceReference : draft.SourceReference;
        var existing = defect switch
        {
            "expired" => proposed with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) },
            "expiry-change" => proposed with { ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) },
            _ => proposed with { Layout = [new("changed-node", 10, 20)] }
        };
        await using (var seed = Open()) await seed.SourceReferences.SaveAsync(existing);
        await using (var owner = Open())
        {
            if (sourceOwned) await Assert.ThrowsAsync<InvalidOperationException>(() => owner.SourceCommand.ExecuteAsync(source));
            else await Assert.ThrowsAsync<InvalidOperationException>(() => owner.Command.ExecuteAsync(draft));
        }
        await using var fresh = Open();
        Assert.Null(await fresh.Templates.FindAsync(proposed.ArtifactId));
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(existing, (await fresh.SourceReferences.FindAsync(proposed.SourceReferenceId))!));
        Assert.Null(await ((IActivityDefinitionVersionPublicationStore)fresh.DesignStores).FindAsync(proposed.DefinitionVersionId));
        Assert.Null(await fresh.Receipts.FindAsync(null, draft.Receipt.IdempotencyKey));
        Assert.Equal(ActivityDefinitionDraftStatus.Active, (await ((IActivityDefinitionDraftStore)fresh.DesignStores).FindAsync("draft-1"))!.Status);
        Assert.Empty(await fresh.Runtime.ExecutableActivityTemplateHashClaims.AsNoTracking().ToArrayAsync());
    }
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Production_publication_commands_preserve_caller_Runtime_writes_and_transactions(bool sourceOwned, bool dirty)
    {
        var draft = Commit(); var source = SourceCommit(Published);
        var template = sourceOwned ? source.ExecutableTemplate : draft.ExecutableTemplate;
        await using (var owner = Open())
        {
            await owner.Templates.SaveAsync(template);
            var tracked = await owner.Runtime.ExecutableActivityTemplates.SingleAsync();
            if (dirty)
            {
                tracked.Revision++;
                await RefuseAsync(); Assert.True(owner.Runtime.ChangeTracker.HasChanges());
            }
            else
            {
                await using var transaction = await owner.Runtime.Database.BeginTransactionAsync();
                await RefuseAsync(); Assert.Same(transaction, owner.Runtime.Database.CurrentTransaction);
                Assert.Equal(EntityState.Unchanged, owner.Runtime.Entry(tracked).State);
            }
            async Task RefuseAsync()
            {
                if (sourceOwned) await Assert.ThrowsAsync<InvalidOperationException>(() => owner.SourceCommand.ExecuteAsync(source));
                else await Assert.ThrowsAsync<InvalidOperationException>(() => owner.Command.ExecuteAsync(draft));
            }
        }
        await using var fresh = Open();
        var proposed = sourceOwned ? source.SourceReference : draft.SourceReference;
        Assert.Null(await fresh.SourceReferences.FindAsync(proposed.SourceReferenceId));
        Assert.Equal(1, (await fresh.Runtime.ExecutableActivityTemplates.SingleAsync()).Revision);
        Assert.Null(await ((IActivityDefinitionVersionPublicationStore)fresh.DesignStores).FindAsync(proposed.DefinitionVersionId));
        Assert.Null(await fresh.Receipts.FindAsync(null, draft.Receipt.IdempotencyKey));
    }

}
