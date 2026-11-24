using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Publishing.Releases.NativeCapture.Tests;

/// <summary>
/// Spec 1239: Graph composites a reconciliation source owns publish when they use other activity versions, through the
/// actual source-owned bridge, Graph provider, template compiler and EF source commit. S uses the CLR WriteLine, T uses S.
/// </summary>
public sealed class SourceOwnedGraphDependencyTests
{
    [Fact]
    public async Task Source_owned_composites_that_use_clr_and_composite_versions_publish_with_edges_and_projection()
    {
        await using var f = await PublishedNativeFixture.Create();
        var writeLine = await f.PublishClrCatalog();

        var s = await f.PublishSourceOwnedGraph("source-s", "Migrated.S", writeLine, childIsWriteLine: true);
        var t = await f.PublishSourceOwnedGraph("source-t", "Migrated.T", "source-s", childIsWriteLine: false);

        Assert.True(s.IsPublished, string.Join(",", s.Diagnostics.Select(x => x.Code)));
        Assert.True(t.IsPublished, string.Join(",", t.Diagnostics.Select(x => x.Code)));
        var db = f.Service<ActivitiesDesignDbContext>();
        db.ChangeTracker.Clear();
        var edges = await db.ActivityDependencyEdges.AsNoTracking().OrderBy(x => x.OwnerVersionId).ToListAsync();
        Assert.Equal([("source-s", writeLine), ("source-t", "source-s")], edges.Select(x => (x.OwnerVersionId, x.DependencyVersionId)));
        var publications = await db.ActivityDefinitionVersionPublications.AsNoTracking().ToDictionaryAsync(x => x.DefinitionVersionId);
        Assert.All(edges, edge => Assert.Equal(publications[edge.OwnerVersionId].TemplateHash, edge.OwnerTemplateHash));
        Assert.All(edges, edge => Assert.Equal(publications[edge.DependencyVersionId].TemplateHash, edge.DependencyTemplateHash));

        var projection = Assert.Single(await db.ActivityDependencyProjections.AsNoTracking().ToListAsync());
        Assert.Equal(
            [("ActivityVersion", "source-s", writeLine), ("ActivityVersion", "source-t", "source-s")],
            projection.Items.Select(x => (x.Owner.Kind, x.Owner.VersionId!, x.Dependency.VersionId!)).OrderBy(x => x.Item2, StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_composite_tried_before_the_composite_it_uses_is_refused_then_published_once_that_one_is()
    {
        // The order a reconciliation pass with T listed before S tries them in: T, S, then T again in a retry round.
        await using var f = await PublishedNativeFixture.Create();
        var writeLine = await f.PublishClrCatalog();

        var early = await f.PublishSourceOwnedGraph("source-t", "Migrated.T", "source-s", childIsWriteLine: false);
        var s = await f.PublishSourceOwnedGraph("source-s", "Migrated.S", writeLine, childIsWriteLine: true);
        var retried = await f.PublishSourceOwnedGraph("source-t", "Migrated.T", "source-s", childIsWriteLine: false);

        Assert.False(early.IsPublished);
        Assert.Contains(early.Diagnostics, x => x.Code == "activity.dependency.version-not-found");
        Assert.True(s.IsPublished);
        Assert.True(retried.IsPublished, string.Join(",", retried.Diagnostics.Select(x => x.Code)));
        var db = f.Service<ActivitiesDesignDbContext>();
        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.ActivityDependencyEdges.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task A_source_owned_composite_that_uses_an_unknown_version_is_refused_and_nothing_is_stored()
    {
        await using var f = await PublishedNativeFixture.Create();

        var result = await f.PublishSourceOwnedGraph("source-w", "Migrated.W", "no-such-version", childIsWriteLine: false);

        Assert.False(result.IsPublished);
        var diagnostic = Assert.Single(result.Diagnostics, x => x.Code == "activity.dependency.version-not-found");
        Assert.Contains("no-such-version", diagnostic.Message, StringComparison.Ordinal);
        var db = f.Service<ActivitiesDesignDbContext>();
        Assert.Empty(await db.ActivityDefinitionVersionPublications.AsNoTracking().ToListAsync());
        Assert.Empty(await db.ActivityDependencyEdges.AsNoTracking().ToListAsync());
        Assert.Empty(await f.Runtime.ExecutableActivityTemplates.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Publishing_the_same_source_owned_composite_again_converges_without_new_edges()
    {
        await using var f = await PublishedNativeFixture.Create();
        var writeLine = await f.PublishClrCatalog();

        var first = await f.PublishSourceOwnedGraph("source-s", "Migrated.S", writeLine, childIsWriteLine: true);
        var again = await f.PublishSourceOwnedGraph("source-s", "Migrated.S", writeLine, childIsWriteLine: true);

        Assert.True(first.IsPublished);
        Assert.True(again.IsPublished);
        var db = f.Service<ActivitiesDesignDbContext>();
        db.ChangeTracker.Clear();
        Assert.Single(await db.ActivityDependencyEdges.AsNoTracking().ToListAsync());
    }
}
