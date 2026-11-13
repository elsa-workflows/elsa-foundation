using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Workflows.Runtime.Api.Capabilities;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

// Actual mapped HTTP endpoints/services over controlled read projections. These do not qualify
// physical history capture, provider isolation, replay, source settlement or Studio acceptance.
public sealed partial class RuntimeHistoryApiTests
{
    private const string Path = "/runtime/workflows/instances/run/history";
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly RuntimeHistoryCapturePolicy Policy = new("runtime-history-v3", 3, true, ["WorkflowStarted"]);

    [Theory]
    [InlineData("")]
    [InlineData("/events/event")]
    [InlineData("/bookmarks")]
    [InlineData("/bookmarks/bookmark")]
    [InlineData("/occurrences")]
    public async Task Every_route_requires_read_permission_and_reauthorizes_before_history_access(string suffix)
    {
        await using var host = await Host.StartAsync();
        using var anonymous = await host.Client.GetAsync(Path + suffix);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        host.Client.DefaultRequestHeaders.Add("X-History-Test", "wrong-permission");
        using var forbidden = await host.Client.GetAsync(Path + suffix);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(0, host.Authorization.Reads);
        Assert.Equal(0, host.History.Reads);

        host.Authenticate();
        host.Authorization.Allowed = false;
        using var denied = await host.Client.GetAsync(Path + suffix);
        using var missing = await host.Client.GetAsync(Path.Replace("/run/", "/missing/") + suffix);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal(denied.StatusCode, missing.StatusCode);
        Assert.Equal(await denied.Content.ReadAsStringAsync(), await missing.Content.ReadAsStringAsync());
        Assert.Equal(0, host.History.Reads);
        Assert.Equal(0, host.Authorization.ValueReads);
    }

    [Fact]
    public async Task Page_forwards_one_bounded_query_and_an_empty_advancing_window_without_traversal()
    {
        await using var host = await Host.StartAsync();
        host.Authenticate();
        host.History.Page = new(Head() with { LastSequence = 100 }, [], 100, 40, 20, "next-window", "catch-up")
            { ScopeComplete = false, ScopeLimitation = "Ancestry incomplete." };
        using var response = await host.Client.GetAsync(Path + "?limit=20&cursor=original&activityExecutionId=activity&includeDescendants=true&bookmarkId=bookmark&category=BookmarkLifecycle&name=BookmarkConsumed&search=needle");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var query = Assert.IsType<RuntimeHistoryQuery>(host.History.Query);
        Assert.Equal((20, "original", "activity", true, "bookmark", RuntimeHistoryEventCategory.BookmarkLifecycle, "BookmarkConsumed", "needle"),
            (query.Limit, query.ContinuationToken, query.ActivityExecutionId, query.IncludeDescendants, query.BookmarkId, query.Category, query.Name, query.Search));
        Assert.Equal(RuntimeHistoryIdentity.CreateBinding(["tenant-a", "operator", "structure-only"]), query.AccessBinding);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, json.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal("next-window", json.RootElement.GetProperty("nextContinuationToken").GetString());
        Assert.Equal(40, json.RootElement.GetProperty("scannedThroughSequence").GetInt64());
        Assert.False(json.RootElement.GetProperty("scopeComplete").GetBoolean());
        Assert.Equal(1, host.History.Reads);
        host.Authorization.Subject = "replacement-operator";
        using var refreshed = await host.Client.GetAsync(Path);
        Assert.NotEqual(query.AccessBinding, host.History.Query!.AccessBinding);
    }

