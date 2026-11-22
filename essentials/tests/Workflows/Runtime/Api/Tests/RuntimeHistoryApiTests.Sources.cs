using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Inspection.Core;
using Elsa.Primitives.Models;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

// Controlled source responses qualify the API boundary, not physical provider/capture behavior.
public sealed partial class RuntimeHistoryApiTests
{
    private static readonly RuntimeHistoryCapturePolicy SourcePolicy = new("runtime-history-v3", 3, true,
        ["StimulusPublicationCommitted", "StimulusRoutingAttemptObserved", "StimulusRoutingDispositionObserved"]);

    [Theory]
    [InlineData(RuntimePostCommitOutboxStatus.Pending, true)]
    [InlineData(RuntimePostCommitOutboxStatus.Delivering, true)]
    [InlineData(RuntimePostCommitOutboxStatus.FailedRetryable, true)]
    [InlineData(RuntimePostCommitOutboxStatus.FailedFinal, true)]
    [InlineData(RuntimePostCommitOutboxStatus.Cancelled, true)]
    [InlineData(RuntimePostCommitOutboxStatus.Delivered, false)]
    public async Task Source_settlement_uses_only_exact_physical_responsibility_not_due_or_claim_state(RuntimePostCommitOutboxStatus status, bool expected)
    {
        var projection = new SourceProjection(); var lookup = new SourceLookup(projection) { Item = SourceItem(status) };
        using var provider = await SourceServices(projection, lookup);
        var view = await provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"));
        Assert.Equal(expected, view.Coverage.SourceHistoryPending);
        Assert.Equal(expected ? "pending" : "captured", view.Coverage.State);
        Assert.Null(view.Coverage.SourceHistoryLimitation);
        Assert.Equal(new[] { "outgoing" }, lookup.Reads);
        Assert.Equal(2, projection.Queries.Count); Assert.Equal(100, projection.Queries[1].Limit);
        Assert.Equal("StimulusPublicationCommitted", projection.Queries[1].Name);
        Assert.Null(projection.Queries[1].ActivityExecutionId); Assert.Null(projection.Queries[1].ContinuationToken);
        Assert.Equal(10, view.UpperSequence); Assert.Equal(10, view.Coverage.LastSequence);
        Assert.Equal(expected ? 0 : 1, projection.HeadReads);
        Assert.Equal(0, provider.GetRequiredService<Authorization>().ValueReads);
    }

    [Theory]
    [InlineData("append")] [InlineData("purge")] [InlineData("gap")]
    [InlineData("late")] [InlineData("first")] [InlineData("physical")]
    [InlineData("instant")] [InlineData("policy")] [InlineData("missing")]
    public async Task Negative_source_observation_requires_the_entire_original_boundary_and_never_advances_the_page(string changed)
    {
        var projection = new SourceProjection();
        projection.Refreshed = changed switch
        {
            "append" => projection.Head with { LastSequence = 11 },
            "purge" => projection.Head with { PurgedThroughSequence = 1 },
            "gap" => projection.Head with { HasGaps = true },
            "late" => projection.Head with { CapturedFromStart = false },
            "first" => projection.Head with { FirstSequence = 2 },
            "physical" => projection.Head with { LastPhysicalCommitId = "replacement" },
            "instant" => projection.Head with { LastCommittedAt = Now.AddSeconds(1) },
            "policy" => projection.Head with { Policy = Policy },
            _ => null
        };
        using var provider = await SourceServices(projection, new SourceLookup(projection));
        var view = await provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"));
        Assert.Null(view.Coverage.SourceHistoryPending); Assert.Equal("partial", view.Coverage.State);
        Assert.Contains("changed", view.Coverage.SourceHistoryLimitation);
        Assert.Equal(10, view.UpperSequence); Assert.Equal(10, view.Coverage.LastSequence);
        Assert.Equal("physical", view.Coverage.LastPhysicalCommitId);
    }

