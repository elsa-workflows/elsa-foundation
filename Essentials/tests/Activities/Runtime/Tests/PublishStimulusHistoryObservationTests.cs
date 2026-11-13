using System.Text.Json;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Primitives.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>Actual contributed handler with controlled native-source/policy/observation/router ports; no native delivery or provider proof.</summary>
public sealed class PublishStimulusHistoryObservationTests
{
    private const string Attempt = "StimulusRoutingAttemptObserved", Disposition = "StimulusRoutingDispositionObserved";
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch.AddDays(1);
    private static readonly PersistenceScope Scope = new("tenant");
    private static readonly RuntimeHistoryCapturePolicy Policy = new("runtime-history-v3", 3, true, ["StimulusPublicationCommitted", Attempt, Disposition]);

    [Fact]
    public async Task Actual_feature_handler_records_attempt_before_routing_and_links_its_observed_disposition()
    {
        using var f = new Fixture();
        f.Router.Before = () =>
        {
            Assert.Equal(Attempt, Assert.Single(f.Writer.Stored.Values).Event.Name);
            Assert.Equal(1, f.Sources.Reads); Assert.Equal(1, f.Owner.Reads);
        };
        await f.Deliver();
        var rows = f.Writer.Stored.Values.ToArray();
        Assert.Equal(new[] { Attempt, Disposition }, rows.Select(row => row.Event.Name));
        Assert.Equal(f.Source.Intent.HistoryCausation, rows[0].Event.Causation);
        Assert.Equal(rows[0].Event.HistoryEventId, rows[1].Event.Causation!.HistoryEventId);
        Assert.Equal("commit:send", rows[1].Event.Causation!.DeliveryId);
        Assert.All(rows, row =>
        {
            Assert.Equal("publisher", row.Event.WorkflowExecutionId); Assert.Equal("activity", row.Event.ActivityExecutionId);
            Assert.Equal(RuntimeHistoryAuthority.DeliveryObservation, row.Event.Authority);
            Assert.Null(row.Event.CheckpointId); Assert.Null(row.Event.BookmarkId); Assert.Null(row.Event.Payload);
            Assert.Equal("outbox-attempt:1:fence:1", row.Event.AttemptId);
            Assert.DoesNotContain("private", JsonSerializer.Serialize(row));
            RuntimeHistorySafety.ValidateObservation(row);
        });
        Assert.Equal("saved-cohort-routing-acknowledged-observed", rows[1].Event.Metadata["history.reasonCode"]);
        Assert.Equal(1, f.Router.Calls); Assert.Equal(0, f.Reader.Reads);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, f.Source.Claim.Item.Status); // Handler observations do not acknowledge the outbox.
    }

    [Fact]
    public async Task Repeated_handler_invocations_observe_distinct_receipts_without_minting_another_business_source()
    {
        using var f = new Fixture(); await f.Deliver(); await f.Deliver();
        Assert.Equal(4, f.Writer.Stored.Count); Assert.Equal(2, f.Router.Calls);
        var attempts = f.Writer.Stored.Values.Where(row => row.Event.Name == Attempt).ToArray();
        Assert.Equal(2, attempts.Length); Assert.NotEqual(attempts[0].Event.HistoryEventId, attempts[1].Event.HistoryEventId);
        Assert.All(attempts, row => Assert.Equal(f.Source.Intent.HistoryCausation, row.Event.Causation));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Ambiguous_attempt_or_disposition_write_retries_the_same_immutable_event_without_repeating_the_route(bool disposition)
    {
        using var f = new Fixture(); var kind = disposition ? Disposition : Attempt; var lost = false;
        f.Writer.Failure = row =>
        {
            if (row.Event.Name != kind || lost) return null;
            lost = true; f.Clock.Advance(TimeSpan.FromSeconds(1)); return new IOException("private lost acknowledgement");
        };
        f.Writer.CommitBeforeFailure = true;
        await f.Deliver();
        var retried = f.Writer.Attempts.Where(row => row.Event.Name == kind).ToArray();
        Assert.Equal(2, retried.Length); Assert.Same(retried[0], retried[1]);
        Assert.Equal(RuntimeHistoryIdentity.Compute(retried[0].Event), RuntimeHistoryIdentity.Compute(retried[1].Event));
        Assert.Equal(2, f.Writer.Stored.Count); Assert.Equal(1, f.Router.Calls);
    }

    [Theory]
    [InlineData("io", true)] [InlineData("hard", false)] [InlineData("head", true)]
    public async Task Unacknowledged_attempt_prevents_routing_and_preserves_failure_classification(string failure, bool unavailable)
    {
        using var f = new Fixture();
        Exception? original = failure == "io" ? new IOException("private IO") : failure == "hard" ? new InvalidDataException("private malformed", new IOException()) : null;
        f.Writer.Failure = _ => original; f.Writer.Recorded = failure != "head";
        var result = await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(f.Deliver);
        Assert.Equal(unavailable, result.IsUnavailable); Assert.Equal("commit:send", result.SourceOutboxItemId);
        if (original is not null) Assert.Same(original, result.InnerException);
        Assert.Equal(0, f.Router.Calls); Assert.Empty(f.Writer.Stored);
        Assert.Equal(failure == "io" ? 2 : 1, f.Writer.Attempts.Count);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Disposition_failure_after_routing_keeps_responsibility_owed_without_claiming_effect_rollback(bool ambiguous)
    {
        using var f = new Fixture(); var original = new IOException("private disposition IO");
        f.Writer.Failure = row => row.Event.Name == Disposition ? original : null; f.Writer.CommitBeforeFailure = ambiguous;
        var result = await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(f.Deliver);
        Assert.True(result.IsUnavailable); Assert.Same(original, result.InnerException);
        Assert.Equal(1, f.Router.Calls); Assert.Equal(ambiguous ? 2 : 1, f.Writer.Stored.Count);
        var attempts = f.Writer.Attempts.Where(row => row.Event.Name == Disposition).ToArray();
        Assert.Equal(2, attempts.Length); Assert.Same(attempts[0], attempts[1]);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, f.Source.Claim.Item.Status);
    }

    [Theory]
    [InlineData("boundary-unavailable", true, "routing-source-unavailable-observed")]
    [InlineData("boundary-hard", false, "routing-source-refused-observed")]
    [InlineData("generic", true, "routing-failure-observed")]
    [InlineData("validation", false, "routing-failure-observed")]
    [InlineData("shed", true, "routing-capacity-shed-observed")]
    public async Task Actual_routing_failure_or_capacity_shed_has_a_closed_observation_without_inventing_target_execution(string mode, bool transient, string reason)
    {
        using var f = new Fixture();
        Exception? original = mode switch
        {
            "boundary-unavailable" => new StimulusDeliveryBoundaryException("commit:send", new IOException("private"), true),
            "boundary-hard" => new StimulusDeliveryBoundaryException("commit:send", new InvalidDataException("private", new IOException()), false),
            "generic" => new IOException("private"), "validation" => new RuntimeCheckpointCommitValidationException("private"), _ => null
        };
        f.Router.Failure = original;
        if (mode == "shed") f.Router.Result = new([StimulusStartOutcome.Shed("binding", "artifact", TimeSpan.FromSeconds(1))], []);
        var result = await Record.ExceptionAsync(f.Deliver);
        if (mode.StartsWith("boundary", StringComparison.Ordinal))
        {
            var boundary = Assert.IsType<RuntimePostCommitSourceBoundaryException>(result);
            Assert.Equal(transient, boundary.IsUnavailable); Assert.Same(original, boundary.InnerException);
        }
        else
        {
            var delivery = Assert.IsType<RuntimePostCommitDeliveryException>(result);
            Assert.Equal(transient ? PostCommitFailureKind.Transient : PostCommitFailureKind.Permanent, delivery.Kind);
            if (original is not null) Assert.Same(original, delivery.InnerException);
        }
        Assert.Equal(1, f.Router.Calls); Assert.Equal(2, f.Writer.Stored.Count);
        var observed = Assert.Single(f.Writer.Stored.Values, row => row.Event.Name == Disposition);
        Assert.Equal(reason, observed.Event.Metadata["history.reasonCode"]);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(observed));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Primary_routing_and_secondary_observation_failures_preserve_both_causes_and_hard_dominance(bool hardPrimary, bool hardObservation)
    {
        using var f = new Fixture();
        Exception primary = hardPrimary ? new RuntimeCheckpointCommitValidationException("private primary") : new IOException("private primary");
        Exception secondary = hardObservation ? new InvalidDataException("private secondary", new IOException()) : new IOException("private secondary");
        f.Router.Failure = primary; f.Writer.Failure = row => row.Event.Name == Disposition ? secondary : null;
        var result = await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(f.Deliver);
        Assert.Equal(!hardPrimary && !hardObservation, result.IsUnavailable);
        var failures = Assert.IsType<AggregateException>(result.InnerException).InnerExceptions;
        Assert.Same(primary, Assert.IsType<RuntimePostCommitDeliveryException>(failures[0]).InnerException);
        Assert.Same(secondary, Assert.IsType<RuntimePostCommitSourceBoundaryException>(failures[1]).InnerException);
        Assert.Equal(1, f.Router.Calls);
    }

    [Theory]
    [InlineData("generic")] [InlineData("shed")] [InlineData("boundary")]
    public async Task Requested_cancellation_during_failure_observation_keeps_the_same_cancellation_and_original_primary_diagnostics(string mode)
    {
        using var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        var original = new OperationCanceledException("controlled disposition cancellation", null, cancellation.Token);
        const string key = "Elsa.Runtime.History.PublicationFailuresBeforeCancellation";
        original.Data[key] = "opaque prior diagnostics";
        Exception? primary = mode == "generic" ? new IOException("private route failure") : mode == "boundary"
            ? new StimulusDeliveryBoundaryException("commit:send", new IOException("private source failure"), true) : null;
        f.Router.Failure = primary;
        if (mode == "shed") f.Router.Result = new([StimulusStartOutcome.Shed("binding", "artifact", null)], []);
        f.Writer.Failure = row =>
        {
            if (row.Event.Name != Disposition) return null;
            cancellation.Cancel(); return original;
        };
        Assert.Same(original, await Assert.ThrowsAsync<OperationCanceledException>(() => f.Deliver(cancellation.Token)));
        var preserved = Assert.IsAssignableFrom<IReadOnlyList<object>>(original.Data[key]);
        Assert.Equal("opaque prior diagnostics", preserved[0]);
        if (mode == "boundary") Assert.Same(primary, Assert.IsType<RuntimePostCommitSourceBoundaryException>(preserved[1]).InnerException);
        else
        {
            var delivery = Assert.IsType<RuntimePostCommitDeliveryException>(preserved[1]);
            Assert.Equal(PostCommitFailureKind.Transient, delivery.Kind);
            if (primary is not null) Assert.Same(primary, delivery.InnerException);
        }
        Assert.Equal(1, f.Router.Calls); Assert.Single(f.Writer.Stored);
        Assert.Equal(1, f.Writer.Attempts.Count(row => row.Event.Name == Disposition));
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, f.Source.Claim.Item.Status);
    }

    [Theory]
    [InlineData("source")] [InlineData("owner")] [InlineData("write")]
    public async Task Scope_drift_across_source_owner_or_observation_await_refuses_further_effects(string phase)
    {
        using var f = new Fixture(); Action drift = () => f.Access.Current = PersistenceAccessContext.Scoped(new("foreign"));
        if (phase == "source") f.Sources.AfterRead = drift;
        if (phase == "owner") f.Owner.AfterRead = drift;
        if (phase == "write") f.Writer.AfterWrite = drift;
        var result = await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(f.Deliver);
        Assert.False(result.IsUnavailable); Assert.Equal(0, f.Router.Calls);
        Assert.Equal(phase == "write" ? 1 : 0, f.Writer.Stored.Count); // Acknowledgement failure does not erase an actual write.
    }

    [Theory]
    [InlineData("source-io", true)] [InlineData("source-hard", false)] [InlineData("owner-io", true)]
    [InlineData("owner-hard", false)] [InlineData("foreign-source", false)] [InlineData("old-policy", false)]
    public async Task Native_input_read_or_profile_failure_precedes_any_observation_or_route(string mode, bool unavailable)
    {
        using var f = new Fixture();
        if (mode == "source-io") f.Sources.Failure = new IOException("private");
        if (mode == "source-hard") f.Sources.Failure = new InvalidDataException("private", new IOException());
        if (mode == "owner-io") f.Owner.Failure = new IOException("private");
        if (mode == "owner-hard") f.Owner.Failure = new InvalidDataException("private", new IOException());
        if (mode == "foreign-source") f.Sources.Returned = Source(commit: "foreign");
        if (mode == "old-policy") f.Owner.State = f.Owner.State with { HistoryCapturePolicy = new("runtime-history-v3", 3, true, ["StimulusPublicationCommitted"]) };
        var result = await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(f.Deliver);
        Assert.Equal(unavailable, result.IsUnavailable); Assert.Equal(0, f.Router.Calls); Assert.Empty(f.Writer.Stored);
    }

    [Theory]
    [InlineData("writer")] [InlineData("reader")] [InlineData("source")] [InlineData("owner")] [InlineData("clock")] [InlineData("access")]
    public async Task Missing_selected_support_or_original_constructor_refuses_stamped_work(string missing)
    {
        using var f = new Fixture(missing);
        Assert.False(((IRuntimePostCommitDurableSourceIntentHandler)f.Executor).CanRecordHistory(f.Writer, f.Reader));
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(f.Deliver);
        Assert.Equal(0, f.Router.Calls); Assert.Empty(f.Writer.Stored);
    }

    [Theory]
    [InlineData("stale-source")] [InlineData("stale-route")] [InlineData("requested-cancel")] [InlineData("unrequested-cancel")]
    public async Task Stale_claim_and_requested_cancellation_propagate_without_inventing_a_routing_disposition(string mode)
    {
        using var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        Exception original = mode.StartsWith("stale", StringComparison.Ordinal)
            ? new RuntimePostCommitOutboxStaleClaimException("commit:send", "owner", 1, "peer", 2, RuntimePostCommitOutboxStatus.Delivering)
            : new OperationCanceledException("controlled native read cancellation", null, cancellation.Token);
        if (mode == "stale-route") f.Router.Failure = original;
        else f.Sources.Failure = original;
        if (mode == "requested-cancel") f.Sources.BeforeRead = cancellation.Cancel;
        var failure = await Record.ExceptionAsync(() => f.Deliver(cancellation.Token));
        if (mode == "unrequested-cancel")
        {
            var boundary = Assert.IsType<RuntimePostCommitSourceBoundaryException>(failure);
            Assert.True(boundary.IsUnavailable); Assert.Same(original, boundary.InnerException);
        }
        else Assert.Same(original, failure);
        Assert.Equal(mode == "stale-route" ? 1 : 0, f.Router.Calls);
        Assert.Equal(mode == "stale-route" ? 1 : 0, f.Writer.Stored.Count);
        Assert.DoesNotContain(f.Writer.Stored.Values, row => row.Event.Name == Disposition);
    }

    [Fact]
    public async Task Legacy_unstamped_work_and_original_constructor_keep_their_existing_route_without_resolving_history()
    {
        using var f = new Fixture(); var original = new PublishStimulusExecutor(f.Router);
        Assert.False(((IRuntimePostCommitDurableSourceIntentHandler)original).CanRecordHistory(f.Writer, f.Reader));
        await ((IRuntimePostCommitDurableSourceIntentHandler)original).HandleAsync(Source(origin: false));
        Assert.Equal(1, f.Router.Calls); Assert.Equal(0, f.Sources.Reads); Assert.Equal(0, f.Owner.Reads); Assert.Empty(f.Writer.Attempts);
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => ((IRuntimePostCommitDurableSourceIntentHandler)original).HandleAsync(f.Source).AsTask());
        Assert.Equal(1, f.Router.Calls);
    }

    [Fact]
    public void Actual_contributed_owner_qualifies_only_its_selected_pair_and_instance()
    {
        using var f = new Fixture(); var owner = (IRuntimePostCommitDurableSourceIntentHandler)f.Executor;
        Assert.True(owner.CanRecordHistory(f.Writer, f.Reader));
        Assert.False(owner.CanRecordHistory(new Writer(f.Reader), f.Reader));
        Assert.False(owner.CanRecordHistory(f.Writer, new Reader()));
        Assert.Equal(0, f.Sources.Reads); Assert.Equal(0, f.Owner.Reads); Assert.Empty(f.Writer.Attempts);
    }

    private static PostCommitDeliveryContext Source(bool origin = true, string commit = "commit")
    {
        var intent = new RuntimePostCommitIntent("send", "publisher", PublishStimulusConstants.PublishStimulusIntentKind, At, "activity", "key",
            JsonSerializer.SerializeToElement(new { value = "private payload" }), new Dictionary<string, string> { ["private"] = "metadata" })
            { HistoryCausation = origin ? new("publication-event", "publisher", commit + ":send", "command", "work") : null };
        var pending = new RuntimePostCommitOutboxItem(commit + ":send", intent, RuntimePostCommitOutboxStatus.Pending, At, At,
            null, 0, null, null, null, null, null, 0, null, 1, Scope, null);
        return new(RuntimePostCommitOutboxClaimTransitions.Claim(pending, new("owner", At, TimeSpan.FromMinutes(1), 1)), Scope);
    }
    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; set; } = PersistenceAccessContext.Scoped(Scope); }
    private sealed class Sources : IStimulusDeliveryStore
    {
        internal int Reads; internal Exception? Failure; internal Action? BeforeRead, AfterRead; internal PostCommitDeliveryContext? Returned;
        public ValueTask<PostCommitDeliveryContext> ReadSourceAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
        { Reads++; BeforeRead?.Invoke(); if (Failure is { } failure) throw failure; AfterRead?.Invoke(); return ValueTask.FromResult(Returned ?? source); }
        public ValueTask<StimulusDeliverySnapshot> InstallAsync(PostCommitDeliveryContext source, StimulusDeliverySnapshot candidate, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The observation owner must not select a recipient cohort.");
    }
    private sealed class Owner : IWorkflowExecutionStateStore
    {
        internal int Reads; internal Exception? Failure; internal Action? AfterRead;
        internal WorkflowExecutionState State = new("publisher", new("artifact", "definition", "version", "1", "hash"), WorkflowExecutionStatus.Completed,
            null, At, At, At, At, null, null, "tenant", new Dictionary<string, string> { ["private"] = "owner" })
            { HistoryCapturePolicy = Policy, HistoryCaptureEnabledAtAdmission = true };
        public ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        { Reads++; if (Failure is { } failure) throw failure; AfterRead?.Invoke(); return ValueTask.FromResult<WorkflowExecutionState?>(State.WorkflowExecutionId == workflowExecutionId ? State : null); }
        public ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowExecutionStatePage> QueryPageAsync(WorkflowExecutionStatePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> DeleteAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Router : IStimulusRouter
    {
        internal int Calls; internal Exception? Failure; internal Action? Before; internal StimulusRoutingResult Result = new([], []);
        public ValueTask<StimulusRoutingResult> RouteSourceAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
        { Calls++; Before?.Invoke(); if (Failure is { } failure) throw failure; return ValueTask.FromResult(Result); }
        public ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Captured publication must retain its physical source path.");
    }
    private sealed class Reader : IRuntimeHistoryStore
    {
        internal int Reads;
        private ValueTask<T> Read<T>() { Reads++; throw new InvalidOperationException("Diagnostic history cannot choose native execution authority."); }
        public ValueTask<RuntimeHistoryHead?> FindHeadAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => Read<RuntimeHistoryHead?>();
        public ValueTask<RuntimeHistoryPage> ReadPageAsync(RuntimeHistoryQuery query, CancellationToken cancellationToken = default) => Read<RuntimeHistoryPage>();
        public ValueTask<RuntimeHistoryEvent?> FindEventAsync(string workflowExecutionId, string historyEventId, CancellationToken cancellationToken = default) => Read<RuntimeHistoryEvent?>();
        public ValueTask<RuntimeHistoryBookmarkPage> ListBookmarksAsync(RuntimeHistoryBookmarkQuery query, CancellationToken cancellationToken = default) => Read<RuntimeHistoryBookmarkPage>();
        public ValueTask<RuntimeHistoryBookmark?> FindBookmarkAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default) => Read<RuntimeHistoryBookmark?>();
    }
    private sealed class Writer(Reader reader) : IRuntimeHistoryObservationWriter
    {
        internal List<RuntimeHistoryObservation> Attempts { get; } = [];
        internal Dictionary<string, RuntimeHistoryObservation> Stored { get; } = new(StringComparer.Ordinal);
        internal Func<RuntimeHistoryObservation, Exception?>? Failure; internal bool CommitBeforeFailure, Recorded = true; internal Action? AfterWrite;
        public bool CanRecordHistory(IRuntimeHistoryStore selectedStore) => ReferenceEquals(selectedStore, reader);
        public ValueTask<RuntimeHistoryObservationResult> RecordAsync(RuntimeHistoryObservation observation, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); RuntimeHistorySafety.ValidateObservation(observation); Attempts.Add(observation);
            var failure = Failure?.Invoke(observation);
            if (Recorded && (failure is null || CommitBeforeFailure)) Stored.TryAdd(observation.Event.HistoryEventId, observation);
            if (failure is not null) throw failure;
            AfterWrite?.Invoke(); return ValueTask.FromResult(new RuntimeHistoryObservationResult(Recorded, Recorded ? observation.Event : null));
        }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider provider; private readonly IServiceScope scope;
        internal Access Access { get; } = new(); internal Sources Sources { get; } = new(); internal Owner Owner { get; } = new();
        internal Router Router { get; } = new(); internal Reader Reader { get; } = new(); internal Writer Writer { get; }
        internal FakeTimeProvider Clock { get; } = new(At); internal PostCommitDeliveryContext Source { get; } = PublishStimulusHistoryObservationTests.Source();
        internal PublishStimulusExecutor Executor => scope.ServiceProvider.GetRequiredService<PublishStimulusExecutor>();
        internal Fixture(string? missing = null)
        {
            Writer = new(Reader); var services = new ServiceCollection();
            new ActivitiesPrimitivesFeature().ConfigureServices(services);
            services.AddSingleton<IStimulusRouter>(Router);
            if (missing != "access") services.AddSingleton<IPersistenceAccessContextAccessor>(Access);
            else services.RemoveAll<IPersistenceAccessContextAccessor>();
            if (missing != "clock") services.AddSingleton<TimeProvider>(Clock);
            if (missing != "source") services.AddSingleton<IStimulusDeliveryStore>(Sources);
            if (missing != "owner") services.AddSingleton<IWorkflowExecutionStateStore>(Owner);
            if (missing != "writer") services.AddSingleton<IRuntimeHistoryObservationWriter>(Writer);
            if (missing != "reader") services.AddSingleton<IRuntimeHistoryStore>(Reader);
            provider = services.BuildServiceProvider(validateScopes: true); scope = provider.CreateScope();
        }
        internal Task Deliver() => Deliver(default);
        internal Task Deliver(CancellationToken cancellationToken) => ((IRuntimePostCommitDurableSourceIntentHandler)Executor).HandleAsync(Source, cancellationToken).AsTask();
        public void Dispose() { scope.Dispose(); provider.Dispose(); }
    }
}
