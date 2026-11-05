using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed partial class RuntimeMinimalApiBehaviorTests
{
    private const string HistoryPath = "/runtime/workflows/instances/wf-1/history";

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
        Assert.Equal(1L, page.Coverage.SettledSequence);
        Assert.Equal((1, "ae-1", RuntimeHistoryEventCategory.ActivityLifecycle),
            (source.Query!.Limit, source.Query.ActivityExecutionId, source.Query.Category));
        Assert.False(string.IsNullOrWhiteSpace(source.Query.AccessBinding));
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
        await native.DeleteAsync("wf-1", "bookmark");
        var missing = await host.Client.GetFromJsonAsync<RuntimeHistoryBookmarkDetailsView>(HistoryPath + "/bookmarks/bookmark");
        Assert.Equal(("Active", "unavailable"), (missing!.Bookmark.Status, missing.Bookmark.Eligibility));
        var page = await host.Client.GetFromJsonAsync<RuntimeHistoryBookmarkPageView>(HistoryPath + "/bookmarks");
        Assert.Null(page!.TotalCount);
        Assert.Null(page.ActiveCount);
        Assert.Equal(1L, Assert.Single(page.Items).LastSequence);
    }

    private static async Task<BehaviorHost> HistoryHostAsync(HistorySource? source, BrowserAuthorization? authorization = null,
        bool legacy = false, IBookmarkStateStore? nativeBookmarks = null)
    {
        var workflows = new InMemoryWorkflowExecutionStateStore();
        await workflows.SaveAsync(new WorkflowExecutionState("wf-1", new("artifact", "definition", "version", "1", "hash"),
            WorkflowExecutionStatus.Suspended, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch, null, null, null, "tenant-a", new Dictionary<string, string>())
            { HistoryCapturePolicy = legacy ? null : HistorySource.Policy });
        return await StartAsync(_ => DispatchView(WorkflowExecutionCommandDispatchStatus.Accepted), services =>
        {
            services.AddSingleton<IWorkflowExecutionStateStore>(workflows);
            services.AddSingleton<IActivityInspectionContextAsync>(authorization ?? new BrowserAuthorization());
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMinutes(2)));
            if (source is not null) services.AddSingleton<IRuntimeHistoryStore>(source);
            if (nativeBookmarks is not null) services.AddSingleton(nativeBookmarks);
        });
    }

    private sealed class HistorySource : IRuntimeHistoryStore
    {
        internal static readonly RuntimeHistoryCapturePolicy Policy = new("test", 1, true, ["FutureActivityKind"]);
        private static readonly RuntimeHistoryHead Head = new("wf-1", 1, 1, "physical", DateTimeOffset.UnixEpoch,
            Policy, true, false, 0);
        internal int Calls { get; private set; }
        internal RuntimeHistoryQuery? Query { get; private set; }
        internal bool EmptyWindow { get; init; }
        internal bool Uncaptured { get; init; }
        internal bool Invalid { get; init; }
        public ValueTask<RuntimeHistoryHead?> FindHeadAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        { Calls++; return ValueTask.FromResult<RuntimeHistoryHead?>(Uncaptured ? null : Head); }
        public ValueTask<RuntimeHistoryPage> ReadPageAsync(RuntimeHistoryQuery query, CancellationToken cancellationToken = default)
        {
            Calls++; Query = query;
            if (Invalid) throw new RuntimeHistoryIntegrityException(query.WorkflowExecutionId, "private-sentinel");
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
        private static RuntimeHistoryEvent Event() => new("future-event", "wf-1", 1, RuntimeHistoryEventCategory.ActivityLifecycle,
            "FutureActivityKind", DateTimeOffset.UnixEpoch, "ae-1", "node", "logical", payload: JsonSerializer.SerializeToElement("private-sentinel"),
            metadata: new Dictionary<string, string> { ["private"] = "private-sentinel", ["history.status"] = "Suspended" })
            { SchemaVersion = 2, Authority = RuntimeHistoryAuthority.CommittedTransition, PhysicalCommitId = "physical", CheckpointOrdinal = 3 };
        private static RuntimeHistoryBookmark Bookmark() => new(new(new("bookmark", "wf-1", "ae-1", "continue"), "task", "hash",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1)), "node", RuntimeHistoryBookmarkStatus.Active,
            "future-event", null, "future-event", 1, 1, null, null, null, RuntimeHistoryPriorState.CreationEvent);
    }
}