    [Theory]
    [InlineData("?limit=501")]
    [InlineData("?includeDescendants=true")]
    [InlineData("?category=999")]
    [InlineData("/bookmarks?limit=0")]
    [InlineData("/bookmarks?status=999")]
    [InlineData("?limit=invalid")]
    public async Task Invalid_queries_have_safe_history_problems_and_do_not_reach_the_projection(string suffix)
    {
        await using var host = await Host.StartAsync();
        host.Authenticate();
        using var response = await host.Client.GetAsync(Path + suffix);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("runtime.history.request-invalid", json.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal(0, host.History.Reads);
    }

    [Fact]
    public async Task Exact_common_envelope_uses_one_snapshot_and_withholds_unknown_occurrence_and_payload()
    {
        await using var host = await Host.StartAsync();
        host.Authenticate();
        host.History.Event = new("event", "run", 1, RuntimeHistoryEventCategory.IncidentLifecycle,
            "future.kind", Now, payload: JsonSerializer.SerializeToElement("secret-payload"),
            metadata: new Dictionary<string, string> { ["history.reasonCode"] = "safe-code", ["secret"] = "secret-metadata" })
            { SchemaVersion = 99, RecordedAt = Now.AddMinutes(2), OccurredAtKnown = false, ActivityAncestryKnown = false };
        using var response = await host.Client.GetAsync(Path + "/events/event");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret-payload", body);
        Assert.DoesNotContain("secret-metadata", body);
        using var json = JsonDocument.Parse(body);
        var item = json.RootElement.GetProperty("event");
        Assert.Equal("future.kind", item.GetProperty("name").GetString());
        Assert.Equal(99, item.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("occurredAt").ValueKind);
        Assert.False(item.GetProperty("occurredAtKnown").GetBoolean());
        Assert.Equal(Now.AddMinutes(2), item.GetProperty("recordedAt").GetDateTimeOffset());
        Assert.Equal("safe-code", item.GetProperty("metadata").GetProperty("history.reasonCode").GetString());
        Assert.Equal(1, host.History.Reads);
        Assert.Equal(0, host.History.IndependentExactReads);
        Assert.Equal(0, host.Authorization.ValueReads);
    }

    [Theory]
    [InlineData("integrity", "/events/event", HttpStatusCode.Conflict, "runtime.history.invalid")]
    [InlineData("unavailable", "/events/event", HttpStatusCode.ServiceUnavailable, "runtime.history.unavailable")]
    [InlineData("argument", "", HttpStatusCode.BadRequest, "runtime.history.request-invalid")]
    [InlineData("unavailable", "", HttpStatusCode.ServiceUnavailable, "runtime.history.unavailable")]
    public async Task Read_failure_classes_remain_distinct_without_provider_details(string failure, string suffix, HttpStatusCode status, string code)
    {
        await using var host = await Host.StartAsync();
        host.Authenticate();
        host.History.Failure = failure switch
        {
            "integrity" => new RuntimeHistoryIntegrityException("run", "provider-secret"),
            "unavailable" => new RuntimeHistoryUnavailableException("run", "provider-secret", new IOException("provider-secret")),
            _ => new ArgumentException("provider-secret")
        };
        using var response = await host.Client.GetAsync(Path + suffix);
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("provider-secret", body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(code, json.RootElement.GetProperty("errorCode").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("/bookmarks")]
    public async Task Contradictory_pages_return_invalid_coverage_without_rows(string suffix)
    {
        await using var host = await Host.StartAsync();
        host.Authenticate();
        host.History.Failure = new RuntimeHistoryIntegrityException("run", "provider-secret");
        using var response = await host.Client.GetAsync(Path + suffix);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("invalid", json.RootElement.GetProperty("coverage").GetProperty("state").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData("event-after-head")]
    [InlineData("event-before-retained")]
    [InlineData("event-purged")]
    [InlineData("event-reversed")]
    [InlineData("bookmark-after-head")]
    [InlineData("bookmark-no-first-position")]
    public async Task Page_and_inventory_positions_must_agree_with_their_committed_snapshot(string scenario)
    {
        await using var host = await Host.StartAsync();
        host.Authenticate();
        var head = scenario switch
        {
            "event-after-head" => Head() with { LastSequence = 0 },
            "event-before-retained" => Head() with { FirstSequence = 2, LastSequence = 2 },
            "event-purged" => Head() with { FirstSequence = 2, PurgedThroughSequence = 1 },
            "event-reversed" => Head() with { LastSequence = 2 },
            _ => Head()
        };
        var events = new List<RuntimeHistoryEvent> { host.History.Event! };
        if (scenario == "event-reversed") events.Insert(0, new("event-2", "run", 2, RuntimeHistoryEventCategory.WorkflowLifecycle, "WorkflowStarted", Now));
        host.History.Page = new(head, events, head.LastSequence, head.LastSequence, events.Count, null, "catch-up");
        host.History.BookmarkPage = new(head, [host.History.Bookmark! with
            { LastSequence = scenario == "bookmark-after-head" ? 2 : 1, FirstRecordedSequence = scenario == "bookmark-no-first-position" ? 0 : 1 }], null);
        using var response = await host.Client.GetAsync(Path + (scenario.StartsWith("bookmark", StringComparison.Ordinal) ? "/bookmarks" : ""));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("invalid", json.RootElement.GetProperty("coverage").GetProperty("state").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task A_capture_owner_outage_keeps_retained_history_readable_and_denied_reads_lazy()
    {
        var resolutions = 0;
        await using var host = await Host.StartAsync(services =>
        {
            services.AddSingleton<IRuntimeCheckpointCommitStore>(new Writer(true));
            services.AddScoped<IRuntimeCheckpointCommitEnricher>(_ => { resolutions++; throw new IOException("capture-secret"); });
        });
        host.Authenticate();
        host.Authorization.Allowed = false;
        using var denied = await host.Client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal(0, resolutions);
        host.Authorization.Allowed = true;
        using var response = await host.Client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("capture-secret", body);
        using var json = JsonDocument.Parse(body);
        Assert.False(json.RootElement.GetProperty("coverage").GetProperty("captureAvailable").GetBoolean());
        Assert.Equal(1, json.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(1, resolutions);
    }

    [Theory]
    [InlineData(false, false, "uncaptured")]
    [InlineData(true, false, "disabled")]
    [InlineData(false, true, "pending")]
    public async Task Coverage_uses_original_admission_choice_not_current_host_options(bool disabled, bool pinned, string expected)
    {
        var workflows = new InMemoryWorkflowExecutionStateStore();
        await workflows.SaveAsync(Workflow() with { HistoryCapturePolicy = pinned ? Policy : null, HistoryCaptureEnabledAtAdmission = disabled ? false : null });
        var history = new Projection { Head = null, Page = new(null, [], 0, 0, 0, null, "catch-up") };
        var view = await new RuntimeHistoryInspectionService(workflows, new Authorization(), new FakeTimeProvider(Now), history).ReadAsync(new("run"));
        Assert.Equal(expected, view.Coverage.State);
    }

    [Fact]
    public async Task Missing_reader_is_honest_and_missing_composite_contract_never_rejoins_independent_details()
    {
        var workflows = new InMemoryWorkflowExecutionStateStore();
        await workflows.SaveAsync(Workflow());
        var service = new RuntimeHistoryInspectionService(workflows, new Authorization(), new FakeTimeProvider(Now));
        Assert.Equal("unsupported", (await service.ReadAsync(new("run"))).Coverage.State);
        var projection = new BaseProjection();
        service = new(workflows, new Authorization(), new FakeTimeProvider(Now), projection);
        var failure = await Record.ExceptionAsync(() => service.FindEventAsync(new("run", "event")));
        Assert.NotNull(failure);
        Assert.Equal("RuntimeHistoryUnsupportedSignal", failure.GetType().Name);
        Assert.Equal(0, projection.Reads);
    }

    [Fact]
    public async Task Retained_source_profile_without_its_settlement_owner_is_partial_not_zero_pending()
    {
        var workflows = new InMemoryWorkflowExecutionStateStore();
        var policy = new RuntimeHistoryCapturePolicy("runtime-history-v3", 3, true, ["TimerDispatched"]);
        await workflows.SaveAsync(Workflow() with { HistoryCapturePolicy = policy });
        var history = new Projection { Head = Head() with { Policy = policy } };
        history.Page = new(history.Head, [], 1, 1, 1, null, "catch-up");
        var view = await new RuntimeHistoryInspectionService(workflows, new Authorization(), new FakeTimeProvider(Now), history).ReadAsync(new("run"));
        Assert.Equal("partial", view.Coverage.State);
        Assert.Null(view.Coverage.SourceHistoryPending);
        Assert.NotNull(view.Coverage.SourceHistoryLimitation);
    }

    [Fact]
    public async Task Bookmark_HTTP_reads_preserve_unknown_counts_and_use_the_composite_detail()
    {
        await using var host = await Host.StartAsync();
        host.Authenticate();
        using var page = await host.Client.GetAsync(Path + "/bookmarks?limit=10&cursor=bookmark-next&activityExecutionId=activity&includeDescendants=true&status=Active");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var query = Assert.IsType<RuntimeHistoryBookmarkQuery>(host.History.BookmarkQuery);
        Assert.Equal((10, "bookmark-next", "activity", true), (query.Limit, query.ContinuationToken, query.ActivityExecutionId, query.IncludeDescendants));
        Assert.Equal(RuntimeHistoryBookmarkStatus.Active, query.Status);
        Assert.Equal(RuntimeHistoryIdentity.CreateBinding(["tenant-a", "operator", "structure-only"]), query.AccessBinding);
        using var json = JsonDocument.Parse(await page.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("totalCount").ValueKind);
        using var detail = await host.Client.GetAsync(Path + "/bookmarks/bookmark");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var exact = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.Equal("Active", exact.RootElement.GetProperty("bookmark").GetProperty("status").GetString());
        Assert.Equal(2, host.History.Reads);
        Assert.Equal(0, host.History.IndependentExactReads);
    }

    [Fact]
    public async Task Optional_reader_absence_and_legacy_details_return_honest_HTTP_support()
    {
        await using var unsupported = await Host.StartAsync(includeHistory: false);
        unsupported.Authenticate();
        using var page = await unsupported.Client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        using var json = JsonDocument.Parse(await page.Content.ReadAsStringAsync());
        Assert.Equal("unsupported", json.RootElement.GetProperty("coverage").GetProperty("state").GetString());
        using var detail = await unsupported.Client.GetAsync(Path + "/events/event");
        Assert.Equal(HttpStatusCode.NotImplemented, detail.StatusCode);
        await using var legacy = await Host.StartAsync(services => services.AddSingleton<IRuntimeHistoryStore>(new BaseProjection()));
        legacy.Authenticate();
        using var legacyDetail = await legacy.Client.GetAsync(Path + "/bookmarks/bookmark");
        Assert.Equal(HttpStatusCode.NotImplemented, legacyDetail.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_exact_target_cannot_return_a_different_requested_identity(bool bookmark)
    {
        await using var host = await Host.StartAsync();
        host.Authenticate();
        using var response = await host.Client.GetAsync(Path + (bookmark ? "/bookmarks/other" : "/events/other"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("event-after-head")]
    [InlineData("event-before-retained")]
    [InlineData("event-purged")]
    [InlineData("bookmark-after-head")]
    [InlineData("bookmark-no-first-position")]
    [InlineData("bookmark-reversed-positions")]
    public async Task Exact_positions_must_belong_to_the_acknowledged_composite_snapshot(string scenario)
    {
        await using var host = await Host.StartAsync();
        host.Authenticate();
        if (scenario == "event-after-head") host.History.Head = Head() with { LastSequence = 0 };
        if (scenario == "event-before-retained") host.History.Head = Head() with { FirstSequence = 2, LastSequence = 2 };
        if (scenario == "event-purged") host.History.Head = Head() with { FirstSequence = 2, PurgedThroughSequence = 1 };
        if (scenario == "bookmark-after-head") host.History.Bookmark = host.History.Bookmark! with { LastSequence = 2 };
        if (scenario == "bookmark-no-first-position") host.History.Bookmark = host.History.Bookmark! with { FirstRecordedSequence = 0 };
        if (scenario == "bookmark-reversed-positions") host.History.Bookmark = host.History.Bookmark! with { FirstRecordedSequence = 2 };
        using var response = await host.Client.GetAsync(Path + (scenario.StartsWith("event", StringComparison.Ordinal) ? "/events/event" : "/bookmarks/bookmark"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_head_cannot_replace_absent_or_known_disabled_native_admission_policy(bool disabled)
    {
        var workflows = new InMemoryWorkflowExecutionStateStore();
        await workflows.SaveAsync(Workflow() with { HistoryCapturePolicy = null, HistoryCaptureEnabledAtAdmission = disabled ? false : null });
        var service = new RuntimeHistoryInspectionService(workflows, new Authorization(), new FakeTimeProvider(Now), new Projection());
        Assert.Equal("invalid", (await service.ReadAsync(new("run"))).Coverage.State);
        Assert.Equal("invalid", (await service.ListBookmarksAsync(new("run"))).Coverage.State);
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => service.FindEventAsync(new("run", "event")));
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => service.FindBookmarkAsync(new("run", "bookmark")));
    }

    [Fact]
    public async Task Cancellation_propagates_without_becoming_invalid_history()
    {
        var workflows = new InMemoryWorkflowExecutionStateStore();
        await workflows.SaveAsync(Workflow());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var history = new Projection();
        var service = new RuntimeHistoryInspectionService(workflows, new Authorization(), new FakeTimeProvider(Now), history);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadAsync(new("run"), cancelled.Token));
        Assert.Equal(0, history.Reads);
    }

    [Theory]
    [InlineData("eligible", "eligible", null)]
    [InlineData("missing", "unavailable", "native-handle-missing")]
    [InlineData("expired", "expired", "handle-expired")]
    [InlineData("changed", "unknown", "handle-changed")]
    [InlineData("terminal", "ineligible", "workflow-terminal")]
    [InlineData("not-waiting", "ineligible", "activity-not-waiting")]
    [InlineData("superseded", "ineligible", "activity-superseded")]
    [InlineData("unowned", "ineligible", "bookmark-not-owned")]
    public async Task Bookmark_history_is_separate_from_current_eligibility_and_never_consumed_by_inspection(string scenario, string expected, string? reason)
    {
        var workflows = new InMemoryWorkflowExecutionStateStore();
        await workflows.SaveAsync(Workflow() with { Status = scenario == "terminal" ? WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Running });
        var native = NativeBookmark(scenario == "expired" ? Now.AddSeconds(-1) : null);
        var history = new Projection { Bookmark = Bookmark(native) };
        var bookmarks = new InMemoryBookmarkStateStore();
        if (scenario != "missing") await bookmarks.SaveAsync(scenario == "changed" ? native with { StimulusHash = "changed" } : native);
        var activities = new InMemoryActivityExecutionStateStore();
        var owner = new ActivityExecutionState(new("activity", "run", "node", "authored", "Wait", "1"),
            scenario == "superseded" ? ActivityExecutionStatus.Superseded :
                scenario == "not-waiting" ? ActivityExecutionStatus.Completed : ActivityExecutionStatus.Suspended,
            null, 1, Now, Now, null, null, null, null, null, ActivitySchedulingProvenance.Empty, null,
            scenario == "unowned" ? [] : ["bookmark"], [], 0, 0, new Dictionary<string, string>())
            {
                SupersededAt = scenario == "superseded" ? Now : null,
                SupersededByActivityExecutionId = scenario == "superseded" ? "replacement-activity" : null
            };
        await activities.SaveAsync(owner);
        var service = new RuntimeHistoryInspectionService(workflows, new Authorization(), new FakeTimeProvider(Now), history, bookmarks, activities);
        var view = await service.FindBookmarkAsync(new("run", "bookmark"));
        Assert.Equal("Active", view.Bookmark.Status);
        Assert.Equal(expected, view.Bookmark.Eligibility);
        Assert.Equal(reason, view.Bookmark.EligibilityReason);
        Assert.Equal(Now, view.Bookmark.EligibilityObservedAt);
        Assert.Null(view.Bookmark.EndedHistoryEventId);
        Assert.Equal(1, history.Reads);
        Assert.Equal(0, history.IndependentExactReads);
        Assert.Equal(scenario != "missing", await bookmarks.FindAsync("run", "bookmark") is not null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Eligibility_uses_one_observation_instant_after_an_owner_read_crosses_expiry(bool testScope)
    {
        var clock = new FakeTimeProvider(Now);
        var workflows = new InMemoryWorkflowExecutionStateStore();
        var workflow = Workflow();
        if (testScope) workflow = workflow with { TestScope = new("test-scope", Now.AddSeconds(1), "tenant-a", new("test")) };
        await workflows.SaveAsync(workflow);
        var native = NativeBookmark(testScope ? null : Now.AddSeconds(1));
        var bookmarks = new InMemoryBookmarkStateStore();
        await bookmarks.SaveAsync(native);
        var owner = new ActivityExecutionState(new("activity", "run", "node", "authored", "Wait", "1"), ActivityExecutionStatus.Suspended,
            null, 1, Now, Now, null, null, null, null, null, ActivitySchedulingProvenance.Empty, null,
            ["bookmark"], [], 0, 0, new Dictionary<string, string>());
        var activities = new AdvancingOwner(owner, () => clock.Advance(TimeSpan.FromSeconds(2)));
        var service = new RuntimeHistoryInspectionService(workflows, new Authorization(), clock, new Projection { Bookmark = Bookmark(native) }, bookmarks, activities);
        var view = await service.FindBookmarkAsync(new("run", "bookmark"));
        Assert.Equal("Active", view.Bookmark.Status);
        Assert.Equal(testScope ? "ineligible" : "expired", view.Bookmark.Eligibility);
        Assert.Equal(testScope ? "test-scope-expired" : "handle-expired", view.Bookmark.EligibilityReason);
        Assert.Equal(Now.AddSeconds(2), view.Bookmark.EligibilityObservedAt);
        Assert.NotNull(await bookmarks.FindAsync("run", "bookmark"));
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 2)]
    [InlineData(true, true, 4)]
    public async Task Capabilities_require_actual_pairing_and_composite_details(bool paired, bool details, int count)
    {
        IRuntimeHistoryStore reader = details ? new Projection() : new BaseProjection();
        var declarations = await new RuntimeHistoryCapabilitySource(new Writer(paired), reader).GetCapabilitiesAsync();
        Assert.Equal(count, declarations.SelectMany(item => item.Links).Count());
        if (count == 2) Assert.DoesNotContain(declarations.SelectMany(item => item.Links), item => item.Rel == "workflow-history-event");
    }

    private static RuntimeHistoryHead Head() => new("run", 1, 1, "physical", Now, Policy, true, false, 0);
    private static WorkflowExecutionState Workflow() => new("run", new("artifact", "definition", "version", "1", "hash"), WorkflowExecutionStatus.Running,
        null, Now, Now, Now, null, null, null, "tenant-a", new Dictionary<string, string>())
        { HistoryCapturePolicy = Policy, HistoryCaptureEnabledAtAdmission = true };
    private static BookmarkState NativeBookmark(DateTimeOffset? expires = null) => new("bookmark", "run", "activity", "node", "resume", "signal", "hash", null, new Dictionary<string, string>(), Now, expires);
    private static RuntimeHistoryBookmark Bookmark(BookmarkState native) => new(ActivityExecutionBookmarkSummary.From(native), "node", RuntimeHistoryBookmarkStatus.Active,
        "created", null, "created", 1, 1, null, null, null, RuntimeHistoryCreationEvidence.CreationEvent);

    private class BaseProjection : IRuntimeHistoryStore
    {
        public int Reads { get; protected set; }
        public int IndependentExactReads { get; private set; }
        public RuntimeHistoryHead? Head { get; set; } = RuntimeHistoryApiTests.Head();
        public RuntimeHistoryQuery? Query { get; private set; }
        public RuntimeHistoryBookmarkQuery? BookmarkQuery { get; private set; }
        public RuntimeHistoryPage Page { get; set; } = new(RuntimeHistoryApiTests.Head(), [new("event", "run", 1, RuntimeHistoryEventCategory.WorkflowLifecycle, "WorkflowStarted", Now)], 1, 1, 1, null, "catch-up");
        public RuntimeHistoryBookmarkPage? BookmarkPage { get; set; }
        public Exception? Failure { get; set; }
        protected void Read(CancellationToken token) { token.ThrowIfCancellationRequested(); Reads++; if (Failure is { } error) throw error; }
        public ValueTask<RuntimeHistoryHead?> FindHeadAsync(string run, CancellationToken token = default) { IndependentExactReads++; throw new InvalidOperationException("Independent read forbidden."); }
        public ValueTask<RuntimeHistoryEvent?> FindEventAsync(string run, string id, CancellationToken token = default) { IndependentExactReads++; throw new InvalidOperationException("Independent read forbidden."); }
        public ValueTask<RuntimeHistoryBookmark?> FindBookmarkAsync(string run, string id, CancellationToken token = default) { IndependentExactReads++; throw new InvalidOperationException("Independent read forbidden."); }
        public ValueTask<RuntimeHistoryPage> ReadPageAsync(RuntimeHistoryQuery query, CancellationToken token = default) { Read(token); Query = query; return ValueTask.FromResult(Page); }
        public ValueTask<RuntimeHistoryBookmarkPage> ListBookmarksAsync(RuntimeHistoryBookmarkQuery query, CancellationToken token = default) { Read(token); BookmarkQuery = query; return ValueTask.FromResult(BookmarkPage ?? new RuntimeHistoryBookmarkPage(Head, [], null)); }
    }

    private sealed class Projection : BaseProjection, IRuntimeHistoryDetailsStore
    {
        public RuntimeHistoryEvent? Event { get; set; } = new("event", "run", 1, RuntimeHistoryEventCategory.WorkflowLifecycle, "WorkflowStarted", Now);
        public RuntimeHistoryBookmark? Bookmark { get; set; } = RuntimeHistoryApiTests.Bookmark(NativeBookmark());
        public ValueTask<(RuntimeHistoryHead? Head, RuntimeHistoryEvent? Event)> FindEventDetailsAsync(string run, string id, CancellationToken token = default) { Read(token); return ValueTask.FromResult((Head, Event)); }
        public ValueTask<(RuntimeHistoryHead? Head, RuntimeHistoryBookmark? Bookmark)> FindBookmarkDetailsAsync(string run, string id, CancellationToken token = default) { Read(token); return ValueTask.FromResult((Head, Bookmark)); }
    }

    private sealed class Writer(bool paired) : IRuntimeCheckpointCommitStore
    {
        public bool SupportsHistory => paired;
        public bool CanCommitHistory(IRuntimeHistoryStore reader) => paired;
        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken token = default) => throw new InvalidOperationException("Inspection cannot write.");
    }

    private sealed class AdvancingOwner(ActivityExecutionState owner, Action advance) : IActivityExecutionStateStore
    {
        public ValueTask<ActivityExecutionState?> FindAsync(string run, string activity, CancellationToken token = default) { advance(); return ValueTask.FromResult<ActivityExecutionState?>(owner); }
        public ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken token = default) => throw new InvalidOperationException("Inspection cannot write.");
        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListPageAsync(ActivityExecutionStatePageQuery query, CancellationToken token = default) => throw new InvalidOperationException("Inspection must use exact lookup.");
        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListByParentPageAsync(ActivityExecutionStateParentPageQuery query, CancellationToken token = default) => throw new InvalidOperationException("Inspection must use exact lookup.");
    }

    private sealed class Authorization : IActivityInspectionContextAsync
    {
        public bool Allowed { get; set; } = true;
        public int Reads { get; private set; }
        public int ValueReads { get; private set; }
        public string Subject { get; set; } = "operator";
        public string TenantScope => "tenant-a";
        public string AuditSubject => Subject;
        public string RequestCorrelationId => "request";
        public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken token = default) => ValueTask.FromResult("structure-only");
        public ValueTask<bool> CanInspectStructureAsync(WorkflowExecutionState workflow, CancellationToken token = default) { Reads++; return ValueTask.FromResult(Allowed && workflow.TenantId == TenantScope); }
        public ValueTask<bool> CanInspectSensitiveValuesAsync(WorkflowExecutionState workflow, CancellationToken token = default) { ValueReads++; throw new InvalidOperationException("No value resolution."); }
        public ValueTask<bool> CanResolveSensitiveValuePayloadsAsync(WorkflowExecutionState workflow, CancellationToken token = default) { ValueReads++; throw new InvalidOperationException("No payload resolution."); }
    }

    private sealed class Host(WebApplication app, HttpClient client, Projection history, Authorization authorization) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public Projection History { get; } = history;
        public Authorization Authorization { get; } = authorization;
        public void Authenticate() { Client.DefaultRequestHeaders.Remove("X-History-Test"); Client.DefaultRequestHeaders.Add("X-History-Test", "read"); }
        public static async Task<Host> StartAsync(Action<IServiceCollection>? configure = null, bool includeHistory = true, WorkflowExecutionState? workflow = null)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddElsaEndpoints();
            builder.Services.AddAuthentication("HistoryTests").AddScheme<AuthenticationSchemeOptions, Authentication>("HistoryTests", _ => { });
            builder.Services.AddFoundationIdentityAbstractions(options => options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { "HistoryTests" });
            builder.Services.AddAuthorization();
            var history = new Projection();
            var authorization = new Authorization();
            var workflows = new InMemoryWorkflowExecutionStateStore();
            await workflows.SaveAsync(workflow ?? Workflow());
            builder.Services.AddSingleton<IWorkflowExecutionStateStore>(workflows);
            if (includeHistory) builder.Services.AddSingleton<IRuntimeHistoryStore>(history);
            builder.Services.AddSingleton<IActivityInspectionContextAsync>(authorization);
            builder.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
            configure?.Invoke(builder.Services);
            new WorkflowsRuntimeApiFeature().ConfigureServices(builder.Services);
            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            WorkflowsRuntimeApi.MapWorkflowsRuntimeApi(app);
            await app.StartAsync();
            return new(app, app.GetTestClient(), history, authorization);
        }
        public async ValueTask DisposeAsync() { Client.Dispose(); await app.DisposeAsync(); }
    }

    private sealed class Authentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-History-Test", out var permission)) return Task.FromResult(AuthenticateResult.NoResult());
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "operator"), new Claim(IdentityClaimTypes.Normalized, "v1"),
                 new Claim(IdentityClaimTypes.Permission, permission == "read" ? "workflows/runtime:read" : "workflows/runtime:execute")], Scheme.Name)), Scheme.Name)));
        }
    }
}
