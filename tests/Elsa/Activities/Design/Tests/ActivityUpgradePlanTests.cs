using System.Text.Json;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Contracts;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Publishing.Api.Commands;
using Elsa.Workflows.Publishing.Core.Contracts;
using Xunit;

namespace Elsa.Activities.Design.Tests;

public sealed class ActivityUpgradePlanTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Planner_orders_affected_owners_bottom_up_and_pins_every_snapshot()
    {
        var child = Owner("ActivityDraft", "activity-child", "draft-child", 7, Path(
            Reference("ActivityDraft", "activity-child", draft: "draft-child"),
            Reference("ActivityVersion", "dependency", version: "old")));
        var parent = Owner("WorkflowDraft", "workflow-parent", "draft-parent", 11, Path(
            Reference("WorkflowDraft", "workflow-parent", draft: "draft-parent"),
            Reference("ActivityDraft", "activity-child", draft: "draft-child"),
            Reference("ActivityVersion", "dependency", version: "old")));
        var store = new PlanStore();
        var planner = Planner(new([parent, child], []), store);

        var plan = await planner.PlanAsync(Request());

        Assert.Equal(ActivityUpgradePlanStatus.Ready, plan.Status);
        Assert.Equal(["activity-child", "workflow-parent"], plan.Steps.Select(x => x.Target.DefinitionId));
        Assert.Equal([plan.Steps[0].StepId], plan.Steps[1].DependsOnStepIds);
        Assert.Equal(ActivityVersionCompatibility.Compatible, plan.Steps[0].ResultingDiff!.Compatibility);
        Assert.Equal(ActivityVersionBump.Minor, plan.Steps[0].ResultingDiff!.RequiredBump);
        Assert.Null(plan.Steps[1].ResultingDiff);
        Assert.Contains(plan.ExpectedSnapshots, x => x.Kind == "ActivityDraft" && x.Revision == 7 && x.HeadVersionId == "head-activity-child");
        Assert.Contains(plan.ExpectedSnapshots, x => x.Kind == "WorkflowDefinition" && x.Id == "workflow-parent" && x.HeadVersionId == "head-workflow-parent");
        Assert.All(plan.Steps, x => Assert.False(string.IsNullOrWhiteSpace(x.StageId)));
        Assert.Equal(plan.Steps.Select(x => x.StageId), plan.Stages.Select(x => x.StageId));
        Assert.Equal(Request().Roots, plan.Binding!.Roots);
        Assert.Equal(Request().AccessProfileFingerprint, plan.Binding.AccessProfileFingerprint);
        Assert.Contains(plan.Binding.SelectedClosure, x => x.Kind == "WorkflowDraft" && x.DraftId == "draft-parent");
        Assert.Same(plan, await store.FindAsync(plan.PlanId));
    }

    [Fact]
    public async Task Planner_accepts_exact_version_roots_without_mislabeling_the_identity_as_a_draft()
    {
        var owner = new ActivityUpgradeOwnerSnapshot(
            Reference("ActivityVersion", "activity-child", version: "version-child"),
            "head-activity-child",
            null,
            [new("occurrence-activity-child", "old", "new")],
            [Path(
                Reference("ActivityVersion", "activity-child", version: "version-child"),
                Reference("ActivityVersion", "dependency", version: "old"))]);
        var request = Request() with { Roots = [new("ActivityVersion", "version-child")] };

        var plan = await Planner(new([owner], []), new PlanStore()).PlanAsync(request);

        var step = Assert.Single(plan.Steps);
        Assert.Equal(ActivityUpgradeAction.CloneActivityVersion, step.Action);
        Assert.Equal("ActivityDraft", step.Target.Kind);
        Assert.Equal("version-child", step.Target.SourceVersionId);
    }

    [Fact]
    public async Task Planner_rejects_duplicate_root_identities()
    {
        var request = Request() with
        {
            Roots = [new("WorkflowDraft", "draft-parent"), new("WorkflowDraft", "draft-parent")]
        };

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await Planner(new([], []), new PlanStore()).PlanAsync(request));
    }

    [Fact]
    public async Task Planner_blocks_parent_handoff_until_child_has_an_exact_published_version()
    {
        var parent = Owner("WorkflowDraft", "workflow-parent", "draft-parent", 11, Path(
            Reference("WorkflowDraft", "workflow-parent", draft: "draft-parent"),
            Reference("ActivityVersion", "activity-child", version: "child-v1"),
            Reference("ActivityVersion", "dependency", version: "old"))) with
        {
            DirectReplacements = [],
            RequiresPublishedChildVersion = true,
            RequiredChildStepId = "step-child"
        };

        var plan = await Planner(new([parent], []), new PlanStore()).PlanAsync(Request());

        Assert.Equal(ActivityUpgradePlanStatus.AwaitingPublication, plan.Status);
        var diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal("activity.upgrade.requires-published-version", diagnostic.Code);
        Assert.Equal("step-child", diagnostic.Metadata!["requiredStepId"]);
    }

    [Fact]
    public async Task Planner_preserves_breaking_compatibility_and_required_major_bump()
    {
        var owner = Owner("ActivityDraft", "activity-child", "draft-child", 7, Path(
            Reference("ActivityDraft", "activity-child", draft: "draft-child"),
            Reference("ActivityVersion", "dependency", version: "old")));
        var expected = Diff(ActivityVersionCompatibility.Breaking, ActivityVersionBump.Major);

        var plan = await Planner(new([owner], []), new PlanStore(), new FixedDiffBuilder(expected)).PlanAsync(Request());

        var actual = Assert.Single(plan.Steps).ResultingDiff!;
        Assert.Equal(ActivityVersionCompatibility.Breaking, actual.Compatibility);
        Assert.Equal(ActivityVersionBump.Major, actual.RequiredBump);
    }

    [Fact]
    public async Task Planner_orders_diagnostics_by_the_complete_canonical_location_key()
    {
        var later = Diagnostic("/z");
        var earlier = Diagnostic("/a");

        var plan = await Planner(new([], [later, earlier]), new PlanStore()).PlanAsync(Request());

        Assert.Equal(
            ["/a", "/z"],
            plan.Diagnostics.Where(x => x.Code == "activity.test").Select(x => x.Location!.JsonPointer));
    }

    [Fact]
    public async Task Planner_blocks_every_stage_when_any_global_error_diagnostic_exists()
    {
        var owner = Owner("ActivityDraft", "activity-child", "draft-child", 7, Path(
            Reference("ActivityDraft", "activity-child", draft: "draft-child"),
            Reference("ActivityVersion", "dependency", version: "old")));
        var plan = await Planner(
            new([owner], [Diagnostic("/global")]),
            new PlanStore()).PlanAsync(Request());

        Assert.Equal(ActivityUpgradePlanStatus.Blocked, plan.Status);
        var mutations = new RecordingMutationStore();
        var exception = await Assert.ThrowsAsync<ActivityUpgradeApplyException>(async () =>
            await new ApplyActivityUpgradePlanCommand(
                    new PlanStore(plan),
                    new ReceiptStore(),
                    mutations,
                    new Clock(Now))
                .ApplyAsync(new(plan.PlanId, plan.Stages[0].StageId, "operation")));

        Assert.Equal("activity.upgrade.plan-blocked", exception.ErrorCode);
        Assert.Equal(0, mutations.Calls);
    }

    [Fact]
    public async Task Planner_unions_prerequisites_from_every_repeated_diamond_owner_path()
    {
        var left = Owner("ActivityDraft", "left", "draft-left", 1, Path(
            Reference("ActivityDraft", "left", draft: "draft-left"),
            Reference("ActivityVersion", "dependency", version: "old")));
        var right = Owner("ActivityDraft", "right", "draft-right", 1, Path(
            Reference("ActivityDraft", "right", draft: "draft-right"),
            Reference("ActivityVersion", "dependency", version: "old")));
        var parentReference = Reference("WorkflowDraft", "parent", draft: "draft-parent", revision: 1);
        var parent = new ActivityUpgradeOwnerSnapshot(
            parentReference,
            "head-parent",
            null,
            [new("parent-occurrence", "old", "new")],
            [
                [parentReference, left.Owner, Reference("ActivityVersion", "dependency", version: "old")],
                [parentReference, right.Owner, Reference("ActivityVersion", "dependency", version: "old")]
            ]);

        var plan = await Planner(new([parent, left, right], []), new PlanStore()).PlanAsync(Request());

        var leftStep = plan.Steps.Single(x => x.Target.DefinitionId == "left");
        var rightStep = plan.Steps.Single(x => x.Target.DefinitionId == "right");
        var parentStep = plan.Steps.Single(x => x.Target.DefinitionId == "parent");
        Assert.Equal(
            new[] { leftStep.StepId, rightStep.StepId }.Order(StringComparer.Ordinal),
            parentStep.DependsOnStepIds);
        Assert.Contains(plan.Binding!.SelectedClosure, x => x.DraftId == "draft-left");
        Assert.Contains(plan.Binding.SelectedClosure, x => x.DraftId == "draft-right");
    }

    [Fact]
    public async Task Post_rewrite_candidate_is_compiled_and_dependency_change_requires_minor_bump()
    {
        var baseline = Publication("base", "owner", "hash-base");
        var oldDependency = Publication("old", "dependency", "hash-old");
        var newDependency = Publication("new", "dependency", "hash-new");
        var publications = new PublicationStore(baseline, oldDependency, newDependency);
        var ownerReference = new ActivityDefinitionReference("ActivityDraft", "owner", DraftId: "draft", Revision: 4);
        var newReference = new ActivityDefinitionReference("ActivityVersion", "dependency", "new", "2.0.0", TemplateHash: "hash-new", Lifecycle: ActivityDefinitionVersionLifecycle.Active);
        var candidate = new ActivityDraftDiffCandidate(
            "hash-candidate",
            "provider",
            1,
            0,
            [],
            [new("candidate:node", ownerReference, newReference, new("node", []), true, 1, [ownerReference, newReference])],
            [],
            []);
        var request = new ActivityDraftDiffCandidateRequest(
            "owner",
            "draft",
            4,
            new(Contract(), Provider(), new Dictionary<string, string>()),
            [],
            "base");
        var snapshot = new ActivityUpgradeOwnerSnapshot(
            ownerReference,
            "base",
            null,
            [new("node", "old", "new")],
            [[ownerReference, newReference]],
            DiffCandidate: request);
        var builder = new ActivityUpgradeDiffBuilder(
            new CandidateCompiler(candidate),
            new ActivityVersionDiffer(),
            publications,
            new DependencyStore(new ActivityDependencyEdge
            {
                Id = "edge",
                OwnerVersionId = "base",
                OwnerTemplateHash = "hash-base",
                DependencyVersionId = "old",
                DependencyTemplateHash = "hash-old",
                OccurrenceId = "node",
                NodeOrigin = []
            }),
            new LayoutStore());

        var diff = await builder.BuildAsync(snapshot);

        Assert.NotNull(diff);
        Assert.Equal(ActivityVersionCompatibility.Compatible, diff.Compatibility);
        Assert.Equal(ActivityVersionBump.Minor, diff.RequiredBump);
        Assert.Contains(diff.Changes, x => x.Kind == "DependencyVersionChanged" && x.Subject.OccurrenceId == "node");
    }

    [Fact]
    public async Task Apply_rejects_a_stage_with_unapplied_prerequisites_before_mutation()
    {
        var plan = ReadyPlan();
        var plans = new PlanStore(plan);
        var mutations = new RecordingMutationStore();
        var command = new ApplyActivityUpgradePlanCommand(plans, new ReceiptStore(), mutations, new Clock(Now));

        var exception = await Assert.ThrowsAsync<ActivityUpgradeApplyException>(async () =>
            await command.ApplyAsync(new(plan.PlanId, "stage-parent", "operation-1")));

        Assert.Equal(422, exception.StatusCode);
        Assert.Equal("activity.upgrade.stage-prerequisite", exception.ErrorCode);
        Assert.Equal(0, mutations.Calls);
    }

    [Fact]
    public async Task Apply_passes_one_bottom_up_atomic_stage_and_never_mutates_published_material()
    {
        var plan = ReadyPlan();
        var published = new Dictionary<string, string>(StringComparer.Ordinal) { ["activity-v1"] = "immutable", ["workflow-v1"] = "immutable" };
        var mutations = new RecordingMutationStore(published);
        var command = new ApplyActivityUpgradePlanCommand(new PlanStore(plan), new ReceiptStore(), mutations, new Clock(Now));

        var result = await command.ApplyAsync(new(plan.PlanId, "stage-child", "operation-1"));

        Assert.Equal(ActivityUpgradePlanStatus.Applied, result.Status);
        Assert.Equal(["step-child"], mutations.SelectedStepIds);
        Assert.Equal("immutable", published["activity-v1"]);
        Assert.Equal("immutable", published["workflow-v1"]);
    }

    [Fact]
    public async Task Apply_rejects_an_empty_idempotency_key()
    {
        var mutations = new RecordingMutationStore();
        var plan = ReadyPlan();
        var command = new ApplyActivityUpgradePlanCommand(new PlanStore(plan), new ReceiptStore(), mutations, new Clock(Now));

        var exception = await Assert.ThrowsAsync<ActivityUpgradeApplyException>(async () =>
            await command.ApplyAsync(new(plan.PlanId, "stage-child", "")));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("activity.request.invalid", exception.ErrorCode);
        Assert.Equal(0, mutations.Calls);
    }

    [Fact]
    public async Task Apply_handler_exposes_an_empty_idempotency_key_as_the_canonical_request_error()
    {
        var plan = ReadyPlan();
        var store = new PlanStore(plan);
        var handler = new ApplyActivityUpgradePlanHandler(
            new ApplyActivityUpgradePlanCommand(store, new ReceiptStore(), new RecordingMutationStore(), new Clock(Now)),
            store,
            new AuthoringContext(null));

        var exception = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            handler.Handle(new(plan.PlanId, "stage-child", ""), default));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("activity.request.invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task Stale_apply_reports_conflict_and_commits_no_partial_draft_edits()
    {
        var plan = ReadyPlan();
        var mutations = new RecordingMutationStore { FailStale = true };
        var command = new ApplyActivityUpgradePlanCommand(new PlanStore(plan), new ReceiptStore(), mutations, new Clock(Now));

        var exception = await Assert.ThrowsAsync<ActivityUpgradeApplyException>(async () => await command.ApplyAsync(new(plan.PlanId, "stage-child", "operation-1")));

        Assert.Equal(409, exception.StatusCode);
        Assert.Equal("activity.upgrade.stale-plan", exception.ErrorCode);
        Assert.Empty(mutations.Drafts);
    }

    [Fact]
    public async Task Plan_expiry_is_checked_before_any_mutation()
    {
        var plan = ReadyPlan() with { ExpiresAt = Now };
        var mutations = new RecordingMutationStore();
        var command = new ApplyActivityUpgradePlanCommand(new PlanStore(plan), new ReceiptStore(), mutations, new Clock(Now));

        var exception = await Assert.ThrowsAsync<ActivityUpgradeApplyException>(async () => await command.ApplyAsync(new(plan.PlanId, "stage-child", "operation-1")));

        Assert.Equal(410, exception.StatusCode);
        Assert.Equal(0, mutations.Calls);
    }

    [Fact]
    public async Task Repeated_apply_returns_the_durable_identical_apply_result_without_mutating_again()
    {
        var appliedAt = Now.AddMinutes(-2);
        var drafts = new[] { new ActivityUpgradeAppliedDraft("WorkflowDraft", "draft-w", "workflow", 6, false) };
        var plan = ReadyPlan();
        var result = new ActivityUpgradeApplyResult(plan.PlanId, ActivityUpgradePlanStatus.Applied, appliedAt, drafts, [], "receipt", "stage-child");
        var receipts = new ReceiptStore(new ActivityUpgradeApplyReceipt(
            "receipt",
            plan.PlanId,
            "stage-child",
            ActivityUpgradeApplyIdentity.HashIdempotencyKey("operation-1"),
            ActivityUpgradeApplyIdentity.CreateRequestFingerprint(plan.PlanId, "stage-child"),
            null,
            plan.Binding!.AccessProfileFingerprint,
            ActivityUpgradeApplyReceiptStatus.Applied,
            appliedAt,
            appliedAt,
            2,
            result));
        var mutations = new RecordingMutationStore();
        var command = new ApplyActivityUpgradePlanCommand(new PlanStore(plan), receipts, mutations, new Clock(Now));

        var replay = await command.ApplyAsync(new(plan.PlanId, "stage-child", "operation-1"));

        Assert.Equal(appliedAt, replay.AppliedAt);
        Assert.Equal(drafts, replay.Drafts);
        Assert.Equal(0, mutations.Calls);
    }

    [Fact]
    public async Task Preparing_receipt_requires_status_reconciliation_before_retry()
    {
        var plan = ReadyPlan();
        var key = "operation-1";
        var receipt = new ActivityUpgradeApplyReceipt(
            "receipt",
            plan.PlanId,
            "stage-child",
            ActivityUpgradeApplyIdentity.HashIdempotencyKey(key),
            ActivityUpgradeApplyIdentity.CreateRequestFingerprint(plan.PlanId, "stage-child"),
            null,
            plan.Binding!.AccessProfileFingerprint,
            ActivityUpgradeApplyReceiptStatus.Preparing,
            Now,
            Now,
            1,
            LeaseExpiresAt: Now.AddMinutes(1));
        var mutations = new RecordingMutationStore();
        var command = new ApplyActivityUpgradePlanCommand(
            new PlanStore(plan),
            new ReceiptStore(receipt),
            mutations,
            new Clock(Now));

        var exception = await Assert.ThrowsAsync<ActivityUpgradeApplyException>(async () =>
            await command.ApplyAsync(new(plan.PlanId, "stage-child", key)));

        Assert.Equal("activity.upgrade.outcome-unknown", exception.ErrorCode);
        Assert.Equal("receipt", Assert.Single(exception.Diagnostics).Metadata!["receiptId"]);
        Assert.Equal(0, mutations.Calls);
    }

    [Fact]
    public async Task Expired_preparing_receipt_is_reclaimed_by_revision_and_applied_once()
    {
        var plan = ReadyPlan();
        var key = "operation-1";
        var receipt = new ActivityUpgradeApplyReceipt(
            "receipt",
            plan.PlanId,
            "stage-child",
            ActivityUpgradeApplyIdentity.HashIdempotencyKey(key),
            ActivityUpgradeApplyIdentity.CreateRequestFingerprint(plan.PlanId, "stage-child"),
            null,
            plan.Binding!.AccessProfileFingerprint,
            ActivityUpgradeApplyReceiptStatus.Preparing,
            Now.AddMinutes(-5),
            Now.AddMinutes(-5),
            1,
            LeaseExpiresAt: Now.AddMinutes(-1));
        var receipts = new ReceiptStore(receipt);
        var mutations = new RecordingMutationStore();

        var result = await new ApplyActivityUpgradePlanCommand(
                new PlanStore(plan),
                receipts,
                mutations,
                new Clock(Now))
            .ApplyAsync(new(plan.PlanId, "stage-child", key));

        Assert.Equal(ActivityUpgradePlanStatus.Applied, result.Status);
        Assert.Equal(1, mutations.Calls);
        var reclaimed = await receipts.FindAsync(receipt.ReceiptId);
        Assert.Equal(2, reclaimed!.Revision);
        Assert.True(reclaimed.LeaseExpiresAt > Now);
    }

    [Fact]
    public async Task Reusing_an_idempotency_key_for_another_stage_is_rejected()
    {
        var plan = ReadyPlan();
        var key = "operation-1";
        var receipt = new ActivityUpgradeApplyReceipt(
            "receipt",
            plan.PlanId,
            "stage-parent",
            ActivityUpgradeApplyIdentity.HashIdempotencyKey(key),
            ActivityUpgradeApplyIdentity.CreateRequestFingerprint(plan.PlanId, "stage-parent"),
            null,
            plan.Binding!.AccessProfileFingerprint,
            ActivityUpgradeApplyReceiptStatus.Preparing,
            Now,
            Now,
            1);
        var command = new ApplyActivityUpgradePlanCommand(
            new PlanStore(plan),
            new ReceiptStore(receipt),
            new RecordingMutationStore(),
            new Clock(Now));

        var exception = await Assert.ThrowsAsync<ActivityUpgradeApplyException>(async () =>
            await command.ApplyAsync(new(plan.PlanId, "stage-child", key)));

        Assert.Equal("activity.upgrade.idempotency-conflict", exception.ErrorCode);
    }

    [Fact]
    public async Task Refresh_uses_the_version_published_from_the_exact_handoff_draft()
    {
        var predecessor = ReadyPlan();
        var result = new ActivityUpgradeApplyResult(
            predecessor.PlanId,
            ActivityUpgradePlanStatus.AwaitingPublication,
            Now,
            [new("ActivityDraft", "draft-new", "activity", 1, true, "child-v1")],
            [],
            "receipt",
            "stage-child",
            [new("ActivityDraft", "draft-new", "activity", 1, "child-v1", ["stage-parent"])]);
        var receipt = new ActivityUpgradeApplyReceipt(
            "receipt",
            predecessor.PlanId,
            "stage-child",
            "key",
            "request",
            null,
            predecessor.Binding!.AccessProfileFingerprint,
            ActivityUpgradeApplyReceiptStatus.Applied,
            Now,
            Now,
            2,
            result);
        var parent = Owner("WorkflowDraft", "workflow-parent", "draft-parent", 11, Path(
            Reference("WorkflowDraft", "workflow-parent", draft: "draft-parent"),
            Reference("ActivityVersion", "activity", version: "child-v1"))) with
        {
            DirectReplacements = [new("node", "child-v1", "child-v2")]
        };
        var store = new PlanStore(predecessor);
        var planner = Planner(
            new([parent], []),
            store,
            receipts: new ReceiptStore(receipt),
            resolver: new PublishedDraftResolver(new ActivityUpgradePublishedDraft("ActivityVersion", "draft-new", "activity", "child-v2")));

        var successor = await planner.RefreshAsync(new(
            predecessor.PlanId,
            [new(receipt.ReceiptId, [new("draft-new", "child-v2")])],
            null,
            predecessor.Binding.AccessProfileFingerprint));

        Assert.Equal(predecessor.PlanId, successor.PredecessorPlanId);
        Assert.Equal(new ActivityVersionReplacement("child-v1", "child-v2"), Assert.Single(successor.Replacements));
        var linked = await store.FindAsync(predecessor.PlanId);
        Assert.Equal(ActivityUpgradePlanStatus.Superseded, linked!.Status);
        Assert.Equal(successor.PlanId, linked.SuccessorPlanId);
    }

    [Fact]
    public async Task Refresh_cumulatively_advances_successors_for_separately_published_siblings()
    {
        var predecessor = ReadyPlan();
        var receiptA = PublicationReceipt(predecessor, "receipt-a", "draft-a", "activity-a", "a-v1", "stage-a");
        var receiptB = PublicationReceipt(predecessor, "receipt-b", "draft-b", "activity-b", "b-v1", "stage-b");
        var owner = Owner("WorkflowDraft", "workflow-parent", "draft-parent", 11, Path(
            Reference("WorkflowDraft", "workflow-parent", draft: "draft-parent"),
            Reference("ActivityVersion", "activity-a", version: "a-v1")));
        var store = new PlanStore(predecessor);
        var planner = Planner(
            new([owner], []),
            store,
            receipts: new ReceiptStore(receiptA, receiptB),
            resolver: new PublishedDraftResolver(
                new("ActivityVersion", "draft-a", "activity-a", "a-v2"),
                new("ActivityVersion", "draft-b", "activity-b", "b-v2")));

        var first = await planner.RefreshAsync(new(
            predecessor.PlanId,
            [new(receiptA.ReceiptId, [new("draft-a", "a-v2")])],
            null,
            predecessor.Binding!.AccessProfileFingerprint));
        var advanced = await planner.RefreshAsync(new(
            predecessor.PlanId,
            [new(receiptB.ReceiptId, [new("draft-b", "b-v2")])],
            null,
            predecessor.Binding.AccessProfileFingerprint));

        Assert.Equal(first.PlanId, advanced.PredecessorPlanId);
        Assert.Equal(
            [new ActivityVersionReplacement("a-v1", "a-v2"), new("b-v1", "b-v2")],
            advanced.Replacements);
        Assert.Equal(advanced.PlanId, (await store.FindAsync(first.PlanId))!.SuccessorPlanId);
    }

    [Fact]
    public async Task Planner_binds_the_plan_to_the_internal_tenant_scope()
    {
        var request = Request() with { TenantId = "tenant-a" };

        var plan = await Planner(new([], []), new PlanStore()).PlanAsync(request);

        Assert.Equal("tenant-a", plan.TenantId);
    }

    [Fact]
    public async Task Planner_omits_owners_and_diagnostics_outside_the_authorized_access_profile()
    {
        var hidden = Owner(
            "WorkflowDraft",
            "hidden-definition",
            "hidden-draft",
            3,
            Path(
                Reference("WorkflowDraft", "hidden-definition", draft: "hidden-draft"),
                Reference("ActivityVersion", "dependency", version: "old")));
        var diagnostic = new ActivityDiagnostic(
            "activity.upgrade.owner-unavailable",
            ActivityDiagnosticSeverity.Error,
            "hidden",
            new("WorkflowDraft", "unrequested-hidden-draft", "unrequested-hidden-definition"));
        var planner = Planner(
            new([hidden], [diagnostic]),
            new PlanStore(),
            authorization: new Authorization(canRead: _ => false));
        var request = Request() with { Roots = [new("WorkflowDraft", "hidden-draft")] };

        var plan = await planner.PlanAsync(request);

        Assert.Empty(plan.Steps);
        Assert.Equal(ActivityUpgradePlanStatus.Blocked, plan.Status);
        Assert.DoesNotContain("unrequested-hidden", JsonSerializer.Serialize(plan), StringComparison.Ordinal);
        var rootDiagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal("activity.upgrade.root-not-affected", rootDiagnostic.Code);
        Assert.Equal("hidden-draft", rootDiagnostic.Subject.Id);
    }

    [Fact]
    public async Task Read_and_apply_hide_a_plan_from_a_different_tenant()
    {
        var plan = ReadyPlan() with { TenantId = "tenant-a" };
        var store = new PlanStore(plan);
        var context = new AuthoringContext("tenant-b");
        var applier = new RecordingApplier();
        var publications = new PublicationStore(Publication("old", "dependency", "hash-old"), Publication("new", "dependency", "hash-new"));

        var readException = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            new GetActivityUpgradePlanHandler(store, publications, context).Handle(new(plan.PlanId), default));
        var applyException = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            new ApplyActivityUpgradePlanHandler(applier, store, context).Handle(new(plan.PlanId, "stage-child", "operation-1"), default));

        Assert.Equal("activity.upgrade.plan-not-found", readException.ErrorCode);
        Assert.Equal("activity.upgrade.plan-not-found", applyException.ErrorCode);
        Assert.Equal(0, applier.Calls);
    }

    [Fact]
    public async Task Read_and_apply_hide_a_plan_after_the_access_profile_changes()
    {
        var plan = ReadyPlan();
        var store = new PlanStore(plan);
        var context = new AuthoringContext(null, "global/read-only");
        var applier = new RecordingApplier();
        var publications = new PublicationStore(
            Publication("old", "dependency", "hash-old"),
            Publication("new", "dependency", "hash-new"));

        var readException = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            new GetActivityUpgradePlanHandler(store, publications, context).Handle(new(plan.PlanId), default));
        var applyException = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            new ApplyActivityUpgradePlanHandler(applier, store, context).Handle(
                new(plan.PlanId, "stage-child", "operation-1"),
                default));

        Assert.Equal("activity.upgrade.plan-not-found", readException.ErrorCode);
        Assert.Equal("activity.upgrade.plan-not-found", applyException.ErrorCode);
        Assert.Equal(0, applier.Calls);
    }

    [Fact]
    public async Task Create_handler_translates_invalid_plan_arguments_to_the_canonical_request_error()
    {
        var handler = new CreateActivityUpgradePlanHandler(
            Planner(new([], []), new PlanStore()),
            new PublicationStore(),
            new AuthoringContext(null));

        var exception = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            handler.Handle(new([], [], true, false), default));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("activity.request.invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task Upgrade_plan_view_projects_nested_exact_version_identities_and_canonical_diagnostics()
    {
        var later = Diagnostic("/z");
        var earlier = Diagnostic("/a");
        var plan = ReadyPlan();
        plan = plan with
        {
            Diagnostics = [later, earlier],
            Steps = [plan.Steps[0] with { Diagnostics = [later, earlier] }, plan.Steps[1]]
        };
        var publications = new PublicationStore(
            Publication("old", "dependency", "hash-old"),
            Publication("new", "dependency", "hash-new"));

        var view = await plan.ToViewAsync(publications);

        var replacement = Assert.Single(view.Replacements);
        Assert.Equal(new("dependency", "old", "1.0.0", "hash-old"), replacement.From);
        Assert.Equal(new("dependency", "new", "2.0.0", "hash-new"), replacement.To);
        Assert.Equal(["/a", "/z"], view.Diagnostics.Select(x => x.Location!.JsonPointer));
        Assert.Equal(["/a", "/z"], view.Steps[0].Diagnostics.Select(x => x.Location!.JsonPointer));

        var json = JsonSerializer.Serialize(view, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"from\":{\"definitionId\":\"dependency\",\"versionId\":\"old\",\"version\":\"1.0.0\",\"templateHash\":\"hash-old\"}", json, StringComparison.Ordinal);
        Assert.Contains("\"to\":{\"definitionId\":\"dependency\",\"versionId\":\"new\",\"version\":\"2.0.0\",\"templateHash\":\"hash-new\"}", json, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);
        var replacementJson = document.RootElement.GetProperty("replacements")[0];
        Assert.False(replacementJson.TryGetProperty("fromVersionId", out _));
        Assert.False(replacementJson.TryGetProperty("toVersionId", out _));
    }

    private static ActivityUpgradePlanner Planner(
        ActivityUpgradeDiscovery discovery,
        IActivityUpgradePlanStore store,
        IActivityUpgradeDiffBuilder? diffBuilder = null,
        IActivityUpgradeApplyReceiptStore? receipts = null,
        IActivityUpgradePublishedDraftResolver? resolver = null,
        IActivityDependencyContextAsync? authorization = null) =>
        new(
            new Discovery(discovery),
            store,
            receipts ?? new ReceiptStore(),
            resolver ?? new PublishedDraftResolver(),
            diffBuilder ?? new DiffBuilder(),
            authorization ?? new Authorization(),
            new Ids(),
            new Clock(Now));

    private static ActivityUpgradePlanRequest Request() => new(
        [new("old", "new")],
        [new("WorkflowDraft", "draft-parent")],
        true,
        false,
        AccessProfileFingerprint: ActivityAccessProfileFingerprint.Create("global/manage"));

    private static ActivityUpgradeOwnerSnapshot Owner(
        string kind,
        string definition,
        string draft,
        long revision,
        IReadOnlyList<ActivityDefinitionReference> path) => new(
        Reference(kind, definition, draft: draft, revision: revision),
        $"head-{definition}",
        null,
        [new($"occurrence-{definition}", "old", "new")],
        [path]);

    private static ActivityDefinitionReference Reference(
        string kind,
        string definition,
        string? version = null,
        string? draft = null,
        long? revision = null) => new(kind, definition, version, DraftId: draft, Revision: revision);

    private static IReadOnlyList<ActivityDefinitionReference> Path(params ActivityDefinitionReference[] references) => references;

    private static ActivityContract Contract() => new("1", [], [], []);
    private static ActivityProviderManifest Provider() => new("graph", "1", JsonDocument.Parse("{}").RootElement.Clone());

    private static ActivityDiagnostic Diagnostic(string pointer) => new(
        "activity.test",
        ActivityDiagnosticSeverity.Error,
        "Test diagnostic.",
        new("WorkflowDraft", "draft-parent"),
        new(JsonPointer: pointer),
        Metadata: new Dictionary<string, string>());

    private static ActivityDefinitionVersionPublication Publication(string id, string definitionId, string hash) => new()
    {
        Id = id,
        DefinitionVersionId = id,
        DefinitionId = definitionId,
        Version = id == "new" ? "2.0.0" : "1.0.0",
        ActivityTypeKey = definitionId,
        Contract = Contract(),
        Provider = Provider(),
        TemplateId = $"template-{id}",
        TemplateHash = hash,
        SourceReferenceId = $"source-{id}",
        ProviderFingerprint = "provider",
        DirectDependencyCount = id == "base" ? 1 : 0,
        ClosedTemplateCount = 1,
        RuntimeRequirements = [],
        Lifecycle = ActivityDefinitionVersionLifecycle.Active,
        PublishedAt = Now
    };

    private static ActivityUpgradeApplyReceipt PublicationReceipt(
        ActivityUpgradePlan plan,
        string receiptId,
        string draftId,
        string definitionId,
        string sourceVersionId,
        string stageId)
    {
        var result = new ActivityUpgradeApplyResult(
            plan.PlanId,
            ActivityUpgradePlanStatus.AwaitingPublication,
            Now,
            [new("ActivityDraft", draftId, definitionId, 1, true, sourceVersionId)],
            [],
            receiptId,
            stageId,
            [new("ActivityDraft", draftId, definitionId, 1, sourceVersionId, ["stage-parent"])]);
        return new(
            receiptId,
            plan.PlanId,
            stageId,
            $"key-{receiptId}",
            $"request-{receiptId}",
            null,
            plan.Binding!.AccessProfileFingerprint,
            ActivityUpgradeApplyReceiptStatus.Applied,
            Now,
            Now,
            2,
            result);
    }

    private static ActivityUpgradePlan ReadyPlan()
    {
        var child = new ActivityUpgradeStep(
            "step-child", 10, new("ActivityDraft", "activity", "draft-a", 3), ActivityUpgradeAction.UpdateDraft,
            [], [new("node-a", "old", "new")], 3, "activity-head", null, [], "stage-child");
        var parent = new ActivityUpgradeStep(
            "step-parent", 20, new("WorkflowDraft", "workflow", "draft-w", 5), ActivityUpgradeAction.UpdateDraft,
            [child.StepId], [new("node-w", "old", "new")], 5, "workflow-head", null, [], "stage-parent");
        return new(
            "plan-1", Now.AddMinutes(-1), Now.AddMinutes(29), ActivityUpgradePlanStatus.Ready,
            [new("old", "new")],
            [new("ActivityDraft", "draft-a", 3, "activity", "activity-head"), new("WorkflowDraft", "draft-w", 5, "workflow", "workflow-head")],
            [child, parent],
            [],
            Binding: new(
                [new("WorkflowDraft", "draft-w")],
                true,
                false,
                ActivityAccessProfileFingerprint.Create("global/manage"),
                []),
            Stages:
            [
                new("stage-child", 10, ActivityUpgradeStageStatus.Ready, ["step-child"], []),
                new("stage-parent", 20, ActivityUpgradeStageStatus.Ready, ["step-parent"], ["stage-child"])
            ]);
    }

    private sealed class Discovery(ActivityUpgradeDiscovery result) : IActivityUpgradeDiscoverySource
    {
        public ValueTask<ActivityUpgradeDiscovery> DiscoverAsync(ActivityUpgradePlanRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(result);
    }

    private sealed class DiffBuilder : IActivityUpgradeDiffBuilder
    {
        public ValueTask<ActivityVersionDiff?> BuildAsync(ActivityUpgradeOwnerSnapshot owner, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ActivityVersionDiff?>(owner.Owner.Kind.StartsWith("Activity", StringComparison.Ordinal) && !owner.RequiresPublishedChildVersion
                ? Diff(ActivityVersionCompatibility.Compatible, ActivityVersionBump.Minor)
                : null);
    }

    private sealed class FixedDiffBuilder(ActivityVersionDiff result) : IActivityUpgradeDiffBuilder
    {
        public ValueTask<ActivityVersionDiff?> BuildAsync(ActivityUpgradeOwnerSnapshot owner, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ActivityVersionDiff?>(result);
    }

    private sealed class CandidateCompiler(ActivityDraftDiffCandidate result) : IActivityDraftDiffCandidateCompiler
    {
        public ValueTask<ActivityDraftDiffCandidate> CompileAsync(ActivityDraftDiffCandidateRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(result);
    }

    private sealed class PublicationStore(params ActivityDefinitionVersionPublication[] items) : IActivityDefinitionVersionPublicationStore
    {
        public Task<ActivityDefinitionVersionPublication?> FindAsync(string definitionVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(items.SingleOrDefault(x => x.DefinitionVersionId == definitionVersionId));
        public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersionPublication>>(items.Where(x => x.DefinitionId == definitionId).ToArray());
    }

    private sealed class DependencyStore(params ActivityDependencyEdge[] items) : IActivityDirectDependencyStore
    {
        public Task<IReadOnlyList<ActivityDependencyEdge>> ListOutboundAsync(string ownerVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDependencyEdge>>(items.Where(x => x.OwnerVersionId == ownerVersionId).ToArray());
    }

    private sealed class LayoutStore : IActivityDefinitionLayoutStore
    {
        public Task<ActivityDefinitionDraftLayout?> FindDraftLayoutAsync(string draftId, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinitionDraftLayout?>(null);
        public Task<ActivityDefinitionVersionLayout?> FindVersionLayoutAsync(string definitionVersionId, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinitionVersionLayout?>(null);
    }

    private static ActivityVersionDiff Diff(ActivityVersionCompatibility compatibility, ActivityVersionBump bump) => new(
        new("ActivityVersion", "activity", "old"),
        new("ActivityDraft", "activity", DraftId: "draft-a", Revision: 4),
        compatibility,
        bump,
        true,
        new("graph", "1", "graph", "1", false),
        new(compatibility == ActivityVersionCompatibility.Breaking ? 1 : 0, compatibility == ActivityVersionCompatibility.Compatible ? 1 : 0, 0, 0),
        [],
        []);

    private sealed class PlanStore : IActivityUpgradePlanStore
    {
        private readonly Dictionary<string, ActivityUpgradePlan> _plans = new(StringComparer.Ordinal);
        public PlanStore(ActivityUpgradePlan? plan = null)
        {
            if (plan is not null)
                _plans.Add(plan.PlanId, plan);
        }
        public Task<ActivityUpgradePlan?> FindAsync(string planId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_plans.GetValueOrDefault(planId));
        public Task SaveAsync(ActivityUpgradePlan plan, CancellationToken cancellationToken = default)
        {
            _plans[plan.PlanId] = plan;
            return Task.CompletedTask;
        }
        public Task LinkSuccessorAsync(string planId, string successorPlanId, CancellationToken cancellationToken = default)
        {
            if (_plans.TryGetValue(planId, out var plan))
                _plans[planId] = plan with { Status = ActivityUpgradePlanStatus.Superseded, SuccessorPlanId = successorPlanId };
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingMutationStore(IReadOnlyDictionary<string, string>? published = null) : IActivityUpgradePlanMutationStore
    {
        public int Calls { get; private set; }
        public bool FailStale { get; init; }
        public List<string> Drafts { get; } = [];
        public IReadOnlyList<string> SelectedStepIds { get; private set; } = [];

        public ValueTask<ActivityUpgradeApplyResult> ApplyAsync(
            ActivityUpgradePlan plan,
            IReadOnlyList<ActivityUpgradeStep> selectedSteps,
            ActivityUpgradeApplyReceipt receipt,
            DateTimeOffset appliedAt,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (FailStale)
                throw new ActivityUpgradeApplyException(409, "activity.upgrade.stale-plan", "stale");
            SelectedStepIds = selectedSteps.Select(x => x.StepId).ToArray();
            Drafts.AddRange(selectedSteps.Select(x => x.Target.DraftId!));
            Assert.All(published ?? new Dictionary<string, string>(), x => Assert.Equal("immutable", x.Value));
            return ValueTask.FromResult(new ActivityUpgradeApplyResult(
                plan.PlanId,
                ActivityUpgradePlanStatus.Applied,
                appliedAt,
                [],
                [],
                receipt.ReceiptId,
                receipt.StageId));
        }
    }

    private sealed class ReceiptStore(params ActivityUpgradeApplyReceipt[] receipts) : IActivityUpgradeApplyReceiptStore
    {
        private readonly Dictionary<string, ActivityUpgradeApplyReceipt> _receipts =
            receipts.ToDictionary(x => x.ReceiptId, StringComparer.Ordinal);

        public Task<ActivityUpgradeApplyReceipt?> FindAsync(string receiptId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_receipts.GetValueOrDefault(receiptId));

        public Task<ActivityUpgradeApplyReceipt?> FindByIdempotencyKeyAsync(
            string planId,
            string idempotencyKeyHash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_receipts.Values.SingleOrDefault(x =>
                StringComparer.Ordinal.Equals(x.PlanId, planId) &&
                StringComparer.Ordinal.Equals(x.IdempotencyKeyHash, idempotencyKeyHash)));

        public Task<bool> TryCreateAsync(ActivityUpgradeApplyReceipt receipt, CancellationToken cancellationToken = default)
        {
            if (_receipts.ContainsKey(receipt.ReceiptId))
                return Task.FromResult(false);
            _receipts.Add(receipt.ReceiptId, receipt);
            return Task.FromResult(true);
        }

        public Task<ActivityUpgradeApplyReceipt?> TryReclaimAsync(
            ActivityUpgradeApplyReceipt receipt,
            DateTimeOffset reclaimedAt,
            DateTimeOffset leaseExpiresAt,
            CancellationToken cancellationToken = default)
        {
            if (!_receipts.TryGetValue(receipt.ReceiptId, out var current) ||
                current.Status != ActivityUpgradeApplyReceiptStatus.Preparing ||
                current.Revision != receipt.Revision ||
                current.LeaseExpiresAt > reclaimedAt)
                return Task.FromResult<ActivityUpgradeApplyReceipt?>(null);
            var reclaimed = current with
            {
                UpdatedAt = reclaimedAt,
                Revision = current.Revision + 1,
                LeaseExpiresAt = leaseExpiresAt
            };
            _receipts[receipt.ReceiptId] = reclaimed;
            return Task.FromResult<ActivityUpgradeApplyReceipt?>(reclaimed);
        }

        public Task RejectAsync(
            ActivityUpgradeApplyReceipt receipt,
            int statusCode,
            string errorCode,
            IReadOnlyList<ActivityDiagnostic> diagnostics,
            DateTimeOffset rejectedAt,
            CancellationToken cancellationToken = default)
        {
            _receipts[receipt.ReceiptId] = receipt with
            {
                Status = ActivityUpgradeApplyReceiptStatus.Rejected,
                UpdatedAt = rejectedAt,
                Revision = receipt.Revision + 1,
                Diagnostics = diagnostics,
                RejectionStatusCode = statusCode,
                RejectionCode = errorCode
            };
            return Task.CompletedTask;
        }
    }

    private sealed class PublishedDraftResolver(params ActivityUpgradePublishedDraft[] publications) : IActivityUpgradePublishedDraftResolver
    {
        public ValueTask<ActivityUpgradePublishedDraft?> ResolveAsync(
            string publishedVersionId,
            string expectedKind,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(publications.SingleOrDefault(x =>
                StringComparer.Ordinal.Equals(x.PublishedVersionId, publishedVersionId) &&
                StringComparer.Ordinal.Equals(x.Kind, expectedKind)));
    }

    private sealed class RecordingApplier : IActivityUpgradePlanApplier
    {
        public int Calls { get; private set; }

        public ValueTask<ActivityUpgradeApplyResult> ApplyAsync(ActivityUpgradeApplyRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("A cross-tenant plan must not reach the applier.");
        }
    }

    private sealed class AuthoringContext(string? tenantId, string? authorizationProfile = null) : IActivityAuthoringContext, IActivityAuthoringContextAsync
    {
        public string? TenantId { get; } = tenantId;
        public string ActorId => "actor-a";
        public string AuthorizationProfile => authorizationProfile ?? $"{TenantId ?? "global"}/manage";
        public bool CanAuthorProvider(string providerKey) => true;
        public bool CanReadProviderPayload(string providerKey) => true;
        public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(AuthorizationProfile);
        public ValueTask<bool> CanAuthorProviderAsync(string providerKey, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> CanReadProviderPayloadAsync(string providerKey, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> CanManageActivityDefinitionsAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class Ids : IIdentityGenerator
    {
        private int _value;
        public string Generate() => (++_value).ToString("D3");
    }

    private sealed class Clock(DateTimeOffset now) : ISystemClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class Authorization(
        string? tenantId = null,
        Func<ActivityDefinitionReference, bool>? canRead = null) : IActivityDependencyContext, IActivityDependencyContextAsync
    {
        public string? TenantId => tenantId;
        public string AuthorizationProfile => "global/manage";
        public bool CanRead(ActivityDefinitionReference reference) =>
            canRead?.Invoke(reference) ?? StringComparer.Ordinal.Equals(reference.TenantId, tenantId);
        public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(AuthorizationProfile);
        public ValueTask<bool> CanReadAsync(ActivityDefinitionReference reference, CancellationToken cancellationToken = default) => ValueTask.FromResult(CanRead(reference));
    }
}
