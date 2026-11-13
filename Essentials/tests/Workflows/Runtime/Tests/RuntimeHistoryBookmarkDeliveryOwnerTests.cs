using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Actual dispatcher/creation/observation owners with a controlled actor handoff, not activity callback/drainer/HTTP/EF proof.</summary>
public sealed class RuntimeHistoryBookmarkDeliveryOwnerTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch.AddDays(1);
    private static readonly WorkflowExecutableIdentity Pin = new("artifact", "definition", "version", "1", "hash");

    [Theory]
    [InlineData(WorkflowExecutionCommandDispatchStatus.Accepted, "DeliveryDispatched", "Dispatched")]
    [InlineData(WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted, "DeliveryDispatched", "accepted-but-faulted")]
    [InlineData(WorkflowExecutionCommandDispatchStatus.Duplicate, "DeliveryDuplicate", "Duplicate")]
    [InlineData(WorkflowExecutionCommandDispatchStatus.Rejected, "DeliveryRejected", "Rejected")]
    [InlineData(WorkflowExecutionCommandDispatchStatus.Deferred, "DeliveryDeferred", "Deferred")]
    public async Task Records_actual_dispatcher_handoff_result_and_receipt_cause_without_inventing_native_resumption(
        WorkflowExecutionCommandDispatchStatus status, string name, string reason)
    {
        using var f = await Fixture.CreateAsync(coalescing: true);
        await f.CreateWaitAsync("wait");
        f.Actors.Actor.Status = status;
        f.Actors.Actor.BeforeEnqueue = async () =>
        {
            Assert.Single((await f.History.ReadPageAsync(new("run"))).Items, e => e.Name == "DeliveryReceived");
        };
        var nativeCommits = f.Commits.ListCommits().Count;
        await f.Dispatcher.DispatchAsync(Request());
        var page = await f.History.ReadPageAsync(new("run"));
        var received = Assert.Single(page.Items, e => e.Name == "DeliveryReceived");
        var result = Assert.Single(page.Items, e => e.Name == name);
        Assert.True(received.Sequence < result.Sequence);
        Assert.Equal(received.HistoryEventId, result.Causation!.HistoryEventId);
        Assert.Equal(reason, result.Metadata["history.reasonCode"]);
        var envelope = Assert.Single(f.Actors.Actor.Envelopes);
        var payload = envelope.Command.Payload!.Value.Deserialize<RuntimeResumeBookmarkCommandPayload>()!;
        Assert.Equal(received.HistoryEventId, payload.HistoryCausation!.HistoryEventId);
        Assert.Equal("run", payload.HistoryCausation.WorkflowExecutionId);
        Assert.Equal(received.Causation!.DeliveryId, payload.HistoryCausation.DeliveryId);
        Assert.Equal(nativeCommits, f.Commits.ListCommits().Count);
        Assert.NotNull(await f.Bookmarks.FindAsync("run", "wait"));
        Assert.Equal(RuntimeHistoryBookmarkStatus.Active, (await f.History.FindBookmarkAsync("run", "wait"))!.Status);
        Assert.DoesNotContain(page.Items, e => e.Name is "BookmarkConsumed" or "ActivityResumeClaimed" or "ActivityCompleted" or "WorkflowFaulted");
        Assert.DoesNotContain("private", JsonSerializer.Serialize(page));
    }

    [Theory]
    [InlineData("removed", "DeliveryUnmatched")]
    [InlineData("expired", "DeliveryUnmatched")]
    [InlineData("ambiguous", "DeliveryRejected")]
    public async Task Known_run_unmatched_or_ambiguous_receipts_never_activate_an_actor_or_infer_consumption(string scenario, string disposition)
    {
        using var f = await Fixture.CreateAsync();
        await f.CreateWaitAsync("wait", scenario == "expired" ? At.AddMinutes(1) : null);
        if (scenario == "removed") await f.Bookmarks.DeleteAsync("run", "wait");
        if (scenario == "expired") f.Clock.Advance(TimeSpan.FromMinutes(2));
        if (scenario == "ambiguous") await f.CreateWaitAsync("competing");
        await f.Dispatcher.DispatchAsync(Request());
        var page = await f.History.ReadPageAsync(new("run"));
        Assert.Single(page.Items, e => e.Name == "DeliveryReceived");
        Assert.Single(page.Items, e => e.Name == disposition);
        Assert.Empty(f.Actors.Activations);
        Assert.DoesNotContain(page.Items, e => e.Name is "BookmarkConsumed" or "BookmarkRetired" or "ActivityResumeClaimed");
        Assert.Equal(RuntimeHistoryBookmarkStatus.Active, (await f.History.FindBookmarkAsync("run", "wait"))!.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_receipt_acknowledgement_prevents_handoff_even_when_the_append_already_committed(bool afterAppend)
    {
        using var f = await Fixture.CreateAsync();
        await f.CreateWaitAsync("wait");
        var failure = new IOException("private receipt failure");
        f.Probe.Writer!.Fail[1] = failure;
        f.Probe.Writer.FailAfterAppend = afterAppend;
        var error = await Assert.ThrowsAsync<RuntimeHistoryUnavailableException>(async () => await f.Dispatcher.DispatchAsync(Request()));
        Assert.Same(failure, error.InnerException);
        Assert.Empty(f.Actors.Activations);
        Assert.NotNull(await f.Bookmarks.FindAsync("run", "wait"));
        var events = (await f.History.ReadPageAsync(new("run"))).Items;
        Assert.Equal(afterAppend ? 1 : 0, events.Count(e => e.Name == "DeliveryReceived"));
        Assert.DoesNotContain(events, e => e.Name is "DeliveryDispatched" or "BookmarkConsumed");
    }

    [Fact]
    public async Task Handoff_exception_records_only_a_delivery_failure_and_rethrows_the_same_primary_exception()
    {
        using var f = await Fixture.CreateAsync();
        await f.CreateWaitAsync("wait");
        var primary = new InvalidOperationException("private dispatch exception");
        f.Actors.Actor.Failure = primary;
        Assert.Same(primary, await Record.ExceptionAsync(async () => await f.Dispatcher.DispatchAsync(Request())));
        var page = await f.History.ReadPageAsync(new("run"));
        var received = Assert.Single(page.Items, e => e.Name == "DeliveryReceived");
        var fault = Assert.Single(page.Items, e => e.Name == "DeliveryFaulted");
        Assert.Equal(received.HistoryEventId, fault.Causation!.HistoryEventId);
        Assert.Equal("dispatch-threw", fault.Metadata["history.reasonCode"]);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(page));
        Assert.DoesNotContain(page.Items, e => e.Name is "ActivityFaulted" or "WorkflowFaulted" or "BookmarkConsumed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Secondary_failure_retains_exact_invocation_diagnostics_without_replacing_the_primary_or_trusting_old_data(bool opaquePrior)
    {
        using var f = await Fixture.CreateAsync();
        await f.CreateWaitAsync("wait");
        var primary = new RuntimePostCommitSourceBoundaryException("source", RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
            new IOException("private underlying detail"), isUnavailable: false);
        var opaque = new object();
        if (opaquePrior) primary.Data[RuntimeHistoryDeliveryObservations.FailureDataKey] = opaque;
        f.Actors.Actor.Failure = primary;
        var secondary = new IOException("private diagnostic failure");
        f.Probe.Writer!.Fail[2] = secondary;
        var request = Request();
        Assert.Same(primary, await Record.ExceptionAsync(async () => await f.Dispatcher.DispatchAsync(request)));
        var diagnostics = Assert.IsAssignableFrom<IReadOnlyList<object>>(primary.Data[RuntimeHistoryDeliveryObservations.FailureDataKey]);
        if (opaquePrior) Assert.Same(opaque, diagnostics[0]);
        var pair = Assert.IsType<KeyValuePair<WeakReference<object>, Exception>>(diagnostics[^1]);
        Assert.True(pair.Key.TryGetTarget(out var actual));
        Assert.Same(request, actual);
        Assert.Same(secondary, pair.Value.InnerException);
        Assert.False(primary.IsUnavailable);
        var page = await f.History.ReadPageAsync(new("run"));
        Assert.Single(page.Items, e => e.Name == "DeliveryReceived");
        Assert.DoesNotContain(page.Items, e => e.Name is "DeliveryFaulted" or "BookmarkConsumed");
    }

    [Theory]
    [InlineData(false, "uncaptured")]
    [InlineData(true, "uncaptured")]
    [InlineData(false, "core-only")]
    [InlineData(true, "core-only")]
    public async Task Old_admission_profiles_never_activate_delivery_observer_factories(bool coalescing, string profile)
    {
        using var f = await Fixture.CreateAsync(coalescing, profile);
        await f.CreateWaitAsync("wait");
        Assert.Equal(0, f.Probe.FactoryCalls);
        await f.Dispatcher.DispatchAsync(Request());
        Assert.Single(f.Actors.Actor.Envelopes);
        Assert.Equal(0, f.Probe.FactoryCalls);
        Assert.DoesNotContain((await f.History.ReadPageAsync(new("run"))).Items, e => e.Authority == RuntimeHistoryAuthority.DeliveryObservation);
    }

    [Fact]
    public async Task A_declared_receipt_only_profile_does_not_write_unsupported_dispositions_after_handoff()
    {
        using var f = await Fixture.CreateAsync(profile: "receipt-only");
        await f.CreateWaitAsync("wait");
        await f.Dispatcher.DispatchAsync(Request());
        var observations = (await f.History.ReadPageAsync(new("run"))).Items.Where(e => e.Authority == RuntimeHistoryAuthority.DeliveryObservation);
        Assert.Equal("DeliveryReceived", Assert.Single(observations).Name);
        Assert.Single(f.Actors.Actor.Envelopes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_disposition_ack_does_not_erase_the_handoff_or_claim_that_the_native_wait_resumed(bool afterAppend)
    {
        using var f = await Fixture.CreateAsync();
        await f.CreateWaitAsync("wait");
        var failure = new IOException("private disposition acknowledgment");
        f.Probe.Writer!.Fail[2] = failure;
        f.Probe.Writer.FailAfterAppend = afterAppend;
        var error = await Assert.ThrowsAsync<RuntimeHistoryUnavailableException>(async () => await f.Dispatcher.DispatchAsync(Request()));
        Assert.Same(failure, error.InnerException);
        Assert.Single(f.Actors.Actor.Envelopes);
        var events = (await f.History.ReadPageAsync(new("run"))).Items;
        Assert.Single(events, e => e.Name == "DeliveryReceived");
        Assert.Equal(afterAppend ? 1 : 0, events.Count(e => e.Name == "DeliveryDispatched"));
        Assert.DoesNotContain(events, e => e.Name is "DeliveryFaulted" or "BookmarkConsumed" or "ActivityResumeClaimed");
        Assert.NotNull(await f.Bookmarks.FindAsync("run", "wait"));
    }

    [Fact]
    public async Task Redelivery_has_a_new_receipt_but_keeps_the_logical_delivery_and_native_idempotency_key()
    {
        using var f = await Fixture.CreateAsync();
        await f.CreateWaitAsync("wait");
        await f.Dispatcher.DispatchAsync(Request());
        f.Actors.Actor.Status = WorkflowExecutionCommandDispatchStatus.Duplicate;
        await f.Dispatcher.DispatchAsync(Request());
        var events = (await f.History.ReadPageAsync(new("run"))).Items;
        var receipts = events.Where(e => e.Name == "DeliveryReceived").OrderBy(e => e.Sequence).ToArray();
        Assert.Equal(2, receipts.Length);
        Assert.NotEqual(receipts[0].HistoryEventId, receipts[1].HistoryEventId);
        Assert.Equal(receipts[0].Causation!.DeliveryId, receipts[1].Causation!.DeliveryId);
        Assert.Equal(receipts[1].HistoryEventId, Assert.Single(events, e => e.Name == "DeliveryDuplicate").Causation!.HistoryEventId);
        Assert.Equal(2, f.Actors.Actor.Envelopes.Count);
        Assert.Equal(f.Actors.Actor.Envelopes[0].IdempotencyKey, f.Actors.Actor.Envelopes[1].IdempotencyKey);
        Assert.NotNull(await f.Bookmarks.FindAsync("run", "wait"));
    }

    [Fact]
    public async Task Requested_handoff_cancellation_propagates_without_inventing_a_delivery_fault()
    {
        using var f = await Fixture.CreateAsync();
        await f.CreateWaitAsync("wait");
        using var cancellation = new CancellationTokenSource();
        var primary = new OperationCanceledException(cancellation.Token);
        f.Actors.Actor.BeforeEnqueue = () => { cancellation.Cancel(); return ValueTask.CompletedTask; };
        f.Actors.Actor.Failure = primary;
        Assert.Same(primary, await Record.ExceptionAsync(async () =>
            await f.Dispatcher.DispatchAsync(Request(), cancellationToken: cancellation.Token)));
        var events = (await f.History.ReadPageAsync(new("run"))).Items;
        Assert.Single(events, e => e.Name == "DeliveryReceived");
        Assert.DoesNotContain(events, e => e.Name is "DeliveryFaulted" or "DeliveryDispatched");
    }

    [Fact]
    public async Task Wrong_tenant_scope_stops_receipt_and_handoff_without_cross_scope_evidence()
    {
        using var f = await Fixture.CreateAsync();
        await f.CreateWaitAsync("wait");
        f.Access.Current = PersistenceAccessContext.Scoped(new("scope-b"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.Dispatcher.DispatchAsync(Request()));
        Assert.Empty(f.Actors.Activations);
        f.Access.Current = PersistenceAccessContext.Scoped(new("scope-a"));
        Assert.DoesNotContain((await f.History.ReadPageAsync(new("run"))).Items, e => e.Authority == RuntimeHistoryAuthority.DeliveryObservation);
    }

    [Fact]
    public async Task Qualified_owner_pairs_the_exact_selected_writer_and_reader_without_activating_an_actor()
    {
        using var f = await Fixture.CreateAsync();
        using var other = await Fixture.CreateAsync();
        Assert.True(f.Dispatcher.CanRecordHistory(f.Probe.Writer!, f.History));
        Assert.False(f.Dispatcher.CanRecordHistory(other.Probe.Writer!, f.History));
        Assert.False(f.Dispatcher.CanRecordHistory(f.Probe.Writer!, other.History));
        Assert.Empty(f.Actors.Activations);
        Assert.Empty(other.Actors.Activations);
    }

    [Fact]
    public void Legacy_selected_dispatcher_refuses_a_new_required_delivery_profile_before_admission()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPersistenceAccessContextAccessor>(new Access());
        services.AddSingleton<IWorkflowExecutableRootWriteLeaseManager>(PassThroughWorkflowExecutableRootWriteLeaseManager.Instance);
        services.AddWorkflowRuntime();
        RuntimeHistoryRegistration.AddServices(services);
        services.AddSingleton<IBookmarkResumeDispatcher>(new LegacyDispatcher());
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var error = Assert.Throws<RuntimeSchedulerWorkUnavailableException>(() => RuntimeHistoryRegistration.ResolvePolicy(scope.ServiceProvider, "run"));
        Assert.Equal("runtime.history.capture-unavailable", error.Code);
        Assert.Empty(scope.ServiceProvider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
    }

    [Fact]
    public void Absent_request_causation_preserves_the_older_null_omitted_transport_shape()
    {
        Assert.DoesNotContain("HistoryCausation", JsonSerializer.Serialize(Request()));
        var request = new BookmarkResumeDispatchRequest("run", "task", "hash")
            { HistoryCausation = new(WorkflowExecutionId: "parent", HistoryEventId: "source-event") };
        var restored = JsonSerializer.Deserialize<BookmarkResumeDispatchRequest>(JsonSerializer.Serialize(request))!;
        Assert.Equal(request.HistoryCausation, restored.HistoryCausation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Canonical_saved_source_receipts_retain_source_identity_and_only_exact_saved_recipients(bool coalescing)
    {
        using var f = await Fixture.CreateAsync(coalescing);
        await f.CreateWaitAsync("wait");
        var source = await f.SourceAsync();
        var routed = await f.Router.RouteSourceAsync(source);
        Assert.Equal(BookmarkResumeDispatchStatus.Dispatched, Assert.Single(routed.Resumes).Status);
        var page = await f.History.ReadPageAsync(new("run"));
        var received = Assert.Single(page.Items, e => e.Name == "DeliveryReceived");
        Assert.Equal("publisher", received.Causation!.WorkflowExecutionId);
        Assert.Equal(source.SourceOutboxItemId, received.Causation.DeliveryId);
        Assert.Null(received.Causation.HistoryEventId); // No source history event is invented.
        Assert.Equal("wait", received.BookmarkId);
        Assert.Equal("activity", received.ActivityExecutionId);
        var payload = Assert.Single(f.Actors.Actor.Envelopes).Command.Payload!.Value.Deserialize<RuntimeResumeBookmarkCommandPayload>()!;
        Assert.Equal(received.HistoryEventId, payload.HistoryCausation!.HistoryEventId);
        Assert.NotNull(payload.ExpectedBookmarkResume!.RegistrationCapture);
        Assert.Equal(source.Intent.Payload!.Value.GetRawText(), payload.Input!.Value.GetRawText());
        var count = page.Items.Count;
        var failure = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(async () =>
            await f.Dispatcher.DispatchAsync(source, "run", "outside-cohort"));
        Assert.False(failure.IsUnavailable);
        Assert.Equal(count, (await f.History.ReadPageAsync(new("run"))).Items.Count);
        Assert.Single(f.Actors.Actor.Envelopes);
    }

    [Theory]
    [InlineData(false, "captured")] [InlineData(true, "captured")]
    [InlineData(false, "uncaptured")] [InlineData(true, "uncaptured")]
    public async Task Verified_source_origin_reaches_the_actual_recipient_receipt_or_uncaptured_resume_without_retrofit(bool coalescing, string profile)
    {
        using var f = await Fixture.CreateAsync(coalescing, profile);
        await f.CreateWaitAsync("wait");
        var origin = new RuntimeHistoryCausation("publication-event", "publisher", "source:send", "publisher-command", "publisher-work");
        var source = await f.SourceAsync(origin: origin);
        var nativeCommits = f.Commits.ListCommits().Count;
        await f.Router.RouteSourceAsync(source);
        var payload = Assert.Single(f.Actors.Actor.Envelopes).Command.Payload!.Value.Deserialize<RuntimeResumeBookmarkCommandPayload>()!;
        var page = await f.History.ReadPageAsync(new("run"));
        if (profile == "captured")
        {
            var receipt = Assert.Single(page.Items, e => e.Name == "DeliveryReceived");
            Assert.Equal(origin, receipt.Causation);
            Assert.Equal(receipt.HistoryEventId, payload.HistoryCausation!.HistoryEventId);
            Assert.Equal("run", payload.HistoryCausation.WorkflowExecutionId);
            Assert.Equal(source.SourceOutboxItemId, payload.HistoryCausation.DeliveryId);
        }
        else
        {
            Assert.Empty(page.Items); Assert.Null(page.Head);
            Assert.Equal(origin, payload.HistoryCausation);
            Assert.Null((await f.Workflows.FindAsync("run"))!.HistoryCapturePolicy);
        }
        Assert.Equal(nativeCommits, f.Commits.ListCommits().Count);
        Assert.NotNull(await f.Bookmarks.FindAsync("run", "wait"));
        Assert.Equal("wait", payload.ExpectedBookmarkResume!.BookmarkId);
        Assert.DoesNotContain(page.Items, e => e.Name is "BookmarkConsumed" or "ActivityCompleted");
    }

    [Fact]
    public async Task A_retired_saved_wait_receipt_keeps_original_publication_causation_without_inventing_a_resume()
    {
        using var f = await Fixture.CreateAsync(); await f.CreateWaitAsync("wait");
        var origin = new RuntimeHistoryCausation("publication-event", "publisher", "source:send");
        var source = await f.SourceAsync(installRecipient: true, origin: origin);
        await f.Bookmarks.DeleteAsync("run", "wait");
        var result = await f.Dispatcher.DispatchAsync(source, "run", "wait");
        Assert.Equal(BookmarkResumeDispatchStatus.NotFound, result.Status);
        var page = await f.History.ReadPageAsync(new("run"));
        Assert.Equal(origin, Assert.Single(page.Items, e => e.Name == "DeliveryReceived").Causation);
        Assert.Single(page.Items, e => e.Name == "DeliveryUnmatched");
        Assert.Empty(f.Actors.Actor.Envelopes);
        Assert.DoesNotContain(page.Items, e => e.Name == "BookmarkConsumed");
    }

    [Theory]
    [InlineData("removed")]
    [InlineData("expired")]
    [InlineData("replaced")]
    public async Task Verified_saved_source_can_record_an_unmatched_exact_wait_without_retargeting_or_native_effects(string scenario)
    {
        using var f = await Fixture.CreateAsync();
        await f.CreateWaitAsync("wait", scenario == "expired" ? At.AddMinutes(1) : null);
        var source = await f.SourceAsync(installRecipient: true);
        if (scenario == "expired") f.Clock.Advance(TimeSpan.FromMinutes(2));
        else
        {
            var original = (await f.Bookmarks.FindAsync("run", "wait"))!;
            await f.Bookmarks.DeleteAsync("run", "wait");
            if (scenario == "replaced") await f.Bookmarks.SaveAsync(original);
        }
        var result = await f.Dispatcher.DispatchAsync(source, "run", "wait");
        Assert.Equal(BookmarkResumeDispatchStatus.NotFound, result.Status);
        var events = (await f.History.ReadPageAsync(new("run"))).Items;
        var receipt = Assert.Single(events, e => e.Name == "DeliveryReceived");
        var unmatched = Assert.Single(events, e => e.Name == "DeliveryUnmatched");
        Assert.Equal(source.SourceOutboxItemId, receipt.Causation!.DeliveryId);
        Assert.Equal(receipt.HistoryEventId, unmatched.Causation!.HistoryEventId);
        Assert.Equal("wait", unmatched.BookmarkId);
        Assert.Empty(f.Actors.Activations);
        Assert.DoesNotContain(events, e => e.Name is "BookmarkConsumed" or "BookmarkRetired" or "ActivityResumeClaimed");
    }

    [Fact]
    public async Task Stale_saved_source_cannot_append_a_receipt_or_enqueue_work()
    {
        using var f = await Fixture.CreateAsync();
        await f.CreateWaitAsync("wait");
        var source = await f.SourceAsync(installRecipient: true);
        await f.Commits.RecordDeliveryResultAsync(source.Claim,
            new(source.SourceOutboxItemId, RuntimePostCommitOutboxStatus.FailedRetryable, At, "retry"));
        await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(async () =>
            await f.Dispatcher.DispatchAsync(source, "run", "wait"));
        Assert.Empty(f.Actors.Activations);
        Assert.DoesNotContain((await f.History.ReadPageAsync(new("run"))).Items,
            e => e.Authority == RuntimeHistoryAuthority.DeliveryObservation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saved_source_double_failure_keeps_both_causes_and_requires_both_to_be_unavailable(bool hardPrimary)
    {
        using var f = await Fixture.CreateAsync();
        await f.CreateWaitAsync("wait");
        var source = await f.SourceAsync(installRecipient: true);
        Exception primary = hardPrimary
            ? new RuntimePostCommitSourceBoundaryException("source", RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
                new IOException("hard source boundary"), isUnavailable: false)
            : new IOException("transient dispatch failure");
        f.Actors.Actor.Failure = primary;
        var secondary = new IOException("diagnostic write failure");
        f.Probe.Writer!.Fail[2] = secondary;
        var boundary = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(async () =>
            await f.Dispatcher.DispatchAsync(source, "run", "wait"));
        Assert.Equal(!hardPrimary, boundary.IsUnavailable);
        var aggregate = Assert.IsType<AggregateException>(boundary.InnerException);
        Assert.Same(primary, aggregate.InnerExceptions[0]);
        Assert.Same(secondary, aggregate.InnerExceptions[1].InnerException);
        Assert.Single((await f.History.ReadPageAsync(new("run"))).Items, e => e.Name == "DeliveryReceived");
    }

    [Theory]
    [InlineData("uncaptured")]
    [InlineData("core-only")]
    public async Task Saved_source_without_delivery_capture_keeps_the_existing_null_omitted_payload_and_does_not_resolve_observers(string profile)
    {
        using var f = await Fixture.CreateAsync(profile: profile);
        await f.CreateWaitAsync("wait");
        var source = await f.SourceAsync(installRecipient: true);
        await f.Dispatcher.DispatchAsync(source, "run", "wait");
        Assert.Equal(0, f.Probe.FactoryCalls);
        var payload = Assert.Single(f.Actors.Actor.Envelopes).Command.Payload!.Value;
        Assert.False(payload.TryGetProperty("HistoryCausation", out _));
        Assert.NotNull(payload.Deserialize<RuntimeResumeBookmarkCommandPayload>()!.ExpectedBookmarkResume);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task A_retained_required_delivery_profile_refuses_a_removed_module_or_original_constructor_even_for_an_unmatched_wait(
        bool originalConstructor, bool removedWait)
    {
        using var f = await Fixture.CreateAsync();
        await f.CreateWaitAsync("wait");
        if (removedWait) await f.Bookmarks.DeleteAsync("run", "wait");
        using var host = f.HistoryFreeHost();
        using var scope = host.CreateScope();
        var dispatcher = originalConstructor ? f.OriginalConstructorDispatcher() : scope.ServiceProvider.GetRequiredService<IBookmarkResumeDispatcher>();
        var request = new BookmarkResumeDispatchRequest("run", "task", "hash")
            { HistoryCausation = new(WorkflowExecutionId: "run", HistoryEventId: "forged-receipt") };
        var failure = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(async () => await dispatcher.DispatchAsync(request));
        Assert.Equal("runtime.history.capture-unavailable", failure.Code);
        Assert.Empty(f.Actors.Activations);
        Assert.DoesNotContain((await f.History.ReadPageAsync(new("run"))).Items,
            e => e.Authority == RuntimeHistoryAuthority.DeliveryObservation);
    }

    [Theory]
    [InlineData(false, "uncaptured")]
    [InlineData(true, "uncaptured")]
    [InlineData(false, "core-only")]
    [InlineData(true, "core-only")]
    public async Task A_history_free_dispatcher_keeps_older_profiles_eligible_without_observer_resolution(bool originalConstructor, string profile)
    {
        using var f = await Fixture.CreateAsync(profile: profile);
        await f.CreateWaitAsync("wait");
        using var host = f.HistoryFreeHost();
        using var scope = host.CreateScope();
        var dispatcher = originalConstructor ? f.OriginalConstructorDispatcher() : scope.ServiceProvider.GetRequiredService<IBookmarkResumeDispatcher>();
        Assert.Equal(BookmarkResumeDispatchStatus.Dispatched, (await dispatcher.DispatchAsync(Request())).Status);
        Assert.Single(f.Actors.Actor.Envelopes);
        Assert.Equal(0, f.Probe.FactoryCalls);
        Assert.False(f.Actors.Actor.Envelopes[0].Command.Payload!.Value.TryGetProperty("HistoryCausation", out _));
    }

    private sealed class LegacyDispatcher : IBookmarkResumeDispatcher
    {
        public ValueTask<BookmarkResumeDispatchResult> DispatchAsync(BookmarkResumeDispatchRequest request,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Qualification must not dispatch.");
    }

    private sealed class SavedAdapter : IStimulusDeliveryIntentAdapter
    {
        public string IntentKind => "test.history-delivery";
        public StimulusDispatchRequest CreateRequest(RuntimePostCommitIntent intent) =>
            new("task", "hash", intent.Payload, mode: StimulusRoutingMode.ResumeOnly, idempotencyKey: intent.IdempotencyKey)
                { ResumeAllMatchingBookmarks = true };
    }

    private static BookmarkResumeDispatchRequest Request() => new("run", "task", "hash", JsonSerializer.SerializeToElement("private input"),
        "delivery-key", "private requester", new Dictionary<string, string> { ["private"] = "private metadata" });
    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; set; } = PersistenceAccessContext.Scoped(new("scope-a")); }
    private sealed class Probe { internal int FactoryCalls; internal ProbedWriter? Writer; }
    private sealed class ProbedWriter(IRuntimeHistoryObservationWriter inner) : IRuntimeHistoryObservationWriter
    {
        internal int Calls;
        internal Dictionary<int, Exception> Fail { get; } = new();
        internal bool FailAfterAppend;
        public bool CanRecordHistory(IRuntimeHistoryStore reader) => inner.CanRecordHistory(reader);
        public async ValueTask<RuntimeHistoryObservationResult> RecordAsync(RuntimeHistoryObservation observation, CancellationToken cancellationToken = default)
        {
            var call = ++Calls;
            if (!FailAfterAppend && Fail.TryGetValue(call, out var before)) throw before;
            var result = await inner.RecordAsync(observation, cancellationToken);
            if (FailAfterAppend && Fail.TryGetValue(call, out var after)) throw after;
            return result;
        }
    }
    private sealed class Actors : IWorkflowExecutionActorProvider
    {
        internal Actor Actor { get; } = new();
        internal List<WorkflowExecutionActorActivationRequest> Activations { get; } = [];
        public WorkflowExecutionActorCapabilities Capabilities => WorkflowExecutionActorCapabilities.InProcessMailbox;
        public ValueTask<IWorkflowExecutionActor> GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default)
        { Activations.Add(request); return ValueTask.FromResult<IWorkflowExecutionActor>(Actor); }
        public ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
    private sealed class Actor : IWorkflowExecutionActor
    {
        internal List<WorkflowExecutionCommandEnvelope> Envelopes { get; } = [];
        internal WorkflowExecutionCommandDispatchStatus Status = WorkflowExecutionCommandDispatchStatus.Accepted;
        internal Exception? Failure;
        internal Func<ValueTask>? BeforeEnqueue;
        public WorkflowExecutionActorDescriptor Descriptor { get; } = new("run", "actor", "controlled-handoff", WorkflowExecutionActorStatus.Active,
            WorkflowExecutionActorCapabilities.InProcessMailbox, At);
        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default) => Record(envelope);
        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, WorkflowExecutionCommandDispatchOptions options, CancellationToken cancellationToken = default) => Record(envelope);
        private async ValueTask<WorkflowExecutionCommandDispatchResult> Record(WorkflowExecutionCommandEnvelope envelope)
        {
            Envelopes.Add(envelope);
            if (BeforeEnqueue is { } inspect) await inspect();
            if (Failure is { } error) throw error;
            return new(envelope.EnvelopeId, "run", Status, At, reason: Status == WorkflowExecutionCommandDispatchStatus.Accepted ? null : "private handoff reason");
        }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;
        private Fixture(ServiceProvider provider, IServiceScope scope, Access access, FakeTimeProvider clock, Actors actors, Probe probe)
        {
            _provider = provider; _scope = scope; Access = access; Clock = clock; Actors = actors; Probe = probe;
            var selected = scope.ServiceProvider;
            Workflows = RuntimeCoreServiceCollectionExtensions.ResolveUnderlyingStore<IWorkflowExecutionStateStore>(selected, typeof(CoalescingWorkflowExecutionStateStore));
            Activities = RuntimeCoreServiceCollectionExtensions.ResolveUnderlyingStore<IActivityExecutionStateStore>(selected, typeof(CoalescingActivityExecutionStateStore));
            Bookmarks = selected.GetRequiredService<IBookmarkStateStore>();
            History = selected.GetRequiredService<IRuntimeHistoryStore>();
            Commits = selected.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
            Dispatcher = selected.GetRequiredService<IBookmarkResumeDispatcher>();
        }
        internal Access Access { get; }
        internal FakeTimeProvider Clock { get; }
        internal Actors Actors { get; }
        internal Probe Probe { get; }
        internal IWorkflowExecutionStateStore Workflows { get; }
        internal IActivityExecutionStateStore Activities { get; }
        internal IBookmarkStateStore Bookmarks { get; }
        internal IRuntimeHistoryStore History { get; }
        internal InMemoryRuntimeCheckpointCommitStore Commits { get; }
        internal IBookmarkResumeDispatcher Dispatcher { get; }
        internal IStimulusRouter Router => _scope.ServiceProvider.GetRequiredService<IStimulusRouter>();

        internal IBookmarkResumeDispatcher OriginalConstructorDispatcher() => new BookmarkResumeDispatcher(
            _scope.ServiceProvider.GetRequiredService<IBookmarkStimulusLookup>(), Workflows,
            _scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>(), _scope.ServiceProvider.GetRequiredService<IBookmarkResumeResolver>(),
            Actors, _scope.ServiceProvider.GetRequiredService<IRuntimeExecutionIdGenerator>(), Clock);

        internal ServiceProvider HistoryFreeHost()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IPersistenceAccessContextAccessor>(Access);
            services.AddSingleton<IWorkflowExecutionActorProvider>(Actors);
            services.AddSingleton<IWorkflowExecutionStateStore>(Workflows);
            services.AddSingleton<IActivityExecutionStateStore>(Activities);
            services.AddSingleton<IBookmarkStateStore>(Bookmarks);
            services.AddSingleton<IBookmarkStimulusLookup>(new BookmarkStimulusLookup(Bookmarks));
            services.AddSingleton<IWorkflowExecutableStore>(_scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>());
            services.AddSingleton<IRuntimeCheckpointCommitStore>(Commits);
            services.AddSingleton(_scope.ServiceProvider.GetRequiredService<InMemoryRuntimeCheckpointStoreState>());
            services.AddWorkflowRuntime();
            return services.BuildServiceProvider(validateScopes: true); // No history options, observer, reader or capture registration.
        }

        internal static async Task<Fixture> CreateAsync(bool coalescing = false, string profile = "captured")
        {
            var access = new Access(); var clock = new FakeTimeProvider(At); var actors = new Actors(); var probe = new Probe();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IPersistenceAccessContextAccessor>(access);
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<IWorkflowExecutionActorProvider>(actors);
            services.AddSingleton<IStimulusDeliveryIntentAdapter, SavedAdapter>();
            services.AddWorkflowRuntime();
            services.AddWorkflowStimulusRouter();
            RuntimeHistoryRegistration.AddServices(services, new() { Enabled = profile is "captured" or "receipt-only" });
            services.Configure<RuntimeRecoveryContinuationOptions>(options => options.SigningKey = "bookmark-delivery-history-tests-key-at-least-32-bytes");
            if (coalescing) services.AddPersistenceModes();
            services.AddScoped<IRuntimeHistoryObservationWriter>(selected =>
            {
                probe.FactoryCalls++;
                return probe.Writer = new(new InMemoryRuntimeHistoryObservationWriter(selected.GetRequiredService<InMemoryRuntimeCheckpointStoreState>(), access,
                    RuntimeCoreServiceCollectionExtensions.ResolveUnderlyingStore<IWorkflowExecutionStateStore>(selected, typeof(CoalescingWorkflowExecutionStateStore)), clock));
            });
            var provider = services.BuildServiceProvider(validateScopes: true); var scope = provider.CreateScope();
            var f = new Fixture(provider, scope, access, clock, actors, probe);
            var policy = profile == "uncaptured" ? null : profile == "core-only"
                ? new RuntimeHistoryCapturePolicy("runtime-history-v3", 3, true, RuntimeHistoryCapture.Kinds.Concat(RuntimeHistoryCapture.Version2Kinds).ToArray())
                : RuntimeHistoryRegistration.ResolvePolicy(scope.ServiceProvider, "run");
            if (profile == "receipt-only")
            {
                var selectedPolicy = policy ?? throw new InvalidOperationException("The receipt profile was not admitted.");
                policy = new(selectedPolicy.PolicyId, selectedPolicy.Version, selectedPolicy.Required,
                    selectedPolicy.SupportedKinds.Where(kind => !RuntimeHistoryDeliveryObservations.Kinds.Contains(kind) || kind == "DeliveryReceived").ToArray());
            }
            Assert.Empty(actors.Activations); // Qualification must not activate a workflow actor.
            await f.Workflows.SaveAsync(new("run", Pin, WorkflowExecutionStatus.Running, null, At, At, At, null, null, null, "scope-a",
                new Dictionary<string, string>()) { HistoryCapturePolicy = policy, HistoryCaptureEnabledAtAdmission = policy is not null,
                Partition = new("partition"), RunKind = WorkflowRunKind.PublishedRun });
            await f.Activities.SaveAsync(new(new("activity", "run", "node", "authored", "test/activity", "1"), ActivityExecutionStatus.Suspended,
                null, 1, At, At, null, null, null, null, null, ActivitySchedulingProvenance.From("run", null, null, null, null, null, "activity", "test"),
                0, [], [], 0, 0, new Dictionary<string, string>(), ExecutionScopeId: "activity"));
            await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(new(Pin,
                new("node", "authored", "test/activity", "1", "test", JsonSerializer.SerializeToElement(new { type = "test" }),
                    new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
                new Dictionary<string, WorkflowExecutableResumeTarget> { ["resume"] = new("resume", "node", "handler", new Dictionary<string, string>()) },
                At, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference));
            return f;
        }

        internal async Task<PostCommitDeliveryContext> SourceAsync(bool installRecipient = false, RuntimeHistoryCausation? origin = null)
        {
            var publisher = new WorkflowExecutionState("publisher", Pin, WorkflowExecutionStatus.Running, null, At, At, At,
                null, null, null, "scope-a", new Dictionary<string, string>())
                { Partition = new("partition"), RunKind = WorkflowRunKind.PublishedRun };
            await Workflows.SaveAsync(publisher);
            var intent = new RuntimePostCommitIntent("send", "publisher", "test.history-delivery", At, "publisher-activity", "source-key",
                JsonSerializer.SerializeToElement(new { value = "private canonical input" })) { HistoryCausation = origin };
            var persistenceScope = new PersistenceScope("scope-a");
            await Commits.AddPendingForTestingAsync(new("source:send", intent, RuntimePostCommitOutboxStatus.Pending, At, At,
                new(4, TimeSpan.FromSeconds(1)), 0, null, null, null, null, null, 0, null, 1, persistenceScope, null));
            var source = new PostCommitDeliveryContext(Assert.Single(await Commits.ClaimAsync(new("owner", Clock.GetUtcNow(), TimeSpan.FromMinutes(10), 1))), persistenceScope);
            if (installRecipient)
            {
                var observation = (await Bookmarks.ObserveAsync("run", "wait"))!;
                var capture = StimulusResumeRecipient.From(observation, (await Workflows.FindAsync("run"))!, persistenceScope);
                await Commits.InstallAsync(source, new(1, source.SourceOutboxItemId, source.IntentFingerprint,
                    StimulusDeliveryScope.From(publisher, persistenceScope), Clock.GetUtcNow(), "task", "hash", [], [capture]));
            }
            return source;
        }
        internal async Task CreateWaitAsync(string id, DateTimeOffset? expiresAt = null)
        {
            var payload = new RuntimeCreateBookmarkCommandPayload(Pin, id, "activity", "node", "resume", "task", "hash",
                JsonSerializer.SerializeToElement("private bookmark"), expiresAt, RuntimeCreateBookmarkCommandPayload.ActivitySuspendedReason);
            var work = new RuntimeSchedulerWorkItem("create-" + id, "run", "command-" + id, WorkflowExecutionCommandKind.CreateBookmark,
                "envelope-" + id, "key-" + id, Clock.GetUtcNow(), Clock.GetUtcNow(), payload: JsonSerializer.SerializeToElement(payload));
            var selected = _scope.ServiceProvider;
            await new WorkflowCreateBookmarkSchedulerWorkHandler(selected.GetRequiredService<IWorkflowExecutableStore>(), Activities,
                selected.GetRequiredService<RuntimeCheckpointCommitter>(), selected.GetRequiredService<IRuntimeActivityExecutionInspectionAccumulator>(), Clock).HandleAsync(work);
        }
        public void Dispose() { _scope.Dispose(); _provider.Dispose(); }
    }
}