    [Theory]
    [InlineData("next")] [InlineData("scan")] [InlineData("scope")]
    [InlineData("head")] [InlineData("overflow")] [InlineData("schema")]
    public async Task Incomplete_or_newer_source_windows_never_claim_zero_pending_or_activate_the_lookup(string boundary)
    {
        var projection = new SourceProjection();
        projection.Transform = page => boundary switch
        {
            "next" => page with { NextContinuationToken = "more" },
            "scan" => page with { ScannedThroughSequence = 5 },
            "scope" => page with { ScopeComplete = false },
            "head" => page with { Head = page.Head! with { HasGaps = true } },
            "overflow" => page with { Items = Enumerable.Repeat(Origin(), 101).ToArray(), ScannedCount = 101 },
            _ => page with { Items = [Origin(schema: 99)] }
        };
        var resolutions = 0;
        using var provider = await SourceServices(projection, resolve: () => { resolutions++; throw new IOException("unused owner"); });
        var view = await provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"));
        Assert.Null(view.Coverage.SourceHistoryPending); Assert.NotNull(view.Coverage.SourceHistoryLimitation);
        Assert.Equal(0, resolutions); Assert.Equal(0, projection.HeadReads);
    }

    [Fact]
    public async Task Complete_source_free_prefix_does_not_activate_an_unused_optional_source_provider()
    {
        var projection = new SourceProjection { Items = [] }; var resolutions = 0;
        using var provider = await SourceServices(projection, resolve: () => { resolutions++; throw new IOException("unused owner"); });
        var view = await provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"));
        Assert.False(view.Coverage.SourceHistoryPending); Assert.Null(view.Coverage.SourceHistoryLimitation);
        Assert.Equal(0, resolutions); Assert.Equal(1, projection.HeadReads);
    }

    [Theory]
    [InlineData("owner")] [InlineData("pair")] [InlineData("row")]
    [InlineData("outage")] [InlineData("unpair")]
    public async Task Missing_foreign_or_unavailable_sources_keep_retained_evidence_readable_and_unknown(string boundary)
    {
        var projection = new SourceProjection(); var lookup = new SourceLookup(projection);
        if (boundary == "pair") lookup.Paired = false;
        if (boundary == "row") lookup.Item = null;
        if (boundary == "outage") lookup.Failure = new IOException("secret-provider-detail");
        if (boundary == "unpair") lookup.AfterRead = () => lookup.Paired = false;
        using var provider = await SourceServices(projection, boundary == "owner" ? null : lookup);
        var view = await provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"));
        Assert.Null(view.Coverage.SourceHistoryPending); Assert.Equal("partial", view.Coverage.State);
        Assert.NotNull(view.Coverage.SourceHistoryLimitation); Assert.DoesNotContain("secret", view.Coverage.SourceHistoryLimitation);
        Assert.Equal(10, view.Coverage.LastSequence);
    }

    [Theory]
    [InlineData("id")] [InlineData("target")] [InlineData("kind")]
    [InlineData("origin")] [InlineData("run")] [InlineData("delivery")]
    public async Task A_durable_source_must_match_all_recorded_origin_and_target_identities(string field)
    {
        var projection = new SourceProjection(); var cause = new RuntimeHistoryCausation("origin", "run", "outgoing");
        cause = field switch { "origin" => cause with { HistoryEventId = "foreign" }, "run" => cause with { WorkflowExecutionId = "foreign" },
            "delivery" => cause with { DeliveryId = "foreign" }, _ => cause };
        var lookup = new SourceLookup(projection) { Item = SourceItem(RuntimePostCommitOutboxStatus.Delivered,
            field == "id" ? "foreign" : "outgoing", field == "target" ? "foreign" : "run",
            field == "kind" ? "foreign.kind" : "Elsa.Activities.Primitives.PublishStimulus", cause) };
        using var provider = await SourceServices(projection, lookup);
        var view = await provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"));
        Assert.Equal("invalid", view.Coverage.State); Assert.Null(view.Coverage.SourceHistoryPending);
    }

