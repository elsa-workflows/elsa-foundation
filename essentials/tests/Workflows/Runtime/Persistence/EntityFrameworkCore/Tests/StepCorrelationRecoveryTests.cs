using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Xunit;
using C = Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.RuntimeCheckpointCommitContract;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    [Fact]
    public async Task Step_identity_checkpoint_preserves_captured_wait_and_registration_keys_after_restart()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = C.Bookmark("earlier") with { Metadata = new Dictionary<string, string> { [RuntimeMetadataKeys.CorrelationId] = "old" } };
        var registration = new ActivityTriggerRegistration("registration", "wait", "resume", new ValueTypeDescriptor("Any"),
            original.StimulusType, original.StimulusHash, ActivityTriggerDeduplicationPolicy.Once, original.Metadata);
        var wait = C.Activity("wait") with { Status = ActivityExecutionStatus.Suspended, TriggerRegistrations = [registration] };
        await using (var context = database.Open("tenant-a"))
        {
            var access = new FixedAccessor("tenant-a");
            await new EfBookmarkStateStore(context, access).SaveAsync(original);
            await new EfActivityExecutionStateStore(context, access, new NoopContinuationCodec()).SaveAsync(wait);
            await new EfRuntimeCheckpointCommitStore(context, access, new FixedTimeProvider(OccurredAt), new PassThroughRootWriteLeaseManager())
                .CommitAsync(StepStateCommit("identity-change"), Decision());
            await new EfBookmarkStateStore(context, access).SaveAsync(C.Bookmark("later") with
                { Metadata = new Dictionary<string, string> { [RuntimeMetadataKeys.CorrelationId] = "independent" } });
        }
        await using var restarted = database.Open("tenant-a");
        var restartedAccess = new FixedAccessor("tenant-a");
        var bookmarks = new EfBookmarkStateStore(restarted, restartedAccess);
        var retained = await bookmarks.FindAsync("workflow-a", "earlier");
        Assert.Equal("old", retained!.Metadata[RuntimeMetadataKeys.CorrelationId]);
        var retainedWait = await new EfActivityExecutionStateStore(restarted, restartedAccess, new NoopContinuationCodec()).FindAsync("workflow-a", "wait");
        Assert.Equal("old", Assert.Single(retainedWait!.TriggerRegistrations!).Metadata[RuntimeMetadataKeys.CorrelationId]);
        var lookup = new GlobalBookmarkStimulusLookup(bookmarks);
        async Task<string[]> Matches(string key) => (await lookup.FindWaitingAsync(new GlobalBookmarkStimulusLookupRequest(
            original.StimulusType, original.StimulusHash, OccurredAt, key))).Matches.Select(bookmark => bookmark.BookmarkId).ToArray();
        Assert.Equal(["earlier"], await Matches("old"));
        Assert.Equal(["later"], await Matches("independent"));
        Assert.Empty(await Matches("new"));
        await AssertStepState(restarted);
    }
}
