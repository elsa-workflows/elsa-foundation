using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class StimulusIngressSourceStoreTests
{
    /// <summary>Creation and replay only; native resumption/restart remains separate qualification.</summary>
    [Fact]
    public async Task Resume_only_source_requires_no_start_binding_and_replay_does_not_add_a_later_wait()
    {
        await using var fixture = await Fixture.Create();
        var original = new List<StimulusResumeRecipient>();
        var registrations = new List<BookmarkStateObservation>();
        await using (var context = fixture.Open())
        {
            await fixture.Material(context).SaveAsync(Material());
            foreach (var executionId in new[] { "first", "second", "later" })
                await fixture.Executions(context).SaveAsync(Execution(executionId) with
                {
                    Status = WorkflowExecutionStatus.Running, CompletedAt = null,
                    Authority = WorkflowExecutionAuthoritySnapshot.CreateRoot("test")
                });
            original.Add(await RegisterWait(context, "first"));
            original.Add(await RegisterWait(context, "second"));
            await fixture.SourceStore(context).InstallAsync(ResumeSource("source", original));
            Assert.Empty(await context.WorkflowTriggerBindings.AsNoTracking().ToArrayAsync());
            Assert.Empty(await context.WorkflowExecutableSourceReferences.AsNoTracking().ToArrayAsync());
        }
        await using var reopened = fixture.Open();
        var later = await RegisterWait(reopened, "later");
        var winner = await fixture.SourceStore(reopened).InstallAsync(ResumeSource("retry", [.. original, later]));
        Assert.Equal("source", winner.SourceId);
        Assert.Equal(StimulusRoutingMode.ResumeOnly, winner.Request.Mode);
        Assert.Null(winner.Request.SelectionOwnerTriggerBindingId);
        Assert.Empty(winner.Cohort.Starts);
        Assert.Equal(new[] { "first", "second" }, winner.GetExecutionDependencies());
        Assert.Equal(original.Select(wait => wait.BookmarkId), winner.Cohort.Resumes.Select(wait => wait.BookmarkId));
        Assert.Equal(original.Select(wait => wait.RegistrationCapture!.Generation), winner.Cohort.Resumes.Select(wait => wait.RegistrationCapture!.Generation));
        Assert.DoesNotContain(winner.Cohort.Resumes, wait => wait.BookmarkId == later.BookmarkId);
        Assert.Equal(3, await reopened.Bookmarks.CountAsync());
        var currentBookmarks = new EfBookmarkStateStore(reopened, new Accessor());
        foreach (var expected in registrations)
        {
            var current = await currentBookmarks.ObserveAsync(expected.State.WorkflowExecutionId, expected.State.BookmarkId);
            Assert.NotNull(current);
            Assert.Equal(expected.Scope, current.Scope);
            Assert.Equal(expected.Generation, current.Generation);
            Assert.Equal(RuntimeArtifactJson.Serialize(expected.State), RuntimeArtifactJson.Serialize(current.State));
            Assert.Equal(WorkflowExecutionStatus.Running,
                (await fixture.Executions(reopened).FindAsync(expected.State.WorkflowExecutionId))!.Status);
        }
        Assert.Single(await reopened.StimulusIngressSources.AsNoTracking().ToArrayAsync());

        async Task<StimulusResumeRecipient> RegisterWait(RuntimeDbContext context, string executionId)
        {
            var bookmarks = new EfBookmarkStateStore(context, new Accessor());
            var bookmark = new BookmarkState("wait-" + executionId, executionId, "activity-" + executionId,
                "node", "resume", "event", "hash", System.Text.Json.JsonSerializer.SerializeToElement(new { wait = executionId }),
                new Dictionary<string, string> { ["registration"] = executionId }, Now, null);
            await bookmarks.SaveAsync(bookmark);
            var observation = (await bookmarks.ObserveAsync(executionId, bookmark.BookmarkId))!;
            registrations.Add(observation);
            var execution = (await fixture.Executions(context).FindAsync(executionId))!;
            return StimulusResumeRecipient.From(observation, execution, Scope);
        }
    }

    private static StimulusIngressSource ResumeSource(string sourceId, IReadOnlyList<StimulusResumeRecipient> waits) =>
        new(1, sourceId, Origin, "resume-only-key",
            new(1, "event", "hash", null, null, StimulusRoutingMode.ResumeOnly, "test", new Dictionary<string, string>(),
                null, null, null, StimulusStartKeyScope.Artifact, false, null), Now,
            new(Scope, Now, [], waits, [], waits.Select(wait => new StimulusIngressLegacyResolution(wait.WorkflowExecutionId,
                StimulusIngressLegacyMode.SingleMatch, StimulusIngressLegacyStatus.Resolved, [wait.BookmarkId], [wait.BookmarkId])).ToArray()));
}
