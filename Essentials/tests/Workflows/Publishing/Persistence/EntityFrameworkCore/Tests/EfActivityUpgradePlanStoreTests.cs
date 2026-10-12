using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests.ActivityUpgradeFixtures;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

public sealed class EfActivityUpgradePlanStoreTests : IAsyncLifetime
{
    private ActivityUpgradeDatabase database = null!;
    private readonly IPayloadSerializer payloads = Serializer();

    public async Task InitializeAsync()
    {
        database = await ActivityUpgradeDatabase.CreateAsync();
        await ActivityUpgradeSeed.BaseGraphAsync(database.Contexts, payloads);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    // -----------------------------------------------------------------------------------------
    // Atomic apply
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Apply_commits_both_catalogs_the_plan_result_and_the_projection_as_one_act()
    {
        var plan = ActivityUpgradeSeed.TwoStepPlan(SeededWorkflowRevision);
        var receipt = ActivityUpgradeSeed.Receipt(plan);
        await ActivityUpgradeSeed.PersistAsync(database.Contexts, plan, receipt);

        var result = await ApplyAsync(plan, receipt, Now.AddMinutes(1));

        Assert.Equal(ActivityUpgradePlanStatus.Applied, result.Status);
        Assert.Equal(["ActivityDraft", "WorkflowDraft"], result.Drafts.Select(x => x.Kind));
        Assert.Equal(3, result.Drafts[0].Revision);

        // Every assertion below reads through connections opened after the commit: the durable state, not
        // a change tracker that happens to agree with it.
        Assert.Equal(3, (await ReadActivityDraftAsync()).Revision);
        Assert.Equal(NewVersionId, (await ReadWorkflowStateAsync()).RootActivity!.ActivityVersionId);
        Assert.Equal(result.Drafts[1].Revision, EfWorkflowDraftRevision.Of((await ReadWorkflowDraftAsync()).LastModifiedAt));

        var storedPlan = await ReadPlanAsync(plan.PlanId);
        Assert.Equal(ActivityUpgradePlanStatus.Applied, storedPlan.Status);
        Assert.Equal(result.Drafts, storedPlan.AppliedDrafts);
        Assert.Equal(result.AppliedAt, storedPlan.AppliedAt);
        var storedReceipt = await ReadReceiptAsync(receipt.ReceiptId);
        Assert.Equal(ActivityUpgradeApplyReceiptStatus.Applied, storedReceipt.Status);
        Assert.Equal(result.AppliedAt, storedReceipt.Result!.AppliedAt);
        Assert.Equal(result.Drafts, storedReceipt.Result.Drafts);
        Assert.Equal(2, storedReceipt.Revision);

        var projection = await ReadProjectionAsync();
        Assert.Equal(2, projection.Sequence);
        Assert.All(projection.Items, item => Assert.Equal(NewVersionId, item.Dependency.VersionId));
        Assert.Equal(
            [3L, result.Drafts[1].Revision],
            projection.Items.OrderBy(x => x.Owner.Kind, StringComparer.Ordinal).Select(x => x.Owner.Revision));
    }

    [Fact]
    public async Task A_stale_workflow_draft_revision_rejects_the_apply_and_leaves_the_activity_edit_uncommitted()
    {
        // The activity step is ordered first, so by the time the workflow compare-and-swap finds drift the
        // activity rows have already been written inside the transaction.
        var plan = ActivityUpgradeSeed.TwoStepPlan(SeededWorkflowRevision, workflowStepRevision: SeededWorkflowRevision + 99);
        var receipt = ActivityUpgradeSeed.Receipt(plan);
        await ActivityUpgradeSeed.PersistAsync(database.Contexts, plan, receipt);

        var failure = await Assert.ThrowsAsync<ActivityUpgradeApplyException>(() => ApplyAsync(plan, receipt, Now.AddMinutes(1)));

        Assert.Equal("activity.upgrade.stale-plan", failure.ErrorCode);
        await AssertNothingWasAppliedAsync(plan.PlanId, receipt.ReceiptId);
    }

    [Fact]
    public async Task An_injected_storage_failure_after_the_activities_write_rolls_both_lanes_back()
    {
        var plan = ActivityUpgradeSeed.TwoStepPlan(SeededWorkflowRevision);
        var receipt = ActivityUpgradeSeed.Receipt(plan);
        await ActivityUpgradeSeed.PersistAsync(database.Contexts, plan, receipt);
        // The shared transaction constructs fresh contexts from these options, so the interceptor travels
        // with them. It refuses the workflow-draft update after the activity rows are already written.
        var crash = new FailCommandOnceInterceptor("elsa_workflow_definition_drafts");
        await using var scope = Scope(Now.AddMinutes(1), workflowsInterceptors: [crash]);

        await Assert.ThrowsAsync<InjectedCrashException>(() =>
            scope.Store.ApplyAsync(plan, plan.Steps, receipt, Now.AddMinutes(1)).AsTask());

        Assert.True(crash.Fired);
        await AssertNothingWasAppliedAsync(plan.PlanId, receipt.ReceiptId);
    }

    [Fact]
    public async Task A_workflow_draft_that_moves_after_the_in_transaction_read_loses_the_compare_and_swap()
    {
        // Everything the plan asserts is current when the apply starts, so the only thing that can refuse
        // the write is the conditional update itself. The row is moved on the transaction's own connection
        // immediately before that update, which is the state the update's predicate has to detect.
        var plan = ActivityUpgradeSeed.TwoStepPlan(SeededWorkflowRevision);
        var receipt = ActivityUpgradeSeed.Receipt(plan);
        await ActivityUpgradeSeed.PersistAsync(database.Contexts, plan, receipt);
        var moved = new MutateBeforeFirstWriteInterceptor(
            "elsa_workflow_definition_drafts",
            $"UPDATE \"elsa_workflow_definition_drafts\" SET \"LastModifiedAt\" = '2027-01-01 00:00:00.0000000+00:00' WHERE \"Id\" = '{WorkflowDraftId}'");
        await using var scope = Scope(Now.AddMinutes(1), workflowsInterceptors: [moved]);

        var failure = await Assert.ThrowsAsync<ActivityUpgradeApplyException>(() =>
            scope.Store.ApplyAsync(plan, plan.Steps, receipt, Now.AddMinutes(1)).AsTask());

        Assert.True(moved.Fired);
        Assert.Equal("activity.upgrade.stale-plan", failure.ErrorCode);
        await AssertNothingWasAppliedAsync(plan.PlanId, receipt.ReceiptId);
    }

    [Fact]
    public async Task A_commit_whose_outcome_is_unknown_is_not_reported_as_a_stale_plan()
    {
        // Calling this stale would tell the caller nothing was written, which a failed commit request does
        // not establish. The durable Preparing receipt and its lease stay the only authority.
        var plan = ActivityUpgradeSeed.TwoStepPlan(SeededWorkflowRevision);
        var receipt = ActivityUpgradeSeed.Receipt(plan);
        await ActivityUpgradeSeed.PersistAsync(database.Contexts, plan, receipt);
        await using var scope = new ActivityUpgradeScope(
            database.Activities(new FailCommitInterceptor()),
            database.Workflows(),
            TestAccess.Scoped(Tenant),
            new SequentialIdentities(),
            new FrozenTimeProvider(Now.AddMinutes(1)));

        var failure = await Assert.ThrowsAsync<EfCommitOutcomeUnknownException>(() =>
            scope.Store.ApplyAsync(plan, plan.Steps, receipt, Now.AddMinutes(1)).AsTask());

        Assert.IsType<InjectedCrashException>(failure.InnerException);
        await AssertNothingWasAppliedAsync(plan.PlanId, receipt.ReceiptId);
    }

    [Fact]
    public async Task A_second_apply_of_an_applied_receipt_is_refused_and_writes_nothing()
    {
        var plan = ActivityUpgradeSeed.TwoStepPlan(SeededWorkflowRevision);
        var receipt = ActivityUpgradeSeed.Receipt(plan);
        await ActivityUpgradeSeed.PersistAsync(database.Contexts, plan, receipt);
        await ApplyAsync(plan, receipt, Now.AddMinutes(1));

        var failure = await Assert.ThrowsAsync<ActivityUpgradeApplyException>(() => ApplyAsync(plan, receipt, Now.AddMinutes(2)));

        Assert.Equal("activity.upgrade.stale-plan", failure.ErrorCode);
        Assert.Equal(3, (await ReadActivityDraftAsync()).Revision);
        Assert.Equal(2, (await ReadProjectionAsync()).Sequence);
    }

    [Fact]
    public async Task The_staged_applier_replays_the_recorded_result_for_a_repeated_idempotency_key()
    {
        var plan = ActivityUpgradeSeed.TwoStepPlan(SeededWorkflowRevision);
        await ActivityUpgradeSeed.PersistAsync(database.Contexts, plan);
        await using var scope = Scope(Now.AddMinutes(1));
        var applier = scope.Applier(new FrozenTimeProvider(Now.AddMinutes(1)));

        var first = await applier.ApplyAsync(new(plan.PlanId, "stage", "idempotency-1"));
        var replay = await applier.ApplyAsync(new(plan.PlanId, "stage", "idempotency-1"));

        Assert.Equal(ActivityUpgradePlanStatus.Applied, first.Status);
        // The replay is the recorded receipt result read back, so it compares by value, not by reference.
        Assert.Equal(first.PlanId, replay.PlanId);
        Assert.Equal(first.Status, replay.Status);
        Assert.Equal(first.AppliedAt, replay.AppliedAt);
        Assert.Equal(first.ReceiptId, replay.ReceiptId);
        Assert.Equal(first.StageId, replay.StageId);
        Assert.Equal(first.Drafts, replay.Drafts);
        Assert.Equal(3, (await ReadActivityDraftAsync()).Revision);
        // A replay that re-applied would advance the projection a second time.
        Assert.Equal(2, (await ReadProjectionAsync()).Sequence);
    }

    // -----------------------------------------------------------------------------------------
    // The workflow-draft revision
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task The_workflow_draft_revision_advances_even_when_the_clock_does_not()
    {
        var plan = ActivityUpgradeSeed.TwoStepPlan(SeededWorkflowRevision);
        var receipt = ActivityUpgradeSeed.Receipt(plan);
        await ActivityUpgradeSeed.PersistAsync(database.Contexts, plan, receipt);

        // Applying at exactly the instant the row was last written at is the case a wall-clock revision
        // would silently accept twice: on Windows DateTimeOffset.UtcNow only moves every ~15ms.
        var result = await ApplyAsync(plan, receipt, Now);

        var stored = await ReadWorkflowDraftAsync();
        Assert.Equal(SeededWorkflowRevision + 1, EfWorkflowDraftRevision.Of(stored.LastModifiedAt));
        Assert.Equal(SeededWorkflowRevision + 1, result.Drafts.Single(x => x.Kind == "WorkflowDraft").Revision);
        // The stamped instant survives a provider round trip unchanged, so the reported revision is the
        // one another process observes.
        Assert.Equal(EfWorkflowDraftRevision.ToInstant(SeededWorkflowRevision + 1), stored.LastModifiedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1)]
    public void The_next_workflow_draft_revision_is_microsecond_aligned_and_strictly_increasing(int clockOffsetMicroseconds)
    {
        var observed = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero).AddTicks(7);
        var now = observed.AddTicks(clockOffsetMicroseconds * 10);

        var next = EfWorkflowDraftRevision.Next(observed, now);

        Assert.True(EfWorkflowDraftRevision.Of(next) > EfWorkflowDraftRevision.Of(observed));
        Assert.Equal(0, next.UtcTicks % 10);
        Assert.Equal(next, EfWorkflowDraftRevision.ToInstant(EfWorkflowDraftRevision.Of(next)));
    }

