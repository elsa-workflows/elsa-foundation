using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Capabilities;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed partial class RuntimeMinimalApiBehaviorTests
{
    private const string HistoryPath = "/runtime/workflows/instances/wf-1/history";

    [Fact]
    public async Task Capture_outage_is_a_safe_infrastructure_problem_instead_of_an_activity_fault_response()
    {
        await using var host = await StartAsync(_ => throw new RuntimeSchedulerWorkUnavailableException(
            "runtime.history.capture-unavailable", "private-sentinel"));
        using var response = await host.Client.PostAsJsonAsync(ExecutePath, new { });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-sentinel", text, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(text);
        Assert.Equal("runtime.history.unavailable", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task History_page_preserves_unknown_common_envelope_and_never_serializes_legacy_payload_or_arbitrary_metadata()
    {
        var source = new HistorySource();
        await using var host = await HistoryHostAsync(source);
        using var response = await host.Client.GetAsync(HistoryPath + "?limit=1&activityExecutionId=ae-1&category=ActivityLifecycle");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-sentinel", text, StringComparison.Ordinal);
        Assert.DoesNotContain("payload", text, StringComparison.OrdinalIgnoreCase);
        var page = JsonSerializer.Deserialize<RuntimeHistoryPageView>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var item = Assert.Single(page.Items);
        Assert.Equal(("future-event", "FutureActivityKind", 2, "CommittedTransition", "ActivityLifecycle", 1L, 3),
            (item.HistoryEventId, item.Name, item.SchemaVersion, item.Authority, item.Category, item.Sequence, item.CheckpointOrdinal));
        Assert.Equal("captured", page.Coverage.State);
        Assert.False(page.Coverage.CaptureAvailable);
        Assert.NotNull(page.Coverage.CaptureLimitation);
        Assert.Equal(1L, page.Coverage.SettledSequence);
        Assert.Equal((1, "ae-1", RuntimeHistoryEventCategory.ActivityLifecycle),
            (source.Query!.Limit, source.Query.ActivityExecutionId, source.Query.Category));
        Assert.False(string.IsNullOrWhiteSpace(source.Query.AccessBinding));
    }

    [Fact]
    public async Task Exact_event_and_page_preserve_explicit_outcome_omission_without_disclosing_other_metadata()
    {
        var metadata = new Dictionary<string, string>
        {
            ["history.outcomeCaptureState"] = "size-omitted", ["history.outcomeCount"] = "1",
            ["history.outcomeContentHash"] = RuntimeHistoryIdentity.CreateBinding([new string('a', 2045)]),
            ["private"] = "private-sentinel"
        };
        await using var host = await HistoryHostAsync(new HistorySource { EventMetadata = metadata });
        var page = (await host.Client.GetFromJsonAsync<RuntimeHistoryPageView>(HistoryPath))!;
        var exact = (await host.Client.GetFromJsonAsync<RuntimeHistoryEventDetailsView>(HistoryPath + "/events/future-event"))!;
        foreach (var item in new[] { Assert.Single(page.Items), exact.Event })
        {
            Assert.Equal("size-omitted", item.Metadata["history.outcomeCaptureState"]);
            Assert.Equal("1", item.Metadata["history.outcomeCount"]);
            Assert.Equal(metadata["history.outcomeContentHash"], item.Metadata["history.outcomeContentHash"]);
            Assert.False(item.Metadata.ContainsKey("history.outcomeNames"));
            Assert.False(item.Metadata.ContainsKey("private"));
            Assert.Equal(("physical", "logical", "ae-1"), (item.PhysicalCommitId, item.CheckpointId, item.ActivityExecutionId));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("/events/future-event")]
    [InlineData("/bookmarks")]
    [InlineData("/bookmarks/bookmark")]
    public async Task History_resource_denial_matches_missing_and_never_touches_the_history_provider(string suffix)
    {
        var source = new HistorySource();
        var authorization = new BrowserAuthorization { Allowed = false };
        await using var host = await HistoryHostAsync(source, authorization);
        using var denied = await host.Client.GetAsync(HistoryPath + suffix);
        using var missing = await host.Client.GetAsync(HistoryPath.Replace("wf-1", "absent", StringComparison.Ordinal) + suffix);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal(missing.StatusCode, denied.StatusCode);
        Assert.Equal(0, source.Calls);
        Assert.DoesNotContain("future-event", await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task History_access_binding_changes_with_subject_profile_and_tenant()
    {
        var source = new HistorySource();
        var authorization = new BrowserAuthorization();
        await using var host = await HistoryHostAsync(source, authorization);
        using var first = await host.Client.GetAsync(HistoryPath);
        var binding = source.Query!.AccessBinding;
        authorization.AuditSubject = "another-subject";
        using var changedSubject = await host.Client.GetAsync(HistoryPath);
        Assert.NotEqual(binding, source.Query!.AccessBinding);
        binding = source.Query.AccessBinding;
        authorization.Profile = "changed-grants";
        using var changed = await host.Client.GetAsync(HistoryPath);
        Assert.NotEqual(binding, source.Query!.AccessBinding);
        var reads = source.Calls;
        authorization.TenantScope = "tenant-b";
        using var foreign = await host.Client.GetAsync(HistoryPath);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(reads, source.Calls);
    }

    [Fact]
    public async Task Empty_scanned_windows_keep_advancing_continuations_and_explicit_scope_limitations()
    {
        var source = new HistorySource { EmptyWindow = true };
        await using var host = await HistoryHostAsync(source);
        var page = await host.Client.GetFromJsonAsync<RuntimeHistoryPageView>(HistoryPath + "?search=no-match");
        Assert.Empty(page!.Items);
        Assert.Equal(("next-window", "catch-up", 40L, 40, false),
            (page.NextContinuationToken, page.CatchUpToken, page.ScannedThroughSequence, page.ScannedCount, page.ScopeComplete));
        Assert.NotNull(page.ScopeLimitation);
    }

    [Fact]
    public async Task Uncaptured_and_unsupported_are_distinct_and_do_not_claim_complete_empty_history()
    {
        await using (var host = await HistoryHostAsync(new HistorySource { Uncaptured = true }, legacy: true))
        {
            var page = await host.Client.GetFromJsonAsync<RuntimeHistoryPageView>(HistoryPath);
            Assert.Equal("uncaptured", page!.Coverage.State);
            Assert.False(page.Coverage.CapturedFromStart);
            Assert.NotNull(page.Coverage.Limitation);
        }
        await using (var host = await HistoryHostAsync(null, legacy: true))
        {
            var page = await host.Client.GetFromJsonAsync<RuntimeHistoryPageView>(HistoryPath);
            Assert.Equal("unsupported", page!.Coverage.State);
            Assert.Null(page.CatchUpToken);
            using var exact = await host.Client.GetAsync(HistoryPath + "/events/future-event");
            Assert.Equal(HttpStatusCode.NotImplemented, exact.StatusCode);
        }
    }

    [Fact]
    public async Task Explicit_disabled_admission_remains_distinct_from_legacy_unknown_capture()
    {
        await using var host = await HistoryHostAsync(new HistorySource { Uncaptured = true }, legacy: true, captureAtAdmission: false);
        var page = await host.Client.GetFromJsonAsync<RuntimeHistoryPageView>(HistoryPath);
        Assert.Equal("disabled", page!.Coverage.State);
        Assert.False(page.Coverage.CapturedFromStart);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Promised_timer_source_responsibility_remains_pending_with_readable_committed_rows()
    {
        var source = new HistorySource { CapturePolicy = new("test-source", 2, true, ["FutureActivityKind", "TimerScheduled"]) };
        var sourceState = new InMemoryRuntimeCheckpointStoreState();
        var sourceLiveness = new InMemoryExecutionLivenessStateStore();
        var sourceExecutions = new InMemoryWorkflowExecutionStateStore(sourceState, sourceLiveness);
        await sourceLiveness.SaveAsync(new(RuntimeExecutionOwnershipStateId.For("wf-1"), "wf-1",
            new("lease", "wf-1", "owner", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1), 1), null, null, null));
        var timers = new InMemoryDurableTimerStore(new InMemoryDurableTimerStoreState(), new HistoryNativeAccess(),
            new FakeTimeProvider(DateTimeOffset.UnixEpoch), sourceState, sourceExecutions, sourceLiveness);
        var timer = new DurableTimer("timer", "wf-1", "elapsed", "hash", DateTimeOffset.UnixEpoch.AddMinutes(1), DateTimeOffset.UnixEpoch);
        var scheduled = DurableTimerHistoryFacts.CreateScheduled(timer, source.CapturePolicy)!;
        await timers.SaveAsync(timer with { HistoryResponsibility = new(source.CapturePolicy, [scheduled.Event], false) });
        await using var host = await HistoryHostAsync(source, nativeTimers: timers);
        var page = await host.Client.GetFromJsonAsync<RuntimeHistoryPageView>(HistoryPath);
        Assert.Equal("pending", page!.Coverage.State);
        Assert.True(page.Coverage.SourceHistoryPending);
        Assert.Single(page.Items);
        Assert.Equal(1, page.Coverage.SettledSequence);
        Assert.NotNull(page.CatchUpToken);
    }

    [Fact]
    public async Task Unavailable_promised_source_does_not_make_the_readable_prefix_look_complete()
    {
        var source = new HistorySource { CapturePolicy = new("test-source", 2, true, ["FutureActivityKind", "TimerScheduled"]) };
        await using var host = await HistoryHostAsync(source, timerHistoryUnavailable: true);
        var page = await host.Client.GetFromJsonAsync<RuntimeHistoryPageView>(HistoryPath);
        Assert.Equal("partial", page!.Coverage.State);
        Assert.Null(page.Coverage.SourceHistoryPending);
        Assert.NotNull(page.Coverage.SourceHistoryLimitation);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task Inconsistent_history_returns_an_explicit_invalid_range_and_conflict_for_exact_targets()
    {
        var source = new HistorySource { Invalid = true };
        await using var host = await HistoryHostAsync(source);
        var page = await host.Client.GetFromJsonAsync<RuntimeHistoryPageView>(HistoryPath);
        Assert.Equal("invalid", page!.Coverage.State);
        Assert.Empty(page.Items);
        using var exact = await host.Client.GetAsync(HistoryPath + "/events/future-event");
        Assert.Equal(HttpStatusCode.Conflict, exact.StatusCode);
        Assert.DoesNotContain("private-sentinel", await exact.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expiry_and_missing_native_handles_do_not_rewrite_the_recorded_bookmark_lifecycle()
    {
        var source = new HistorySource();
        var native = new InMemoryBookmarkStateStore();
        await native.SaveAsync(new("bookmark", "wf-1", "ae-1", "node", "continue", "task", "hash", null,
            new Dictionary<string, string>(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1)));
        await using var host = await HistoryHostAsync(source, nativeBookmarks: native);
        var exact = await host.Client.GetFromJsonAsync<RuntimeHistoryBookmarkDetailsView>(HistoryPath + "/bookmarks/bookmark");
        Assert.Equal(("Active", "expired", "CreationEvent", (string?)null),
            (exact!.Bookmark.Status, exact.Bookmark.Eligibility, exact.Bookmark.PriorState, exact.Bookmark.EndedHistoryEventId));
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMinutes(2), exact.Bookmark.EligibilityObservedAt);
        Assert.Equal("handle-expired", exact.Bookmark.EligibilityReason);
        await native.SaveAsync(new("bookmark", "wf-1", "ae-1", "node", "continue", "task", "hash", null,
            new Dictionary<string, string>(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(10)));
        var changed = await host.Client.GetFromJsonAsync<RuntimeHistoryBookmarkDetailsView>(HistoryPath + "/bookmarks/bookmark");
        Assert.Equal(("Active", "unknown", "handle-changed"),
            (changed!.Bookmark.Status, changed.Bookmark.Eligibility, changed.Bookmark.EligibilityReason));
        await native.DeleteAsync("wf-1", "bookmark");
        var missing = await host.Client.GetFromJsonAsync<RuntimeHistoryBookmarkDetailsView>(HistoryPath + "/bookmarks/bookmark");
        Assert.Equal(("Active", "unavailable"), (missing!.Bookmark.Status, missing.Bookmark.Eligibility));
        var page = await host.Client.GetFromJsonAsync<RuntimeHistoryBookmarkPageView>(HistoryPath + "/bookmarks");
        Assert.Null(page!.TotalCount);
        Assert.Null(page.ActiveCount);
        Assert.Equal(1L, Assert.Single(page.Items).LastSequence);
    }

    [Fact]
    public async Task Native_checkpoint_capture_is_shared_by_run_journal_exact_event_bookmark_and_fault_inspection()
    {
        var now = DateTimeOffset.UnixEpoch;
        var metadata = new Dictionary<string, string>();
        var policy = new RuntimeHistoryCapturePolicy("runtime-history-v3", 3, true,
            ["WorkflowStarted", "ActivitySuspended", "BookmarkCreated", "ActivityFaulted", "IncidentRecorded"]);
        var workflow = new WorkflowExecutionState("wf-1", new("artifact", "definition", "version", "1", "hash"),
            WorkflowExecutionStatus.Running, null, now, now, now, null, null, null, "tenant-a", metadata)
            { HistoryCapturePolicy = policy, HistoryCaptureEnabledAtAdmission = true };
        var state = new InMemoryRuntimeCheckpointStoreState();
        var workflows = new InMemoryWorkflowExecutionStateStore(state, null);
        var activities = new InMemoryActivityExecutionStateStore();
        var handles = new InMemoryBookmarkStateStore();
        var incidents = new InMemoryIncidentStateStore();
        var access = new HistoryNativeAccess();
        var writer = new InMemoryRuntimeCheckpointCommitStore(workflowExecutionStateStore: workflows,
            activityExecutionStateStore: activities, bookmarkStateStore: handles, incidentStateStore: incidents,
            state: state, persistenceAccessContextAccessor: access, rootWriteLeaseManager: new HistoryNativeLease());
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(now));
        services.AddSingleton<IPersistenceAccessContextAccessor>(access);
        services.AddSingleton<IWorkflowExecutionStateStore>(workflows);
        services.AddSingleton<IRuntimeRecoveryContinuationCodec>(new HmacRuntimeRecoveryContinuationCodec(
            Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "native-history-api-signing-key-at-least-32-bytes" })));
        services.AddSingleton(new RuntimeHistorySourceSupport([RuntimeHistorySourceFactKind.IncidentCreated]));
        services.AddRuntimeHistory();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IRuntimeHistoryStore>();
        var capability = Assert.Single(await new RuntimeHistoryCapabilitySource(writer, reader).GetCapabilitiesAsync());
        Assert.Equal(["workflow-history-bookmark", "workflow-history-bookmarks", "workflow-history-event", "workflow-history-page"],
            capability.Links.Select(link => link.Rel));
        Assert.Empty(await new RuntimeHistoryCapabilitySource(writer, new HistorySource()).GetCapabilitiesAsync());
        var capture = Assert.Single(scope.ServiceProvider.GetServices<IRuntimeCheckpointCommitEnricher>());
        var bookmark = new BookmarkState("bookmark", "wf-1", "ae-1", "node", "continue", "task", "hash", null,
            new Dictionary<string, string> { ["capability"] = "private-sentinel" }, now, null);
        var waitingActivity = new ActivityExecutionState(new("ae-1", "wf-1", "node", "authored", "Test", "1"),
            ActivityExecutionStatus.Suspended, null, 1, now, now, null, null, null, null, null,
            ActivitySchedulingProvenance.From("wf-1", null, null, null, null, null, null, "test"), 0,
            ["bookmark"], [], 0, 0, metadata);
        await CommitAsync("start", RuntimeCheckpointNames.WorkflowStarted, workflow);
        await CommitAsync("wait", RuntimeCheckpointNames.ActivitySuspended, activity: waitingActivity, handle: bookmark);
        await using var host = await StartAsync(_ => DispatchView(WorkflowExecutionCommandDispatchStatus.Accepted), collection =>
        {
            collection.AddSingleton<IWorkflowExecutionStateStore>(workflows);
            collection.AddSingleton<IActivityExecutionStateStore>(activities);
            collection.AddSingleton<IBookmarkStateStore>(handles);
            collection.AddSingleton<IRuntimeHistoryStore>(reader);
            collection.AddSingleton<IRuntimeCheckpointCommitStore>(writer);
            collection.AddSingleton<IRuntimeCheckpointCommitEnricher>(capture);
            collection.AddSingleton<IActivityInspectionContextAsync>(new BrowserAuthorization());
        });
        var run = await host.Client.GetFromJsonAsync<RuntimeHistoryPageView>(HistoryPath);
        Assert.Equal(["WorkflowStarted", "ActivitySuspended", "BookmarkCreated"], run!.Items.Select(item => item.Name));
        Assert.True(run.Coverage.CaptureAvailable);
        var journal = await host.Client.GetFromJsonAsync<RuntimeHistoryPageView>(HistoryPath + "?activityExecutionId=ae-1");
        Assert.Equal(run.Items.Skip(1).Select(item => item.HistoryEventId), journal!.Items.Select(item => item.HistoryEventId));
        var target = journal.Items.Last();
        var exact = await host.Client.GetFromJsonAsync<RuntimeHistoryEventDetailsView>(HistoryPath + "/events/" + Uri.EscapeDataString(target.HistoryEventId));
        Assert.Equal(target.HistoryEventId, exact!.Event.HistoryEventId);
        var wait = await host.Client.GetFromJsonAsync<RuntimeHistoryBookmarkDetailsView>(HistoryPath + "/bookmarks/bookmark");
        Assert.Equal(("Active", "eligible", target.HistoryEventId),
            (wait!.Bookmark.Status, wait.Bookmark.Eligibility, wait.Bookmark.CreatedHistoryEventId));
        var faultIncident = new IncidentState("incident", "wf-1", "ae-1", "node", IncidentSeverity.Error, IncidentStatus.Blocking,
            null, "TestFailure", "private-sentinel", now, null);
        await CommitAsync("fault", RuntimeCheckpointNames.IncidentRecorded,
            activity: waitingActivity with { Status = ActivityExecutionStatus.Faulted }, incident: faultIncident);
        var fault = await host.Client.GetFromJsonAsync<RuntimeHistoryPageView>(HistoryPath);
        Assert.Equal(["ActivityFaulted", "IncidentRecorded"], fault!.Items.Skip(3).Select(item => item.Name));
        Assert.DoesNotContain("private-sentinel", JsonSerializer.Serialize(fault), StringComparison.Ordinal);
        var ineligible = await host.Client.GetFromJsonAsync<RuntimeHistoryBookmarkDetailsView>(HistoryPath + "/bookmarks/bookmark");
        Assert.Equal(("Active", "ineligible", "activity-not-waiting"),
            (ineligible!.Bookmark.Status, ineligible.Bookmark.Eligibility, ineligible.Bookmark.EligibilityReason));

        async Task CommitAsync(string id, string name, WorkflowExecutionState? runState = null,
            ActivityExecutionState? activity = null, BookmarkState? handle = null, IncidentState? incident = null)
        {
            var source = new RuntimeCheckpointCommit(id, new("checkpoint-" + id, name, "wf-1", now,
                activity is null ? [] : [activity.InvocationId], metadata),
                new(runState is null ? null : new("wf-1", RuntimeStateChangeOperation.Upsert, runState, metadata), null,
                    activity is null ? [] : [new(activity.InvocationId, RuntimeStateChangeOperation.Upsert, activity, metadata)],
                    handle is null ? [] : [new(handle.BookmarkId, RuntimeStateChangeOperation.Upsert, handle, metadata)], [],
                    incident is null ? [] : [new(incident.IncidentId, RuntimeStateChangeOperation.Upsert, incident, metadata)], []), [], metadata)
                { HistorySourceFacts = incident is null ? null : RuntimeHistorySourceFacts.ForPolicy(policy, [RuntimeHistoryIncidentFacts.Created(incident)]) };
            var captured = await capture.EnrichAsync(source);
            await capture.ValidateFinalAsync(captured);
            await writer.CommitAsync(captured, new(RuntimeCheckpointPersistenceMode.Immediate));
        }
    }

    private sealed class HistoryNativeAccess : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new("tenant-a"));
    }

    private sealed class HistoryNativeLease : IWorkflowExecutableRootWriteLeaseManager
    {
        public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write,
            CancellationToken cancellationToken = default) => write(cancellationToken);
    }

    private static async Task<BehaviorHost> HistoryHostAsync(HistorySource? source, BrowserAuthorization? authorization = null,
        bool legacy = false, IBookmarkStateStore? nativeBookmarks = null, bool? captureAtAdmission = null, IDurableTimerStore? nativeTimers = null,
        bool timerHistoryUnavailable = false, IPostCommitOutboxLookupStore? nativeSources = null)
    {
        var workflows = new InMemoryWorkflowExecutionStateStore();
        await workflows.SaveAsync(new WorkflowExecutionState("wf-1", new("artifact", "definition", "version", "1", "hash"),
            WorkflowExecutionStatus.Suspended, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch, null, null, null, "tenant-a", new Dictionary<string, string>())
            { HistoryCapturePolicy = legacy ? null : source?.CapturePolicy ?? HistorySource.Policy, HistoryCaptureEnabledAtAdmission = captureAtAdmission });
        return await StartAsync(_ => DispatchView(WorkflowExecutionCommandDispatchStatus.Accepted), services =>
        {
            services.AddSingleton<IWorkflowExecutionStateStore>(workflows);
            services.AddSingleton<IActivityInspectionContextAsync>(authorization ?? new BrowserAuthorization());
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMinutes(2)));
            if (source is not null) services.AddSingleton<IRuntimeHistoryStore>(source);
            if (nativeBookmarks is not null) services.AddSingleton(nativeBookmarks);
            if (timerHistoryUnavailable) services.AddSingleton<IDurableTimerStore>(new HistoryUnsupportedTimerStore());
            if (nativeTimers is not null) services.AddSingleton(nativeTimers);
            if (nativeSources is not null) services.AddSingleton(nativeSources);
        });
    }

    // A valid legacy operational provider has no source-history responsibility capability. Its ordinary
    // timer reads must not be used to infer that a promised history source is settled.
    private sealed class HistoryUnsupportedTimerStore : IDurableTimerStore
    {
        public ValueTask<DurableTimer> SaveAsync(DurableTimer timer, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Operational access is outside this history read.");
        public ValueTask<IReadOnlyCollection<DurableTimer>> ListDueAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Operational access is outside this history read.");
        public ValueTask<DurableTimer?> FindAsync(string workflowExecutionId, string timerId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Operational access is outside this history read.");
        public ValueTask DeleteAsync(string workflowExecutionId, string timerId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Operational access is outside this history read.");
    }

    private sealed class HistorySource : IRuntimeHistoryStore
    {
        internal static readonly RuntimeHistoryCapturePolicy Policy = new("test", 1, true, ["FutureActivityKind"]);
        internal RuntimeHistoryCapturePolicy CapturePolicy { get; init; } = Policy;
        private RuntimeHistoryHead Head => new("wf-1", 1, 1, "physical", DateTimeOffset.UnixEpoch,
            CapturePolicy, true, false, 0);
        internal int Calls { get; private set; }
        internal RuntimeHistoryQuery? Query { get; private set; }
        internal bool EmptyWindow { get; init; }
        internal bool Uncaptured { get; init; }
        internal bool Invalid { get; init; }
        internal RuntimeHistoryEvent? DeliverySource { get; init; }
        internal IReadOnlyDictionary<string, string>? EventMetadata { get; init; }
        internal bool MoreSourceHistory { get; init; }
        internal bool AdvanceAfterSourceInspection { get; init; }
        internal List<RuntimeHistoryQuery> SourceQueries { get; } = [];
        public ValueTask<RuntimeHistoryHead?> FindHeadAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        { Calls++; return ValueTask.FromResult<RuntimeHistoryHead?>(Uncaptured ? null : AdvanceAfterSourceInspection && SourceQueries.Count != 0 ? Head with { LastSequence = 2 } : Head); }
        public ValueTask<RuntimeHistoryPage> ReadPageAsync(RuntimeHistoryQuery query, CancellationToken cancellationToken = default)
        {
            Calls++; Query = query;
            if (Invalid) throw new RuntimeHistoryIntegrityException(query.WorkflowExecutionId, "private-sentinel");
            if (query.Name is "StimulusPublicationCommitted" or "ChildWorkflowSettled" or "ChildWorkflowDispatchRequested")
            {
                SourceQueries.Add(query);
                return ValueTask.FromResult(new RuntimeHistoryPage(Head, DeliverySource is null ? [] : [DeliverySource],
                    1, 1, DeliverySource is null ? 0 : 1, MoreSourceHistory ? "more-source-history" : null, "catch-up"));
            }
            return ValueTask.FromResult(Uncaptured ? new(null, [], 0, 0, 0, null, "catch-up") : EmptyWindow
                ? new(Head, [], 40, 40, 40, "next-window", "catch-up") { ScopeComplete = false, ScopeLimitation = "Missing recorded ancestry." }
                : new RuntimeHistoryPage(Head, [Event()], 1, 1, 1, null, "catch-up"));
        }
        public ValueTask<RuntimeHistoryEvent?> FindEventAsync(string workflowExecutionId, string historyEventId, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Invalid) throw new RuntimeHistoryIntegrityException(workflowExecutionId, "private-sentinel");
            return ValueTask.FromResult<RuntimeHistoryEvent?>(Event());
        }
        public ValueTask<RuntimeHistoryBookmarkPage> ListBookmarksAsync(RuntimeHistoryBookmarkQuery query, CancellationToken cancellationToken = default)
        { Calls++; return ValueTask.FromResult(new RuntimeHistoryBookmarkPage(Head, [Bookmark()], null)); }
        public ValueTask<RuntimeHistoryBookmark?> FindBookmarkAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default)
        { Calls++; return ValueTask.FromResult<RuntimeHistoryBookmark?>(Bookmark()); }
        private RuntimeHistoryEvent Event() => new("future-event", "wf-1", 1, RuntimeHistoryEventCategory.ActivityLifecycle,
            "FutureActivityKind", DateTimeOffset.UnixEpoch, "ae-1", "node", "logical", payload: JsonSerializer.SerializeToElement("private-sentinel"),
            metadata: EventMetadata ?? new Dictionary<string, string> { ["private"] = "private-sentinel", ["history.status"] = "Suspended" })
            { SchemaVersion = 2, Authority = RuntimeHistoryAuthority.CommittedTransition, PhysicalCommitId = "physical", CheckpointOrdinal = 3 };
        private static RuntimeHistoryBookmark Bookmark() => new(new(new("bookmark", "wf-1", "ae-1", "continue"), "task", "hash",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1)), "node", RuntimeHistoryBookmarkStatus.Active,
            "future-event", null, "future-event", 1, 1, null, null, null, RuntimeHistoryPriorState.CreationEvent);
    }
}