    [Fact]
    public async Task An_incoming_causal_delivery_is_never_used_as_the_outgoing_source_identity()
    {
        var projection = new SourceProjection { Items = [Origin(sourceId: null)] }; var lookup = new SourceLookup(projection);
        using var provider = await SourceServices(projection, lookup);
        var view = await provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"));
        Assert.Null(view.Coverage.SourceHistoryPending); Assert.Empty(lookup.Reads);
        Assert.Contains("identity", view.Coverage.SourceHistoryLimitation);
    }

    [Theory]
    [InlineData("purged")] [InlineData("late")] [InlineData("gap")] [InlineData("vocabulary")]
    public async Task Incomplete_original_recording_cannot_prove_source_absence(string boundary)
    {
        var projection = new SourceProjection { Items = [] };
        projection.Head = boundary switch
        {
            "purged" => projection.Head with { PurgedThroughSequence = 1, FirstSequence = 2 },
            "late" => projection.Head with { CapturedFromStart = false },
            "gap" => projection.Head with { HasGaps = true },
            _ => projection.Head with { Policy = new("runtime-history-v3", 3, true, ["StimulusPublicationCommitted"]) }
        };
        projection.Refreshed = projection.Head;
        using var provider = await SourceServices(projection);
        var view = await provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"));
        Assert.Null(view.Coverage.SourceHistoryPending); Assert.NotNull(view.Coverage.SourceHistoryLimitation);
        Assert.Single(projection.Queries); Assert.Equal(0, projection.HeadReads);
    }

