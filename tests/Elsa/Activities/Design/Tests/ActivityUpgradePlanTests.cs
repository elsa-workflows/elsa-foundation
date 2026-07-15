using System.Text.Json;
using Elsa.Activities.Design.Api.Commands;
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
        Assert.Same(plan, await store.FindAsync(plan.PlanId));
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

        Assert.Equal(ActivityUpgradePlanStatus.Blocked, plan.Status);
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

        Assert.Equal(["/a", "/z"], plan.Diagnostics.Select(x => x.Location!.JsonPointer));
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
            [ownerReference, newReference],
            DiffCandidate: request);
        var builder = new ActivityUpgradeDiffBuilder(
            new CandidateCompiler(candidate),
            new ActivityVersionDiffer(),
            publications,
            new DependencyStore(new ActivityDependencyEdge
            {
                Id = "edge", OwnerVersionId = "base", OwnerTemplateHash = "hash-base", DependencyVersionId = "old",
                DependencyTemplateHash = "hash-old", OccurrenceId = "node", NodeOrigin = []
            }),
            new LayoutStore());

        var diff = await builder.BuildAsync(snapshot);

        Assert.NotNull(diff);
        Assert.Equal(ActivityVersionCompatibility.Compatible, diff.Compatibility);
        Assert.Equal(ActivityVersionBump.Minor, diff.RequiredBump);
        Assert.Contains(diff.Changes, x => x.Kind == "DependencyVersionChanged" && x.Subject.OccurrenceId == "node");
    }

    [Fact]
    public async Task Apply_rejects_a_selection_that_is_not_dependency_closed_before_mutation()
    {
        var plan = ReadyPlan();
        var plans = new PlanStore(plan);
        var mutations = new RecordingMutationStore();
        var command = new ApplyActivityUpgradePlanCommand(plans, mutations, new Clock(Now));

        var exception = await Assert.ThrowsAsync<ActivityUpgradeApplyException>(async () =>
            await command.ApplyAsync(new(plan.PlanId, ["step-parent"])));

        Assert.Equal(422, exception.StatusCode);
        Assert.Equal("activity.upgrade.selection-not-closed", exception.ErrorCode);
        Assert.Equal(0, mutations.Calls);
    }

    [Fact]
    public async Task Apply_passes_one_bottom_up_atomic_batch_and_never_mutates_published_material()
    {
        var plan = ReadyPlan();
        var published = new Dictionary<string, string>(StringComparer.Ordinal) { ["activity-v1"] = "immutable", ["workflow-v1"] = "immutable" };
        var mutations = new RecordingMutationStore(published);
        var command = new ApplyActivityUpgradePlanCommand(new PlanStore(plan), mutations, new Clock(Now));

        var result = await command.ApplyAsync(new(plan.PlanId));

        Assert.Equal(ActivityUpgradePlanStatus.Applied, result.Status);
        Assert.Equal(["step-child", "step-parent"], mutations.SelectedStepIds);
        Assert.Equal("immutable", published["activity-v1"]);
        Assert.Equal("immutable", published["workflow-v1"]);
    }

    [Fact]
    public async Task Apply_rejects_an_explicit_empty_selection_instead_of_expanding_it_to_all_steps()
    {
        var mutations = new RecordingMutationStore();
        var plan = ReadyPlan();
        var command = new ApplyActivityUpgradePlanCommand(new PlanStore(plan), mutations, new Clock(Now));

        var exception = await Assert.ThrowsAsync<ActivityUpgradeApplyException>(async () =>
            await command.ApplyAsync(new(plan.PlanId, [])));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("activity.request.invalid", exception.ErrorCode);
        Assert.Equal(0, mutations.Calls);
    }

    [Fact]
    public async Task Apply_handler_exposes_an_explicit_empty_selection_as_the_canonical_request_error()
    {
        var plan = ReadyPlan();
        var store = new PlanStore(plan);
        var handler = new ApplyActivityUpgradePlanHandler(
            new ApplyActivityUpgradePlanCommand(store, new RecordingMutationStore(), new Clock(Now)),
            store,
            new AuthoringContext(null));

        var exception = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            handler.Handle(new(plan.PlanId, []), default));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("activity.request.invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task Stale_apply_reports_conflict_and_commits_no_partial_draft_edits()
    {
        var plan = ReadyPlan();
        var mutations = new RecordingMutationStore { FailStale = true };
        var command = new ApplyActivityUpgradePlanCommand(new PlanStore(plan), mutations, new Clock(Now));

        var exception = await Assert.ThrowsAsync<ActivityUpgradeApplyException>(async () => await command.ApplyAsync(new(plan.PlanId)));

        Assert.Equal(409, exception.StatusCode);
        Assert.Equal("activity.upgrade.stale-plan", exception.ErrorCode);
        Assert.Empty(mutations.Drafts);
    }

    [Fact]
    public async Task Plan_expiry_is_checked_before_any_mutation()
    {
        var plan = ReadyPlan() with { ExpiresAt = Now };
        var mutations = new RecordingMutationStore();
        var command = new ApplyActivityUpgradePlanCommand(new PlanStore(plan), mutations, new Clock(Now));

        var exception = await Assert.ThrowsAsync<ActivityUpgradeApplyException>(async () => await command.ApplyAsync(new(plan.PlanId)));

        Assert.Equal(410, exception.StatusCode);
        Assert.Equal(0, mutations.Calls);
    }

    [Fact]
    public async Task Repeated_apply_returns_the_durable_identical_apply_result_without_mutating_again()
    {
        var appliedAt = Now.AddMinutes(-2);
        var drafts = new[] { new ActivityUpgradeAppliedDraft("WorkflowDraft", "draft-w", "workflow", 6, false) };
        var plan = ReadyPlan() with { Status = ActivityUpgradePlanStatus.Applied, AppliedAt = appliedAt, AppliedDrafts = drafts };
        var mutations = new RecordingMutationStore();
        var command = new ApplyActivityUpgradePlanCommand(new PlanStore(plan), mutations, new Clock(Now));

        var result = await command.ApplyAsync(new(plan.PlanId));

        Assert.Equal(appliedAt, result.AppliedAt);
        Assert.Equal(drafts, result.Drafts);
        Assert.Equal(0, mutations.Calls);
    }

    [Fact]
    public async Task Planner_binds_the_plan_to_the_internal_tenant_scope()
    {
        var request = Request() with { TenantId = "tenant-a" };

        var plan = await Planner(new([], []), new PlanStore()).PlanAsync(request);

        Assert.Equal("tenant-a", plan.TenantId);
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
            new ApplyActivityUpgradePlanHandler(applier, store, context).Handle(new(plan.PlanId), default));

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
        IActivityUpgradeDiffBuilder? diffBuilder = null) =>
        new(new Discovery(discovery), store, diffBuilder ?? new DiffBuilder(), new Ids(), new Clock(Now));

    private static ActivityUpgradePlanRequest Request() => new(
        [new("old", "new")],
        [new("WorkflowDraft", "draft-parent")],
        true,
        false);

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
        path);

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
        new("ActivityDraft", "draft-a"),
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

    private static ActivityUpgradePlan ReadyPlan()
    {
        var child = new ActivityUpgradeStep(
            "step-child", 10, new("ActivityDraft", "activity", "draft-a", 3), ActivityUpgradeAction.UpdateDraft,
            [], [new("node-a", "old", "new")], 3, "activity-head", null, []);
        var parent = new ActivityUpgradeStep(
            "step-parent", 20, new("WorkflowDraft", "workflow", "draft-w", 5), ActivityUpgradeAction.UpdateDraft,
            [child.StepId], [new("node-w", "old", "new")], 5, "workflow-head", null, []);
        return new(
            "plan-1", Now.AddMinutes(-1), Now.AddMinutes(29), ActivityUpgradePlanStatus.Ready,
            [new("old", "new")],
            [new("ActivityDraft", "draft-a", 3, "activity", "activity-head"), new("WorkflowDraft", "draft-w", 5, "workflow", "workflow-head")],
            [child, parent], []);
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
        private ActivityUpgradePlan? _plan;
        public PlanStore(ActivityUpgradePlan? plan = null) => _plan = plan;
        public Task<ActivityUpgradePlan?> FindAsync(string planId, CancellationToken cancellationToken = default) => Task.FromResult(_plan?.PlanId == planId ? _plan : null);
        public Task SaveAsync(ActivityUpgradePlan plan, CancellationToken cancellationToken = default) { _plan = plan; return Task.CompletedTask; }
    }

    private sealed class RecordingMutationStore(IReadOnlyDictionary<string, string>? published = null) : IActivityUpgradePlanMutationStore
    {
        public int Calls { get; private set; }
        public bool FailStale { get; init; }
        public List<string> Drafts { get; } = [];
        public IReadOnlyList<string> SelectedStepIds { get; private set; } = [];

        public ValueTask<ActivityUpgradeApplyResult> ApplyAsync(ActivityUpgradePlan plan, IReadOnlyList<ActivityUpgradeStep> selectedSteps, DateTimeOffset appliedAt, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (FailStale)
                throw new ActivityUpgradeApplyException(409, "activity.upgrade.stale-plan", "stale");
            SelectedStepIds = selectedSteps.Select(x => x.StepId).ToArray();
            Drafts.AddRange(selectedSteps.Select(x => x.Target.DraftId!));
            Assert.All(published ?? new Dictionary<string, string>(), x => Assert.Equal("immutable", x.Value));
            return ValueTask.FromResult(new ActivityUpgradeApplyResult(plan.PlanId, ActivityUpgradePlanStatus.Applied, appliedAt, [], []));
        }
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

    private sealed class AuthoringContext(string? tenantId) : IActivityAuthoringContext
    {
        public string? TenantId { get; } = tenantId;
        public bool CanAuthorProvider(string providerKey) => true;
        public bool CanReadProviderPayload(string providerKey) => true;
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
}
