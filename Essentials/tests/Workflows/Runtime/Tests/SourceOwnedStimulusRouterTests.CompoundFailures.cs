using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class SourceOwnedStimulusRouterTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task Compound_failure_preserves_saved_source_or_original_direct_exception_and_accepted_ack_ambiguity(bool saved, bool ackLost)
    {
        var primary = new ArgumentException("Constructed hard actor activation refusal.");
        var secondary = new IOException("Constructed failure-observation outage.");
        var recorder = new ObservationRecorder { Failure = observation => observation.Event.Name == "DeliveryFaulted" ? secondary : null };
        await using var harness = new Harness(observations: recorder);
        var (source, expected) = await PrepareCompoundRecipient(harness);
        var request = new BookmarkResumeDispatchRequest("existing", "event", "named", source.Intent.Payload, "direct-compound", "test")
            { ExpectedRecipient = expected };
        if (ackLost) harness.Actors.LoseNextAck = true;
        else harness.Actors.BeforeAgent = _ => Task.FromException(primary);

        var observed = await Record.ExceptionAsync(async () =>
        {
            if (saved) await harness.Resume.DispatchAsync(source, "existing", "wait");
            else await harness.Resume.DispatchAsync(request);
        });
        Assert.NotNull(observed);
        Exception retainedPrimary;
        Exception retainedSecondary;
        if (saved)
        {
            var boundary = Assert.IsType<StimulusDeliveryBoundaryException>(observed);
            Assert.Equal(source.SourceOutboxItemId, boundary.SourceOutboxItemId);
            Assert.Equal(ackLost, boundary.IsUnavailable);
            var compound = Assert.IsType<AggregateException>(boundary.InnerException);
            Assert.Equal(2, compound.InnerExceptions.Count);
            retainedPrimary = compound.InnerExceptions[0]; retainedSecondary = compound.InnerExceptions[1];
        }
        else
        {
            retainedPrimary = observed;
            var entries = Assert.IsAssignableFrom<IReadOnlyList<object>>(observed.Data[RuntimeHistoryDeliveryFacts.ObservationFailureDataKey]);
            var entry = Assert.IsType<KeyValuePair<WeakReference<object>, Exception>>(Assert.Single(entries));
            Assert.True(entry.Key.TryGetTarget(out var invocation)); Assert.Same(request, invocation);
            retainedSecondary = entry.Value;
        }
        Assert.Same(secondary, Assert.IsType<RuntimeSchedulerWorkUnavailableException>(retainedSecondary).InnerException);
        if (ackLost)
        {
            Assert.IsType<IOException>(retainedPrimary);
            Assert.Equal("Constructed acknowledgment loss after recorded mailbox acceptance.", retainedPrimary.Message);
            Assert.Equal(Assert.Single(harness.Actors.Attempts), Assert.Single(harness.Actors.Accepted));
        }
        else { Assert.Same(primary, retainedPrimary); Assert.Empty(harness.Actors.Attempts); }
        Assert.False(string.IsNullOrWhiteSpace(retainedPrimary.StackTrace));
        Assert.Single(recorder.Observations, observation => observation.Event.Name == "DeliveryReceived");
        Assert.DoesNotContain(recorder.Observations, observation => observation.Event.Name is "ActivityFaulted" or "BookmarkConsumed" or "ActivityContinuationStarted");
        var held = (await harness.Store.FindAsync(source.SourceOutboxItemId))!;
        Assert.Equal(0, held.DeliveryAttemptCount); Assert.Null(held.DeliveredAt);
        Assert.Equal(expected.RegistrationCapture!.Generation, (await harness.Bookmarks.ObserveAsync("existing", "wait"))!.Generation);
    }

    [Fact]
    public async Task Compound_failure_keeps_no_inner_integrity_contradiction_hard_even_when_original_delivery_is_unavailable()
    {
        var primary = new IOException("Constructed actor outage.");
        var secondary = new RuntimeHistoryIntegrityException("existing", "contradictory retained sequence");
        Assert.Null(secondary.InnerException);
        var recorder = new ObservationRecorder { Failure = observation => observation.Event.Name == "DeliveryFaulted" ? secondary : null };
        await using var harness = new Harness(observations: recorder);
        var (source, _) = await PrepareCompoundRecipient(harness);
        harness.Actors.BeforeAgent = _ => Task.FromException(primary);
        var observed = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => harness.Resume.DispatchAsync(source, "existing", "wait").AsTask());
        Assert.False(observed.IsUnavailable);
        var compound = Assert.IsType<AggregateException>(observed.InnerException);
        Assert.Same(primary, compound.InnerExceptions[0]); Assert.Same(secondary, compound.InnerExceptions[1]);
        Assert.Equal(source.SourceOutboxItemId, observed.SourceOutboxItemId);
        Assert.Empty(harness.Actors.Attempts); Assert.Equal(0, (await harness.Store.FindAsync(source.SourceOutboxItemId))!.DeliveryAttemptCount);
    }

    [Fact]
    public async Task Compound_failure_preserves_direct_history_unavailable_type_code_and_safe_mapping_summary()
    {
        var primary = new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The original callback dependency is unavailable.");
        var secondary = new IOException("Constructed secondary observation outage.");
        var recorder = new ObservationRecorder { Failure = observation => observation.Event.Name == "DeliveryFaulted" ? secondary : null };
        await using var harness = new Harness(observations: recorder);
        var (source, expected) = await PrepareCompoundRecipient(harness);
        var request = new BookmarkResumeDispatchRequest("existing", "event", "named", source.Intent.Payload, "direct-unavailable", "test")
            { ExpectedRecipient = expected };
        harness.Actors.BeforeAgent = _ => Task.FromException(primary);
        var observed = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => harness.Resume.DispatchAsync(request).AsTask());
        Assert.Same(primary, observed);
        Assert.Equal("runtime.history.capture-unavailable", observed.Code);
        Assert.Equal("The original callback dependency is unavailable.", observed.SafeSummary);
        Assert.IsAssignableFrom<IReadOnlyList<object>>(observed.Data[RuntimeHistoryDeliveryFacts.ObservationFailureDataKey]);
    }

    [Theory]
    [InlineData("requested-cancellation")] [InlineData("unrequested-cancellation")] [InlineData("stale-source")]
    public async Task Compound_failure_never_attempts_failure_observation_for_primary_cancellation_or_stale_claim(string kind)
    {
        using var stop = new CancellationTokenSource();
        var faultWrites = 0;
        var recorder = new ObservationRecorder { Failure = observation =>
        {
            if (observation.Event.Name != "DeliveryFaulted") return null;
            faultWrites++; return new IOException("Must not replace primary protocol control flow.");
        } };
        await using var harness = new Harness(observations: recorder);
        var (source, _) = await PrepareCompoundRecipient(harness);
        Exception primary = kind == "stale-source" ? new RuntimePostCommitOutboxStaleClaimException(source.SourceOutboxItemId,
            source.Claim.OwnerId, source.Claim.FencingToken, "successor", source.Claim.FencingToken + 1, RuntimePostCommitOutboxStatus.Delivering)
            : new OperationCanceledException("Constructed original cancellation.", null, stop.Token);
        harness.Actors.BeforeAgent = _ =>
        {
            if (kind == "requested-cancellation") stop.Cancel();
            return Task.FromException(primary);
        };
        var observed = await Record.ExceptionAsync(() => harness.Resume.DispatchAsync(source, "existing", "wait", stop.Token).AsTask());
        if (kind == "unrequested-cancellation")
            Assert.Same(primary, Assert.IsType<StimulusDeliveryBoundaryException>(observed).InnerException);
        else Assert.Same(primary, observed);
        Assert.Equal(0, faultWrites); Assert.Empty(harness.Actors.Attempts);
        Assert.Equal(0, (await harness.Store.FindAsync(source.SourceOutboxItemId))!.DeliveryAttemptCount);
    }

    [Fact]
    public async Task Compound_failure_diagnostics_append_on_reused_direct_exception_without_promoting_old_request_entries()
    {
        var primary = new ArgumentException("Constructed reusable direct refusal.");
        var secondary = new IOException("Constructed first failure-observation outage.");
        var fail = true;
        var recorder = new ObservationRecorder { Failure = observation => fail && observation.Event.Name == "DeliveryFaulted" ? secondary : null };
        await using var harness = new Harness(observations: recorder);
        var (source, expected) = await PrepareCompoundRecipient(harness);
        var first = new BookmarkResumeDispatchRequest("existing", "event", "named", source.Intent.Payload, "first", "test") { ExpectedRecipient = expected };
        var second = new BookmarkResumeDispatchRequest("existing", "event", "named", source.Intent.Payload, "second", "test") { ExpectedRecipient = expected };
        harness.Actors.BeforeAgent = _ => Task.FromException(primary);
        Assert.Same(primary, await Record.ExceptionAsync(() => harness.Resume.DispatchAsync(first).AsTask()));
        var old = Assert.IsAssignableFrom<IReadOnlyList<object>>(primary.Data[RuntimeHistoryDeliveryFacts.ObservationFailureDataKey]);
        fail = false; harness.Actors.BeforeAgent = _ => Task.FromException(primary);
        Assert.Same(primary, await Record.ExceptionAsync(() => harness.Resume.DispatchAsync(second).AsTask()));
        Assert.Same(old, primary.Data[RuntimeHistoryDeliveryFacts.ObservationFailureDataKey]);
        var oldEntry = Assert.IsType<KeyValuePair<WeakReference<object>, Exception>>(Assert.Single(old));
        Assert.True(oldEntry.Key.TryGetTarget(out var prior)); Assert.Same(first, prior); Assert.NotSame(second, prior);
        fail = true; harness.Actors.BeforeAgent = _ => Task.FromException(primary);
        Assert.Same(primary, await Record.ExceptionAsync(() => harness.Resume.DispatchAsync(second).AsTask()));
        var all = Assert.IsAssignableFrom<IReadOnlyList<object>>(primary.Data[RuntimeHistoryDeliveryFacts.ObservationFailureDataKey]);
        Assert.Equal(2, all.Count); Assert.Same(old[0], all[0]);
        Assert.Equal(4, recorder.Observations.Count); // Three receipts and the successful second failure disposition.
    }

    private static async Task<(PostCommitDeliveryContext Source, StimulusResumeRecipient Expected)> PrepareCompoundRecipient(Harness harness)
    {
        await harness.SeedMaterial(); await harness.Wait("existing", "wait");
        var owner = (await harness.Executions.FindAsync("existing"))! with
            { HistoryCapturePolicy = new("runtime-history-v3", 3, true, RuntimeHistoryDeliveryFacts.Kinds) };
        await harness.Executions.SaveAsync(owner);
        var source = await harness.Source(historyCausation: new("publisher-event", "publisher", "source-commit:send"));
        var expected = StimulusResumeRecipient.From((await harness.Bookmarks.ObserveAsync("existing", "wait"))!, owner, Scope);
        await harness.Store.InstallAsync(source, new(1, source.SourceOutboxItemId, source.IntentFingerprint,
            StimulusDeliveryScope.From((await harness.Executions.FindAsync("publisher"))!, Scope), Now, "event", "named", [], [expected]));
        return (source, expected);
    }
}