    // -----------------------------------------------------------------------------------------
    // Tenant isolation and discovery
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task An_apply_in_another_tenant_scope_cannot_see_the_plan_and_writes_nothing()
    {
        var plan = ActivityUpgradeSeed.TwoStepPlan(SeededWorkflowRevision);
        var receipt = ActivityUpgradeSeed.Receipt(plan);
        await ActivityUpgradeSeed.PersistAsync(database.Contexts, plan, receipt);
        await using var scope = Scope(Now.AddMinutes(1), tenantId: "other-tenant");

        var failure = await Assert.ThrowsAsync<ActivityUpgradeApplyException>(() =>
            scope.Store.ApplyAsync(plan, plan.Steps, receipt, Now.AddMinutes(1)).AsTask());

        Assert.Equal("activity.upgrade.stale-plan", failure.ErrorCode);
        await AssertNothingWasAppliedAsync(plan.PlanId, receipt.ReceiptId);
    }

    [Fact]
    public async Task Discovery_exhausts_a_multi_page_projection_and_orders_its_diagnostics()
    {
        const int occurrences = EfActivityUpgradeDiscoveryPageSize + 37;
        await WidenProjectionAsync(occurrences);
        await using var scope = Scope(Now);

        var discovery = await scope.Store.DiscoverAsync(new(
            [new(OldVersionId, NewVersionId)],
            [new("ActivityDraft", ActivityDraftId), new("WorkflowDraft", "absent-draft")],
            false,
            false,
            Tenant,
            "access"));
        var repeated = await scope.Store.DiscoverAsync(new(
            [new(OldVersionId, NewVersionId)],
            [new("ActivityDraft", ActivityDraftId), new("WorkflowDraft", "absent-draft")],
            false,
            false,
            Tenant,
            "access"));

        var owner = Assert.Single(discovery.Owners);
        Assert.Equal(occurrences, owner.DirectReplacements.Count);
        Assert.Equal(occurrences, owner.DirectReplacements.Select(x => x.OccurrenceId).Distinct(StringComparer.Ordinal).Count());
        var diagnostic = Assert.Single(discovery.Diagnostics);
        Assert.Equal("activity.upgrade.root-not-affected", diagnostic.Code);
        Assert.Equal(
            discovery.Diagnostics.Select(x => (x.Code, x.Subject.Id)),
            discovery.Diagnostics.OrderBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => x.Subject.Id, StringComparer.Ordinal).Select(x => (x.Code, x.Subject.Id)));
        Assert.Equal(
            discovery.Owners.Select(x => x.Owner.DraftId),
            repeated.Owners.Select(x => x.Owner.DraftId));
    }

    // -----------------------------------------------------------------------------------------
    // The acceptance journey
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_planner_generated_plan_upgrades_the_dependent_workflow_draft()
    {
        await using var scope = Scope(Now);
        var plan = await Planner(scope).PlanAsync(new(
            [new(OldVersionId, NewVersionId)],
            [new("ActivityDraft", ActivityDraftId), new("WorkflowDraft", WorkflowDraftId)],
            false,
            false,
            Tenant,
            "access"));
        Assert.Equal(ActivityUpgradePlanStatus.Ready, plan.Status);
        Assert.Equal(2, plan.Stages.Count);
        var applier = scope.Applier(new FrozenTimeProvider(Now.AddMinutes(1)));

        foreach (var stage in plan.Stages.OrderBy(x => x.Order))
            await applier.ApplyAsync(new(plan.PlanId, stage.StageId, $"stage-{stage.StageId}"));

        Assert.Equal(NewVersionId, (await ReadWorkflowStateAsync()).RootActivity!.ActivityVersionId);
        Assert.Equal(3, (await ReadActivityDraftAsync()).Revision);
        var completed = await ReadPlanAsync(plan.PlanId);
        Assert.Equal(ActivityUpgradePlanStatus.Applied, completed.Status);
        Assert.All(completed.Stages, stage => Assert.Equal(ActivityUpgradeStageStatus.Applied, stage.Status));
        Assert.All((await ReadProjectionAsync()).Items, item => Assert.Equal(NewVersionId, item.Dependency.VersionId));
    }

    [Fact]
    public async Task A_published_dependent_is_staged_behind_its_publication_handoff()
    {
        // The shape the reusable-activity upgrade journey asserts: the nearer published dependent is
        // upgraded into a new draft first, and the outer one waits for that draft to be published.
        await ActivityUpgradeSeed.PublishedChainAsync(database.Contexts);
        await using var scope = Scope(Now);
        var plan = await Planner(scope).PlanAsync(new(
            [new(OldVersionId, NewVersionId)],
            [new("ActivityVersion", "a-v1")],
            true,
            true,
            Tenant,
            "access"));

        Assert.Equal(ActivityUpgradePlanStatus.Ready, plan.Status);
        Assert.Equal(2, plan.Steps.Count);
        Assert.Equal(
            [ActivityUpgradeStageStatus.Ready, ActivityUpgradeStageStatus.AwaitingPublication],
            plan.Stages.OrderBy(x => x.Order).Select(x => x.Status));
        var ready = plan.Stages.Single(x => x.Status == ActivityUpgradeStageStatus.Ready);

        var applied = await scope.Applier(new FrozenTimeProvider(Now.AddMinutes(1)))
            .ApplyAsync(new(plan.PlanId, ready.StageId, "first-stage"));

        Assert.Equal(ActivityUpgradePlanStatus.AwaitingPublication, applied.Status);
        var draft = Assert.Single(applied.Drafts);
        var handoff = Assert.Single(applied.AwaitingPublications);
        Assert.Equal(draft.DraftId, handoff.DraftId);
        Assert.Equal("b-v1", handoff.SourceVersionId);
        Assert.True(draft.Created);
        var created = await ReadActivityDraftAsync(draft.DraftId);
        Assert.Equal("b-definition", created.DefinitionId);
        Assert.Equal(1, created.Revision);
        var receipt = await ReadReceiptAsync(applied.ReceiptId!);
        Assert.Equal(ActivityUpgradeApplyReceiptStatus.Applied, receipt.Status);
        Assert.Equal(applied.Status, receipt.Result!.Status);
        Assert.Equal(applied.Drafts, receipt.Result.Drafts);
        Assert.Equal(
            applied.AwaitingPublications.Select(x => (x.DraftId, x.SourceVersionId, Stages: string.Join(",", x.RequiredByStageIds))),
            receipt.Result.AwaitingPublications.Select(x => (x.DraftId, x.SourceVersionId, Stages: string.Join(",", x.RequiredByStageIds))));
        // The published version's derived facts moved to the new draft, and its pin now names the target.
        var projection = await ReadProjectionAsync();
        Assert.DoesNotContain(projection.Items, item => item.Owner.VersionId == "b-v1");
        Assert.Contains(projection.Items, item => item.Owner.DraftId == draft.DraftId && item.Dependency.VersionId == NewVersionId);
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    /// <summary>The page size the store asks the dependency projection for; also its declared maximum.</summary>
    private const int EfActivityUpgradeDiscoveryPageSize = 500;

    private static long SeededWorkflowRevision => EfWorkflowDraftRevision.Of(Now);

    private ActivityUpgradeScope Scope(
        DateTimeOffset now,
        string tenantId = Tenant,
        IInterceptor[]? workflowsInterceptors = null) =>
        new(
            database.Activities(),
            database.Workflows(workflowsInterceptors ?? []),
            TestAccess.Scoped(tenantId),
            new SequentialIdentities(),
            new FrozenTimeProvider(now));

    private async Task<ActivityUpgradeApplyResult> ApplyAsync(
        ActivityUpgradePlan plan,
        ActivityUpgradeApplyReceipt receipt,
        DateTimeOffset appliedAt)
    {
        await using var scope = Scope(appliedAt);
        return await scope.Store.ApplyAsync(plan, plan.Steps, receipt, appliedAt);
    }

    private ActivityUpgradePlanner Planner(ActivityUpgradeScope scope) => new(
        scope.Store,
        scope.Design,
        scope.Design,
        scope.Store,
        new CompatibleDiffBuilder(),
        new AllowAllAuthorization(),
        new SequentialIdentities("plan"),
        new FrozenClock(Now));

    private async Task AssertNothingWasAppliedAsync(string planId, string receiptId)
    {
        Assert.Equal(2, (await ReadActivityDraftAsync()).Revision);
        Assert.Equal(OldVersionId, (await ReadWorkflowStateAsync()).RootActivity!.ActivityVersionId);
        Assert.Equal(SeededWorkflowRevision, EfWorkflowDraftRevision.Of((await ReadWorkflowDraftAsync()).LastModifiedAt));
        Assert.Equal(ActivityUpgradePlanStatus.Ready, (await ReadPlanAsync(planId)).Status);
        var receipt = await ReadReceiptAsync(receiptId);
        Assert.Equal(ActivityUpgradeApplyReceiptStatus.Preparing, receipt.Status);
        Assert.Equal(1, receipt.Revision);
        Assert.Null(receipt.Result);
        var projection = await ReadProjectionAsync();
        Assert.Equal(1, projection.Sequence);
        Assert.All(projection.Items, item => Assert.Equal(OldVersionId, item.Dependency.VersionId));
    }

    private async Task WidenProjectionAsync(int occurrences)
    {
        await using var activities = database.Activities();
        var from = await activities.ActivityDefinitionVersionPublications.SingleAsync(x => x.DefinitionVersionId == OldVersionId);
        var projection = await activities.ActivityDependencyProjections.SingleAsync(x => x.Id == ActivityDependencyProjectionState.CurrentId);
        projection.Items = Enumerable.Range(0, occurrences)
            .Select(index => DependencyItem("ActivityDraft", ActivityDefinitionId, ActivityDraftId, 2, $"occurrence-{index:D4}", from))
            .ToList();
        await activities.SaveChangesAsync();
    }

    private async Task<ActivityDefinitionDraft> ReadActivityDraftAsync(string draftId = ActivityDraftId)
    {
        await using var activities = database.Activities();
        IActivityDefinitionDraftStore drafts = new EfActivityDesignStores(activities, TestAccess.Scoped(Tenant));
        return await drafts.FindAsync(draftId) ?? throw new InvalidOperationException($"Activity draft '{draftId}' is missing.");
    }

    private async Task<WorkflowDefinitionDraft> ReadWorkflowDraftAsync()
    {
        await using var workflows = database.Workflows();
        return await workflows.Drafts.AsNoTracking().SingleAsync(x => x.Id == WorkflowDraftId);
    }

    private async Task<Elsa.Workflows.Design.Core.Models.WorkflowDefinitionState> ReadWorkflowStateAsync() =>
        payloads.Deserialize<Elsa.Workflows.Design.Core.Models.WorkflowDefinitionState>((await ReadWorkflowDraftAsync()).StateSource!);

    private async Task<ActivityUpgradePlan> ReadPlanAsync(string planId)
    {
        await using var activities = database.Activities();
        return await new EfActivityDesignStores(activities, TestAccess.Scoped(Tenant)).FindAsync(planId)
               ?? throw new InvalidOperationException($"Upgrade plan '{planId}' is missing.");
    }

    private async Task<ActivityUpgradeApplyReceipt> ReadReceiptAsync(string receiptId)
    {
        await using var activities = database.Activities();
        IActivityUpgradeApplyReceiptStore receipts = new EfActivityDesignStores(activities, TestAccess.Scoped(Tenant));
        return await receipts.FindAsync(receiptId) ?? throw new InvalidOperationException($"Upgrade receipt '{receiptId}' is missing.");
    }

    private async Task<ActivityDependencyProjectionState> ReadProjectionAsync()
    {
        await using var activities = database.Activities();
        return await activities.ActivityDependencyProjections.AsNoTracking()
            .SingleAsync(x => x.Id == ActivityDependencyProjectionState.CurrentId);
    }
}

internal sealed class FrozenTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
