using System.Text.Json;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Actual creation handler and typed claim checkpoint owner over the paired native memory boundary,
/// queried through the real history inspection service with an explicit development authorization adapter.
/// These are component controls, not scheduler-drainer/callback, HTTP, provider or restart acceptance.
/// </summary>
public sealed class RuntimeHistoryBookmarkLifecycleOwnerTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch.AddDays(1);
    private static readonly WorkflowExecutableIdentity Pin = new("artifact", "definition", "version", "1", "hash");

    [Fact]
    public async Task Claim_retains_selected_and_sibling_lifecycles_with_exact_activity_inventory_and_causal_event_links()
    {
        using var f = await Fixture.CreateAsync();
        var selected = await f.CreateBookmarkAsync("selected");
        var sibling = await f.CreateBookmarkAsync("sibling");
        var before = await f.Api.ReadAsync(new("run"));
        Assert.Equal(2, before.Items.Count(item => item.Name == "BookmarkCreated"));
        Assert.DoesNotContain(before.Items, item => item.Name == "ActivitySuspended"); // Creation is not a second suspension.
        Assert.False(before.Coverage.CapturedFromStart); // Fixture seeds native state, not an actual start boundary.
        var request = await f.ClaimRequestAsync(selected, [sibling]);
        await f.Consumption.CommitAsync(request);

        Assert.Null(await f.Bookmarks.FindAsync("run", "selected"));
        Assert.Null(await f.Bookmarks.FindAsync("run", "sibling"));
        Assert.Equal(ActivityExecutionStatus.Running, (await f.Activities.FindAsync("run", "activity"))!.Status);
        var consumed = (await f.Api.FindBookmarkAsync(new("run", "selected"))).Bookmark;
        var retired = (await f.Api.FindBookmarkAsync(new("run", "sibling"))).Bookmark;
        Assert.Equal("Consumed", consumed.Status);
        Assert.Equal("CreationEvent", consumed.PriorState);
        Assert.Equal("Removed", retired.Status);
        Assert.Equal("sibling-resume-won", retired.Reason);
        Assert.Equal("selected", retired.SelectedBookmarkId);
        Assert.Equal("not-active", consumed.Eligibility);
        Assert.NotNull(consumed.CreatedHistoryEventId);
        Assert.NotNull(consumed.EndedHistoryEventId);
        var end = (await f.Api.FindEventAsync(new("run", consumed.EndedHistoryEventId!))).Event;
        Assert.Equal("BookmarkConsumed", end.Name);
        Assert.Equal("selected", end.BookmarkId);
        Assert.Equal("delivery", end.Causation!.DeliveryId);
        Assert.Equal(request.ResumeWorkItem.WorkItemId, end.Causation.SchedulerWorkItemId);
        Assert.Equal(request.ResumeWorkItem.CommandId, end.Causation.CommandId);
        var retirement = (await f.Api.FindEventAsync(new("run", retired.EndedHistoryEventId!))).Event;
        Assert.Equal("BookmarkRetired", retirement.Name);
        var inventory = await f.Api.ListBookmarksAsync(new("run", Limit: 1, ActivityExecutionId: "activity"));
        Assert.Single(inventory.Items);
        Assert.NotNull(inventory.NextContinuationToken);
        var next = await f.Api.ListBookmarksAsync(new("run", Limit: 1, Cursor: inventory.NextContinuationToken, ActivityExecutionId: "activity"));
        Assert.Single(next.Items);
        Assert.NotEqual(inventory.Items[0].Bookmark.Identity.BookmarkId, next.Items[0].Bookmark.Identity.BookmarkId);
        var journal = await f.Api.ReadAsync(new("run", ActivityExecutionId: "activity"));
        Assert.Single(journal.Items, item => item.Name == "BookmarkConsumed");
        Assert.Single(journal.Items, item => item.Name == "BookmarkRetired");
        Assert.Contains(journal.Items, item => item.Name == "ActivityResumeClaimed");
        Assert.DoesNotContain(journal.Items, item => item.Name is "ActivityCompleted" or "ActivityFaulted");
        Assert.DoesNotContain("private", JsonSerializer.Serialize(journal));
        Assert.DoesNotContain("private", JsonSerializer.Serialize(consumed));
    }

    [Fact]
    public async Task Rearm_creates_a_new_lifetime_without_reopening_the_consumed_handle()
    {
        using var f = await Fixture.CreateAsync();
        var original = await f.CreateBookmarkAsync("first");
        var claim = await f.ClaimRequestAsync(original);
        await f.Consumption.CommitAsync(claim);
        var claimed = (await f.Activities.FindAsync("run", "activity"))!;
        var attempt = Assert.Single(claimed.Attempts!);
        var ended = new ActivityAttempt(attempt.AttemptId, "activity", attempt.Ordinal, attempt.Reason, attempt.StartedAt,
            f.Clock.GetUtcNow(), transitionKind: Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Suspend);
        var suspended = claimed with { Status = ActivityExecutionStatus.Suspended, BookmarkIds = [], Attempts = [ended],
            Metadata = new Dictionary<string, string>() };
        var create = f.CreateWork("second");
        // Supply the suspension owner's result, then execute its actual creation continuation directly.
        // The scheduler/outbox drainer and activity callback are outside this component boundary.
        await f.Consumption.CommitAsync(BookmarkConsumptionCheckpointRequest.ForSuspension(
            claim.ResumeWorkItem, claim.ResumePayload, suspended, [create]));
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        await f.Creation.HandleAsync(create);
        var first = (await f.Api.FindBookmarkAsync(new("run", "first"))).Bookmark;
        var second = (await f.Api.FindBookmarkAsync(new("run", "second"))).Bookmark;
        Assert.Equal("Consumed", first.Status);
        Assert.Equal("Active", second.Status);
        Assert.Equal("eligible", second.Eligibility);
        Assert.Null(second.EndedHistoryEventId);
        Assert.NotEqual(first.CreatedHistoryEventId, second.CreatedHistoryEventId);
        Assert.True(first.LastSequence < second.FirstRecordedSequence);
        Assert.Null(await f.Bookmarks.FindAsync("run", "first"));
        Assert.NotNull(await f.Bookmarks.FindAsync("run", "second"));
        var journal = await f.Api.ReadAsync(new("run", ActivityExecutionId: "activity"));
        Assert.Equal(2, journal.Items.Count(item => item.Name == "BookmarkCreated"));
        Assert.Single(journal.Items, item => item.Name == "BookmarkConsumed");
        Assert.Single(journal.Items, item => item.Name == "ActivitySuspended");
    }

    [Fact]
    public async Task Passive_expiry_changes_observed_eligibility_without_appending_a_transition_or_deleting_the_wait()
    {
        using var f = await Fixture.CreateAsync();
        await f.CreateBookmarkAsync("expires", At.AddMinutes(1));
        var before = await f.Api.FindBookmarkAsync(new("run", "expires"));
        var head = await f.History.FindHeadAsync("run");
        Assert.Equal("eligible", before.Bookmark.Eligibility);
        f.Clock.Advance(TimeSpan.FromMinutes(2));
        var after = await f.Api.FindBookmarkAsync(new("run", "expires"));
        Assert.Equal("Active", after.Bookmark.Status);
        Assert.Equal("expired", after.Bookmark.Eligibility);
        Assert.True(before.Bookmark.EligibilityObservedAt < after.Bookmark.EligibilityObservedAt);
        Assert.Null(after.Bookmark.EndedHistoryEventId);
        Assert.Equal(before.Bookmark.LastHistoryEventId, after.Bookmark.LastHistoryEventId);
        Assert.Equal(head, await f.History.FindHeadAsync("run"));
        Assert.NotNull(await f.Bookmarks.FindAsync("run", "expires"));
        Assert.Single(f.Commits.ListCommits());
    }

    [Fact]
    public async Task Native_wait_first_observed_at_claim_retains_creation_metadata_without_fabricating_a_creation_event()
    {
        using var f = await Fixture.CreateAsync();
        var bookmark = new BookmarkState("before-capture", "run", "activity", "node", "resume", "task", "hash",
            JsonSerializer.SerializeToElement("private payload"), new Dictionary<string, string>(), At.AddMinutes(-1), null);
        await f.Bookmarks.SaveAsync(bookmark);
        await f.Activities.SaveAsync(Activity() with { BookmarkIds = [bookmark.BookmarkId] });
        await f.Consumption.CommitAsync(await f.ClaimRequestAsync(bookmark));
        var item = (await f.Api.FindBookmarkAsync(new("run", bookmark.BookmarkId))).Bookmark;
        Assert.Equal("Consumed", item.Status);
        Assert.Null(item.CreatedHistoryEventId);
        Assert.Equal("CreationMetadata", item.PriorState);
        Assert.Equal(bookmark.CreatedAt, item.Bookmark.CreatedAt);
        var page = await f.Api.ReadAsync(new("run", BookmarkId: bookmark.BookmarkId));
        Assert.Equal("BookmarkConsumed", Assert.Single(page.Items).Name);
        Assert.False(page.Coverage.CapturedFromStart);
    }

    [Fact]
    public async Task Stale_terminal_cleanup_is_removal_and_does_not_claim_a_resume_or_activity_completion()
    {
        using var f = await Fixture.CreateAsync();
        var bookmark = await f.CreateBookmarkAsync("stale");
        var input = await f.ClaimRequestAsync(bookmark);
        var completed = Activity() with { Status = ActivityExecutionStatus.Completed, CompletedAt = At, BookmarkIds = [] };
        // Seed the already-terminal native owner; this cleanup does not perform or record completion.
        await f.Activities.SaveAsync(completed with { BookmarkIds = [bookmark.BookmarkId] });
        var completePayload = new RuntimeCompleteActivityCommandPayload(Pin, "node", "activity", null, null, [],
            RuntimeCompleteActivityCommandPayload.ActivityInvocationCompletedReason);
        var continuation = new RuntimeSchedulerWorkItem("completion", "run", "complete-command", WorkflowExecutionCommandKind.CompleteActivity,
            "complete-envelope", "complete-key", At, At, payload: JsonSerializer.SerializeToElement(completePayload));
        await f.Consumption.CommitAsync(BookmarkConsumptionCheckpointRequest.ForStaleBookmarkConsumption(
            input.ResumeWorkItem, input.ResumePayload, bookmark, completed, continuation));
        Assert.Null(await f.Bookmarks.FindAsync("run", bookmark.BookmarkId));
        var item = (await f.Api.FindBookmarkAsync(new("run", bookmark.BookmarkId))).Bookmark;
        Assert.Equal("Removed", item.Status);
        Assert.Equal("stale-terminal-cleanup", item.Reason);
        var end = (await f.Api.FindEventAsync(new("run", item.EndedHistoryEventId!))).Event;
        Assert.Equal("BookmarkStaleRemoved", end.Name);
        var page = await f.Api.ReadAsync(new("run"));
        Assert.DoesNotContain(page.Items, item => item.Name is "BookmarkConsumed" or "ActivityResumeClaimed" or "ActivityCompleted");
    }

    [Fact]
    public async Task Replaced_native_generation_refuses_the_captured_claim_without_a_phantom_consumption()
    {
        using var f = await Fixture.CreateAsync();
        var bookmark = await f.CreateBookmarkAsync("selected");
        var claim = await f.ClaimRequestAsync(bookmark);
        var before = await f.History.ReadPageAsync(new("run"));
        await f.Bookmarks.DeleteAsync("run", bookmark.BookmarkId);
        await f.Bookmarks.SaveAsync(bookmark); // Same logical content, a different actual native generation.
        await Assert.ThrowsAsync<BookmarkResumeSupersededException>(async () => await f.Consumption.CommitAsync(claim));
        Assert.NotNull(await f.Bookmarks.FindAsync("run", bookmark.BookmarkId));
        Assert.Equal(ActivityExecutionStatus.Suspended, (await f.Activities.FindAsync("run", "activity"))!.Status);
        var after = await f.History.ReadPageAsync(new("run"));
        Assert.Equal(before.Head, after.Head);
        Assert.Equal(before.Items.Select(item => item.HistoryEventId), after.Items.Select(item => item.HistoryEventId));
        Assert.DoesNotContain(after.Items, item => item.Name == "BookmarkConsumed");
        Assert.Single(f.Commits.ListCommits());
    }

    [Fact]
    public async Task Delayed_typed_sibling_creation_after_claim_does_not_resurrect_a_retired_registration()
    {
        using var f = await Fixture.CreateAsync();
        var state = Activity() with
        {
            PrivateState = new("activity", 1, ValueEnvelope.Inline(new("test/state"), JsonSerializer.SerializeToElement("private"),
                ValueProtectionPolicy.InstanceInline), "initial-attempt", At),
            TriggerRegistrations = [Registration("first"), Registration("delayed")]
        };
        await f.Activities.SaveAsync(state);
        var first = await f.CreateBookmarkAsync("first");
        var delayed = f.CreateWork("delayed");
        await f.Consumption.CommitAsync(await f.ClaimRequestAsync(first));
        var before = await f.History.FindHeadAsync("run");
        await f.Creation.HandleAsync(delayed);
        Assert.Null(await f.Bookmarks.FindAsync("run", "delayed"));
        Assert.Null(await f.History.FindBookmarkAsync("run", "delayed"));
        Assert.Equal(before, await f.History.FindHeadAsync("run"));
        Assert.Equal(ActivityExecutionStatus.Running, (await f.Activities.FindAsync("run", "activity"))!.Status);
    }

    private static ActivityTriggerRegistration Registration(string id) => new(id, "activity", "resume", new("test/task"),
        "task", "hash", ActivityTriggerDeduplicationPolicy.Once);

    private static ActivityExecutionState Activity() => new(new("activity", "run", "node", "authored", "test/activity", "1"),
        ActivityExecutionStatus.Suspended, null, 1, At, At, null, null, null, null, null,
        ActivitySchedulingProvenance.From("run", null, null, null, null, null, "activity", "test"), 0, [], [], 0, 0,
        new Dictionary<string, string>(), ExecutionScopeId: "activity");

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;
        private Fixture(ServiceProvider provider, IServiceScope scope, FakeTimeProvider clock)
        {
            _provider = provider; _scope = scope; Clock = clock;
            var services = scope.ServiceProvider;
            Bookmarks = services.GetRequiredService<IBookmarkStateStore>();
            Activities = services.GetRequiredService<IActivityExecutionStateStore>();
            Workflows = services.GetRequiredService<IWorkflowExecutionStateStore>();
            History = services.GetRequiredService<IRuntimeHistoryStore>();
            Commits = services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
            var committer = services.GetRequiredService<RuntimeCheckpointCommitter>();
            var inspections = services.GetRequiredService<IRuntimeActivityExecutionInspectionAccumulator>();
            Creation = new(services.GetRequiredService<IWorkflowExecutableStore>(), Activities, committer, inspections, clock);
            Consumption = new(committer, inspections, clock);
            Api = new(Workflows, new AllowAllActivityExecutionInspectionAuthorizationContext(), clock, History, Bookmarks, Activities);
        }
        public FakeTimeProvider Clock { get; }
        public IBookmarkStateStore Bookmarks { get; }
        public IActivityExecutionStateStore Activities { get; }
        public IWorkflowExecutionStateStore Workflows { get; }
        public IRuntimeHistoryStore History { get; }
        public InMemoryRuntimeCheckpointCommitStore Commits { get; }
        public WorkflowCreateBookmarkSchedulerWorkHandler Creation { get; }
        public BookmarkConsumptionCheckpointService Consumption { get; }
        public RuntimeHistoryInspectionService Api { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var services = new ServiceCollection();
            var clock = new FakeTimeProvider(At);
            services.AddLogging();
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<IPersistenceAccessContextAccessor>(new RuntimeHistoryTestAccess());
            services.AddSingleton<IWorkflowExecutableRootWriteLeaseManager>(PassThroughWorkflowExecutableRootWriteLeaseManager.Instance);
            services.AddWorkflowRuntime();
            services.Configure<RuntimeRecoveryContinuationOptions>(options => options.SigningKey = "bookmark-history-tests-signing-key-at-least-32-bytes");
            RuntimeHistoryRegistration.AddServices(services);
            var provider = services.BuildServiceProvider(validateScopes: true);
            var scope = provider.CreateScope();
            var f = new Fixture(provider, scope, clock);
            var policy = RuntimeHistoryRegistration.ResolvePolicy(scope.ServiceProvider, "run")!;
            await f.Workflows.SaveAsync(new("run", Pin, WorkflowExecutionStatus.Running, null, At, At, At, null, null, null, null,
                new Dictionary<string, string>()) { HistoryCapturePolicy = policy, HistoryCaptureEnabledAtAdmission = true,
                RunKind = WorkflowRunKind.PublishedRun, Partition = new("partition") });
            await f.Activities.SaveAsync(Activity());
            await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(new(Pin,
                new("node", "authored", "test/activity", "1", "test", JsonSerializer.SerializeToElement(new { type = "test" }),
                    new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
                new Dictionary<string, WorkflowExecutableResumeTarget> { ["resume"] = new("resume", "node", "handler", new Dictionary<string, string>()) },
                At, new Dictionary<string, string>(), incidentStrategy: IncidentStrategyBuiltIns.FaultReference));
            return f;
        }

        public RuntimeSchedulerWorkItem CreateWork(string id, DateTimeOffset? expiresAt = null)
        {
            var payload = new RuntimeCreateBookmarkCommandPayload(Pin, id, "activity", "node", "resume", "task", "hash",
                JsonSerializer.SerializeToElement("private payload"), expiresAt, RuntimeCreateBookmarkCommandPayload.ActivitySuspendedReason,
                new Dictionary<string, string> { ["private"] = "private metadata" });
            return Work($"create-{id}", WorkflowExecutionCommandKind.CreateBookmark, JsonSerializer.SerializeToElement(payload));
        }
        public async Task<BookmarkState> CreateBookmarkAsync(string id, DateTimeOffset? expiresAt = null)
        {
            await Creation.HandleAsync(CreateWork(id, expiresAt));
            return (await Bookmarks.FindAsync("run", id))!;
        }
        public async Task<BookmarkConsumptionCheckpointRequest> ClaimRequestAsync(BookmarkState selected, IReadOnlyCollection<BookmarkState>? siblings = null)
        {
            var observation = (await Bookmarks.ObserveAsync("run", selected.BookmarkId))!;
            var workflow = (await Workflows.FindAsync("run"))!;
            var payload = new RuntimeResumeBookmarkCommandPayload(Pin, selected.BookmarkId, "activity", "node", "resume", "task", "hash",
                JsonSerializer.SerializeToElement("private stimulus"), RuntimeResumeBookmarkCommandPayload.StimulusMatchedReason)
                { ExpectedBookmarkResume = StimulusResumeRecipient.From(observation, workflow, new(PersistenceScope.DefaultValue)),
                  HistoryCausation = new(WorkflowExecutionId: "run", DeliveryId: "delivery") };
            var work = Work($"resume-{selected.BookmarkId}", WorkflowExecutionCommandKind.ResumeBookmark, JsonSerializer.SerializeToElement(payload));
            const string attempt = "resume-attempt";
            var prior = (await Activities.FindAsync("run", "activity"))!;
            var claimed = prior with { Status = ActivityExecutionStatus.Running, SubStatus = null, BookmarkIds = [], TriggerRegistrations = [],
                Attempts = [new(attempt, "activity", 1, ActivityAttemptReason.Resume, Clock.GetUtcNow())],
                Metadata = new Dictionary<string, string> { [RuntimeMetadataKeys.ActivityAttemptActivationClaim] = attempt,
                    [RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId] = work.WorkItemId } };
            return BookmarkConsumptionCheckpointRequest.ForInitialTriggerClaim(work, payload, selected, claimed, attempt, siblings);
        }
        private RuntimeSchedulerWorkItem Work(string id, WorkflowExecutionCommandKind kind, JsonElement payload) => new(id, "run", $"command-{id}",
            kind, $"envelope-{id}", $"key-{id}", Clock.GetUtcNow(), Clock.GetUtcNow(), payload: payload);
        public void Dispose() { _scope.Dispose(); _provider.Dispose(); }
    }
}