    [Fact]
    public async Task Denied_reads_do_not_resolve_the_optional_owner_or_read_any_history()
    {
        var projection = new SourceProjection(); var resolutions = 0; var auth = new Authorization { Allowed = false };
        using var provider = await SourceServices(projection, auth: auth, resolve: () => { resolutions++; throw new IOException(); });
        await Assert.ThrowsAnyAsync<Exception>(() => provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run")));
        Assert.Empty(projection.Queries); Assert.Equal(0, resolutions); Assert.Equal(0, auth.ValueReads);
    }

    [Fact]
    public async Task A_standalone_source_at_the_referenced_ID_is_inconsistent_workflow_origin_evidence()
    {
        var source = StandaloneSettlementSource();
        var projection = new SourceProjection { Items = [Origin(source.OutboxItemId)] };
        using var provider = await SourceServices(projection, new SourceLookup(projection) { Item = source });
        var view = await provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"));
        Assert.Equal("invalid", view.Coverage.State); Assert.Null(view.Coverage.SourceHistoryPending);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Malformed_source_scope_evidence_is_integrity_failure_including_a_contextual_wrapper(bool wrapped)
    {
        var projection = new SourceProjection(); var corruption = new InvalidDataException("private-scope-witness");
        var lookup = new SourceLookup(projection) { Failure = wrapped ? new InvalidOperationException("context", corruption) : corruption };
        using var provider = await SourceServices(projection, lookup);
        var view = await provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"));
        Assert.Equal("invalid", view.Coverage.State); Assert.Null(view.Coverage.SourceHistoryPending);
        Assert.DoesNotContain("private", view.Coverage.Limitation);
    }

    [Theory]
    [InlineData(0)] [InlineData(2)]
    public async Task A_consistently_unexplained_missing_prefix_cannot_establish_source_absence(long first)
    {
        var projection = new SourceProjection { Items = [] };
        projection.Head = projection.Head with { FirstSequence = first }; projection.Refreshed = projection.Head;
        using var provider = await SourceServices(projection);
        var view = await provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"));
        Assert.Null(view.Coverage.SourceHistoryPending); Assert.NotNull(view.Coverage.SourceHistoryLimitation);
        Assert.Equal(first, view.Coverage.FirstSequence); Assert.Equal(10, view.UpperSequence);
        Assert.Single(projection.Queries); Assert.Equal(0, projection.HeadReads);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public async Task A_genuine_empty_boundary_can_observe_no_source_responsibility(long first)
    {
        var projection = new SourceProjection { Items = [] };
        projection.Head = projection.Head with { FirstSequence = first, LastSequence = 0 }; projection.Refreshed = projection.Head;
        using var provider = await SourceServices(projection);
        var view = await provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"));
        Assert.False(view.Coverage.SourceHistoryPending); Assert.Equal(0, view.UpperSequence);
    }

    [Fact]
    public async Task A_source_observation_cannot_establish_absence_after_the_authorization_binding_changes()
    {
        var projection = new SourceProjection(); var auth = new Authorization();
        var lookup = new SourceLookup(projection) { AfterRead = () => auth.Subject = "replacement-operator" };
        using var provider = await SourceServices(projection, lookup, auth);
        var view = await provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"));
        Assert.Null(view.Coverage.SourceHistoryPending); Assert.Contains("context changed", view.Coverage.SourceHistoryLimitation);
        Assert.Equal(0, projection.HeadReads);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Known_outbox_responsibility_does_not_turn_unknown_timer_responsibility_into_zero(bool pending)
    {
        var projection = new SourceProjection();
        projection.Head = projection.Head with { Policy = new("runtime-history-v3", 3, true, SourcePolicy.SupportedKinds.Append("TimerScheduled").ToArray()) };
        projection.Refreshed = projection.Head;
        using var provider = await SourceServices(projection, new SourceLookup(projection)
            { Item = SourceItem(pending ? RuntimePostCommitOutboxStatus.Pending : RuntimePostCommitOutboxStatus.Delivered) });
        var view = await provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"));
        if (pending) Assert.True(view.Coverage.SourceHistoryPending); else Assert.Null(view.Coverage.SourceHistoryPending);
        Assert.Contains("Timer", view.Coverage.SourceHistoryLimitation);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task Unrequested_source_cancellation_preserves_retained_rows_and_unknown_settlement(bool taskCancellation, bool wrapped)
    {
        var projection = new SourceProjection { RecordedItems = [Origin()] };
        Exception failure = taskCancellation ? new TaskCanceledException("source timeout") : new OperationCanceledException("source timeout");
        var lookup = new SourceLookup(projection) { Failure = wrapped ? new InvalidOperationException("source read", failure) : failure };
        using var provider = await SourceServices(projection, lookup);
        var view = await provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"), CancellationToken.None);
        Assert.Equal("origin", Assert.Single(view.Items).HistoryEventId);
        Assert.Equal(10, view.UpperSequence); Assert.Equal(10, view.Coverage.LastSequence);
        Assert.Equal("catch-up", view.CatchUpToken); Assert.Equal("physical", view.Coverage.LastPhysicalCommitId);
        Assert.Equal("partial", view.Coverage.State); Assert.Null(view.Coverage.SourceHistoryPending);
        Assert.Contains("could not be checked", view.Coverage.SourceHistoryLimitation);
        Assert.Single(lookup.Reads); Assert.Equal(0, projection.HeadReads);
        Assert.Equal(0, provider.GetRequiredService<Authorization>().ValueReads);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Request_cancellation_takes_precedence_over_a_concurrent_source_failure(int kind)
    {
        var projection = new SourceProjection(); using var cancellation = new CancellationTokenSource();
        Exception failure = kind switch
        {
            0 => new IOException("source read failed"),
            1 => new OperationCanceledException("source timeout"),
            2 => new TaskCanceledException("source timeout"),
            _ => new InvalidOperationException("source read", new TaskCanceledException("source timeout"))
        };
        var lookup = new SourceLookup(projection) { Failure = failure, BeforeFailure = cancellation.Cancel };
        using var provider = await SourceServices(projection, lookup);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"), cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Fact]
    public async Task Requested_cancellation_propagates_from_the_physical_source_read()
    {
        var projection = new SourceProjection(); using var cancellation = new CancellationTokenSource();
        var lookup = new SourceLookup(projection) { AfterRead = cancellation.Cancel };
        using var provider = await SourceServices(projection, lookup);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetRequiredService<RuntimeHistoryInspectionService>().ReadAsync(new("run"), cancellation.Token));
    }

    private static async Task<ServiceProvider> SourceServices(SourceProjection projection, SourceLookup? lookup = null,
        Authorization? auth = null, Func<IPostCommitOutboxLookupStore>? resolve = null, Func<IDurableTimerHistoryStore>? resolveTimer = null)
    {
        var services = new ServiceCollection(); var workflows = new InMemoryWorkflowExecutionStateStore();
        await workflows.SaveAsync(Workflow() with { HistoryCapturePolicy = projection.Head.Policy });
        services.AddSingleton<IWorkflowExecutionStateStore>(workflows);
        services.AddSingleton<IRuntimeHistoryStore>(projection);
        services.AddSingleton(auth ?? new Authorization());
        services.AddSingleton<IActivityInspectionContextAsync>(sp => sp.GetRequiredService<Authorization>());
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        if (resolve is not null) services.AddSingleton<IPostCommitOutboxLookupStore>(_ => resolve());
        else if (lookup is not null) services.AddSingleton<IPostCommitOutboxLookupStore>(lookup);
        if (resolveTimer is not null) services.AddSingleton<IDurableTimerStore>(_ => resolveTimer());
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    private static RuntimeHistoryEvent Origin(string? sourceId = "outgoing", int schema = 1) => new("origin", "run", 1,
        RuntimeHistoryEventCategory.SchedulerLifecycle, "StimulusPublicationCommitted", Now,
        metadata: sourceId is null ? null : new Dictionary<string, string> { ["history.sourceDeliveryId"] = sourceId })
        { Authority = RuntimeHistoryAuthority.CommittedTransition, SchemaVersion = schema,
          Causation = new("incoming-event", "upstream-run", "incoming-delivery") };

    private static RuntimePostCommitOutboxItem SourceItem(RuntimePostCommitOutboxStatus status = RuntimePostCommitOutboxStatus.Delivered,
        string id = "outgoing", string target = "run", string kind = "Elsa.Activities.Primitives.PublishStimulus", RuntimeHistoryCausation? cause = null) =>
        new(id, new RuntimePostCommitIntent("send", target, kind, Now, null, null, null)
            { HistoryCausation = cause ?? new("origin", "run", "outgoing") }, status, Now, Now.AddDays(1),
            deliveringOwnerId: status == RuntimePostCommitOutboxStatus.Delivering ? "owner" : null,
            deliveryStartedAt: status == RuntimePostCommitOutboxStatus.Delivering ? Now : null,
            deliveredAt: status == RuntimePostCommitOutboxStatus.Delivered ? Now : null);

    private static RuntimePostCommitOutboxItem StandaloneSettlementSource()
    {
        var scope = new PersistenceScope("tenant-a"); var partition = new WorkflowExecutionPartition("tenant-a");
        var schema = new ValueTypeDescriptor("Test.Payload", schemaVersion: 1);
        var payload = JsonSerializer.SerializeToElement(new { value = 42 });
        var actor = new WorkflowExecutionAuthoritySnapshot("standalone-trigger", "source-actor");
        var fingerprint = RuntimeStandaloneIngressSource.ComputeRequestFingerprint(scope, "tenant-a", "trigger", "Test.Source", "request", schema, payload, actor);
        var source = new RuntimeStandaloneIngressSource(1, "admission", "dispatch", Now, scope, "tenant-a", partition,
            "trigger", 1, 1, "Test.Source", "request", fingerprint, schema, payload, RuntimeIngressAdmissionPolicy.RequireAll, actor);
        var identity = new RuntimeSubscriptionStartIdentity(1, scope, source.OutboxItemId, "subscription");
        var recipient = new RuntimeSubscriptionRecipient(identity.Digest, "subscription", 1, 1, "definition", "native", "slot", 1, "activation",
            new("artifact", "definition", "version", "1", "hash"),
            new("reference", "Published", "publication", "1", "definition", "version", "1", "activation", "slot"),
            new string('a', 64), 1, 1, new Dictionary<string, JsonElement>(), actor, "tenant-a", partition, identity);
        var delivery = new RuntimeSubscriptionDelivery(1, source.OutboxItemId, source.Fingerprint, scope,
            [recipient], [], [new(recipient.RecipientId, RuntimeRecipientPhase.Pending)]);
        return RuntimePostCommitOutboxItem.CreateStandaloneIngress(source, delivery, RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)));
    }

    private sealed class SourceLookup(SourceProjection projection) : IPostCommitOutboxLookupStore
    {
        public bool Paired { get; set; } = true;
        public RuntimePostCommitOutboxItem? Item { get; set; } = SourceItem();
        public Exception? Failure { get; set; }
        public Action? AfterRead { get; set; }
        public Action? BeforeFailure { get; set; }
        public List<string> Reads { get; } = [];
        public bool CanInspectHistorySources(IRuntimeHistoryStore reader) => Paired && ReferenceEquals(reader, projection);
        public ValueTask<RuntimePostCommitOutboxItem?> FindAsync(string id, CancellationToken token = default) => throw new InvalidOperationException("Overlay reads forbidden.");
        public ValueTask<RuntimePostCommitOutboxItem?> FindDurableAsync(string id, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); Reads.Add(id); if (Failure is { } error) { BeforeFailure?.Invoke(); throw error; }
            AfterRead?.Invoke(); token.ThrowIfCancellationRequested(); return ValueTask.FromResult(Item);
        }
    }

    private sealed class SourceProjection : IRuntimeHistoryStore
    {
        public RuntimeHistoryHead Head { get; set; } = new("run", 1, 10, "physical", Now, SourcePolicy, true, false, 0);
        public RuntimeHistoryHead? Refreshed { get; set; } = new("run", 1, 10, "physical", Now, SourcePolicy, true, false, 0);
        public IReadOnlyList<RuntimeHistoryEvent> Items { get; set; } = [Origin()];
        public IReadOnlyList<RuntimeHistoryEvent> RecordedItems { get; set; } = [];
        public Func<RuntimeHistoryPage, RuntimeHistoryPage>? Transform { get; set; }
        public List<RuntimeHistoryQuery> Queries { get; } = [];
        public int HeadReads { get; private set; }
        public Action? BeforeHeadRead { get; set; }
        public ValueTask<RuntimeHistoryHead?> FindHeadAsync(string run, CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); HeadReads++; BeforeHeadRead?.Invoke(); return ValueTask.FromResult(Refreshed); }
        public ValueTask<RuntimeHistoryPage> ReadPageAsync(RuntimeHistoryQuery query, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); Queries.Add(query);
            var sourceRead = query.Name is not null;
            var page = new RuntimeHistoryPage(Head, sourceRead ? Items : RecordedItems, Head.LastSequence, Head.LastSequence,
                (sourceRead ? Items : RecordedItems).Count, null, "catch-up");
            return ValueTask.FromResult(sourceRead && Transform is not null ? Transform(page) : page);
        }
        public ValueTask<RuntimeHistoryEvent?> FindEventAsync(string run, string id, CancellationToken token = default) => throw new InvalidOperationException();
        public ValueTask<RuntimeHistoryBookmark?> FindBookmarkAsync(string run, string id, CancellationToken token = default) => throw new InvalidOperationException();
        public ValueTask<RuntimeHistoryBookmarkPage> ListBookmarksAsync(RuntimeHistoryBookmarkQuery query, CancellationToken token = default) => throw new InvalidOperationException();
    }
}
