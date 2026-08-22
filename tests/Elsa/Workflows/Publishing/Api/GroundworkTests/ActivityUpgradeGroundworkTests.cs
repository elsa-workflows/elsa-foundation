using System.Text.Json;
using Elsa.Activities.Design.Api.Contracts;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork;
using ActivityDependencyProjection = Elsa.Activities.Design.Persistence.Groundwork.Services.GroundworkActivityDependencyProjection;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Locking.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.V2.Testing;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.Groundwork;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Services;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Publishing.Api.Commands;
using PublishingUpgradePlanStore = Elsa.Workflows.Publishing.Persistence.Groundwork.Services.GroundworkActivityUpgradePlanStore;
using DurableUpgradePlanStore = Elsa.Activities.Design.Persistence.Groundwork.Services.GroundworkActivityUpgradePlanStore;
using Groundwork.Core.Manifests;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.GroundworkTests;

public sealed class ActivityUpgradeGroundworkTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Apply_commits_activity_workflow_and_durable_result_in_one_batch()
    {
        var harness = await Harness.CreateAsync(workflowExpectedRevision: 1);

        var result = await harness.Subject.ApplyAsync(harness.Plan, harness.Plan.Steps, Receipt(harness.Plan), Now.AddMinutes(1));

        Assert.Equal(ActivityUpgradePlanStatus.Applied, result.Status);
        Assert.Equal([3L, 2L], result.Drafts.Select(x => x.Revision));
        var activity = await harness.LoadActivityDraftAsync();
        var workflow = await harness.LoadWorkflowDraftAsync();
        var storedPlan = await harness.LoadPlanAsync();
        Assert.Equal(3, activity.Revision);
        Assert.Equal("new", workflow.Entity.State.RootActivity!.ActivityVersionId);
        Assert.Equal(2, workflow.EnvelopeVersion);
        Assert.Equal(ActivityUpgradePlanStatus.Applied, storedPlan.Status);
        Assert.Equal(result.Drafts, storedPlan.AppliedDrafts);
        Assert.Equal(result.AppliedAt, storedPlan.AppliedAt);
        var receipt = await harness.LoadReceiptAsync();
        Assert.Equal(ActivityUpgradeApplyReceiptStatus.Applied, receipt.Status);
        Assert.Equal(result.PlanId, receipt.Result!.PlanId);
        Assert.Equal(result.Status, receipt.Result.Status);
        Assert.Equal(result.AppliedAt, receipt.Result.AppliedAt);
        Assert.Equal(result.Drafts, receipt.Result.Drafts);
        Assert.Equal(
            harness.Plan.PlanId,
            (await harness.LoadDocumentJsonAsync(
                ActivitiesDesignStorageManifest.ActivityUpgradePlanDocumentKind,
                harness.Plan.PlanId)).GetProperty("entity").GetProperty("id").GetString());
        Assert.Equal(
            receipt.ReceiptId,
            (await harness.LoadDocumentJsonAsync(
                ActivitiesDesignStorageManifest.ActivityUpgradeApplyReceiptDocumentKind,
                receipt.ReceiptId)).GetProperty("entity").GetProperty("id").GetString());
        var projection = await harness.LoadProjectionAsync();
        Assert.Equal(2, projection.Sequence);
        Assert.All(projection.Items, x => Assert.Equal("new", x.Dependency.VersionId));
        Assert.Equal([3L, 2L], projection.Items.OrderBy(x => x.Owner.Kind, StringComparer.Ordinal).Select(x => x.Owner.Revision));
    }

    [Fact]
    public async Task Stale_workflow_envelope_rejects_without_saving_the_already_staged_activity_edit()
    {
        var harness = await Harness.CreateAsync(workflowExpectedRevision: 99);

        var exception = await Assert.ThrowsAsync<ActivityUpgradeApplyException>(async () =>
            await harness.Subject.ApplyAsync(harness.Plan, harness.Plan.Steps, Receipt(harness.Plan), Now.AddMinutes(1)));

        Assert.Equal("activity.upgrade.stale-plan", exception.ErrorCode);
        Assert.Equal(2, (await harness.LoadActivityDraftAsync()).Revision);
        Assert.Equal("old", (await harness.LoadWorkflowDraftAsync()).Entity.State.RootActivity!.ActivityVersionId);
        Assert.Equal(ActivityUpgradePlanStatus.Ready, (await harness.LoadPlanAsync()).Status);
        var projection = await harness.LoadProjectionAsync();
        Assert.Equal(1, projection.Sequence);
        Assert.All(projection.Items, x => Assert.Equal("old", x.Dependency.VersionId));
    }

    [Fact]
    public async Task Full_rebuild_converges_an_ordinary_workflow_edit_at_a_new_watermark()
    {
        var harness = await Harness.CreateAsync(workflowExpectedRevision: 1);
        var before = await harness.LoadProjectionAsync();

        await harness.ChangeWorkflowDependencyOutsidePublishingAsync("new");

        Assert.All((await harness.LoadProjectionAsync()).Items, x => Assert.Equal("old", x.Dependency.VersionId));
        var rebuild = await harness.RebuildProjectionAsync();
        var after = await harness.LoadProjectionAsync();
        Assert.Equal(before.Sequence + 1, after.Sequence);
        Assert.Equal(rebuild.RebuildId, after.RebuildId);
        Assert.Contains(after.Items, x => x.Owner.Kind == "WorkflowDraft" && x.Dependency.VersionId == "new");
        Assert.Contains(after.Items, x => x.Owner.Kind == "ActivityDraft" && x.Dependency.VersionId == "old");
    }

    [Fact]
    public async Task Full_rebuild_includes_parent_structure_owned_outcome_usage()
    {
        var harness = await Harness.CreateAsync(workflowExpectedRevision: 1);
        var child = new ActivityNode(
            "child",
            "old",
            [ArgumentState.Null("input")],
            [ArgumentState.Null("output")]);
        await harness.ChangeWorkflowStateOutsidePublishingAsync(
            WorkflowDefinitionState.Empty with
            {
                RootActivity = new("container", "native-container", [], [])
            });

        await harness.RebuildProjectionAsync(new ParentUsageStructureService(child));

        var item = Assert.Single((await harness.LoadProjectionAsync()).Items.Where(x =>
            x.Owner.Kind == "WorkflowDraft" && x.Occurrence.OccurrenceId == child.NodeId));
        Assert.Equal(
            [
                new ActivityContractMemberUsage("Input", "input", "Bound"),
                new("Outcome", "approved", "Connected"),
                new("Output", "output", "Bound")
            ],
            item.MemberUsage);
    }

    [Fact]
    public async Task Apply_rejects_selected_closure_lifecycle_drift_before_any_write()
    {
        var harness = await Harness.CreateAsync(workflowExpectedRevision: 1);
        harness.ChangeSelectedVersionLifecycle(ActivityDefinitionVersionLifecycle.Retired);

        var exception = await Assert.ThrowsAsync<ActivityUpgradeApplyException>(async () =>
            await harness.Subject.ApplyAsync(harness.Plan, harness.Plan.Steps, Receipt(harness.Plan), Now.AddMinutes(1)));

        Assert.Equal("activity.upgrade.stale-plan", exception.ErrorCode);
        Assert.Equal(2, (await harness.LoadActivityDraftAsync()).Revision);
        Assert.Equal("old", (await harness.LoadWorkflowDraftAsync()).Entity.State.RootActivity!.ActivityVersionId);
        Assert.Equal(ActivityUpgradeApplyReceiptStatus.Preparing, (await harness.LoadReceiptAsync()).Status);
    }

    [Fact]
    public async Task Dependency_projection_preserves_repeated_diamond_paths_with_bounded_pages()
    {
        var documents = new InMemoryDocumentStore(CombinedManifest());
        var versions = new VersionStore(
            Version("root", ActivityDefinitionVersionLifecycle.Active),
            Version("left", ActivityDefinitionVersionLifecycle.Active),
            Version("right", ActivityDefinitionVersionLifecycle.Active),
            Version("shared", ActivityDefinitionVersionLifecycle.Active),
            Version("leaf", ActivityDefinitionVersionLifecycle.Active));
        var projection = new ActivityDependencyProjection(documents, versions);
        await projection.RebuildAsync(new(
            "diamond",
            1,
            Now,
            [
                Edge("root", "left", "root-left"),
                Edge("root", "right", "root-right"),
                Edge("left", "shared", "left-shared"),
                Edge("right", "shared", "right-shared"),
                Edge("shared", "leaf", "shared-leaf")
            ]));
        var query = new ActivityDependencyQuery(
            ActivityDependencyDirection.Outbound,
            true,
            new HashSet<string>(["Versions"], StringComparer.Ordinal));

        var first = await projection.ReadAsync(new("root", query, null, null, 0, 3));
        var second = await projection.ReadAsync(new("root", query, null, first.Watermark, first.NextOffset!.Value, 10));
        var items = first.Items.Concat(second.Items).ToArray();

        Assert.Equal(2, items.Count(x => x.Occurrence.OccurrenceId == "shared-leaf"));
        Assert.Equal(2, items.Where(x => x.Occurrence.OccurrenceId == "shared-leaf")
            .Select(x => string.Join(">", x.Path.Select(y => y.VersionId)))
            .Distinct(StringComparer.Ordinal)
            .Count());
    }

    [Fact]
    public async Task Expired_receipt_reclaim_race_has_exactly_one_CAS_winner()
    {
        var documents = new InMemoryDocumentStore(CombinedManifest());
        var receipts = new Elsa.Activities.Design.Persistence.Groundwork.Services.GroundworkActivityUpgradePlanStore(documents);
        var receipt = new ActivityUpgradeApplyReceipt(
            "receipt-race",
            "plan",
            "stage",
            "key",
            "request",
            null,
            "access",
            ActivityUpgradeApplyReceiptStatus.Preparing,
            Now.AddMinutes(-5),
            Now.AddMinutes(-5),
            1,
            LeaseExpiresAt: Now.AddMinutes(-1));
        Assert.True(await receipts.TryCreateAsync(receipt));

        var attempts = await Task.WhenAll(
            receipts.TryReclaimAsync(receipt, Now, Now.AddMinutes(2)),
            receipts.TryReclaimAsync(receipt, Now, Now.AddMinutes(3)));

        var winner = Assert.Single(attempts.Where(x => x is not null))!;
        Assert.Equal(2, winner.Revision);
        var persisted = await ((IActivityUpgradeApplyReceiptStore)receipts).FindAsync(receipt.ReceiptId);
        Assert.Equal(winner.ReceiptId, persisted!.ReceiptId);
        Assert.Equal(winner.Revision, persisted.Revision);
        Assert.Equal(winner.LeaseExpiresAt, persisted.LeaseExpiresAt);

        await receipts.RejectAsync(
            receipt,
            409,
            "activity.upgrade.stale-plan",
            [],
            Now.AddSeconds(1));
        persisted = await ((IActivityUpgradeApplyReceiptStore)receipts).FindAsync(receipt.ReceiptId);
        Assert.Equal(ActivityUpgradeApplyReceiptStatus.Preparing, persisted!.Status);
        Assert.Equal(winner.Revision, persisted.Revision);
    }

    [Fact]
    public async Task Planner_generated_stages_rebase_only_committed_snapshots_and_apply_sequentially()
    {
        var harness = await Harness.CreatePlannerGeneratedAsync();
        var original = harness.Plan;
        var stages = original.Stages.OrderBy(x => x.Order).ToArray();
        var applier = harness.CreatePublicApplier();

        var first = await applier.ApplyAsync(new(original.PlanId, stages[0].StageId, "first-stage"));
        var rebased = await harness.LoadPlanAsync();

        Assert.Equal(ActivityUpgradePlanStatus.Ready, first.Status);
        Assert.Equal(original.Replacements, rebased.Replacements);
        Assert.Equal(original.Binding!.Roots, rebased.Binding!.Roots);
        Assert.Equal(original.Binding.IncludeTransitiveDependents, rebased.Binding.IncludeTransitiveDependents);
        Assert.Equal(original.Binding.CreateDraftsForPublishedDependents, rebased.Binding.CreateDraftsForPublishedDependents);
        Assert.Equal(original.Binding.AccessProfileFingerprint, rebased.Binding.AccessProfileFingerprint);
        Assert.Contains(rebased.Binding.SelectedClosure, x =>
            x.Kind == "ActivityDraft" && x.DraftId == "activity-draft" && x.Revision == 3);
        Assert.Contains(rebased.ExpectedSnapshots, x =>
            x.Kind == "ActivityDraft" && x.Id == "activity-draft" && x.Revision == 3);
        Assert.Contains(rebased.Binding.SelectedClosure, x =>
            x.Kind == "WorkflowDraft" && x.DraftId == "workflow-draft" && x.Revision == 1);

        var second = await applier.ApplyAsync(new(original.PlanId, stages[1].StageId, "second-stage"));
        var completed = await harness.LoadPlanAsync();

        Assert.Equal(ActivityUpgradePlanStatus.Applied, second.Status);
        Assert.Equal(2, completed.AppliedDrafts!.Count);
        Assert.Equal([3L, 2L], completed.AppliedDrafts.Select(x => x.Revision));
        Assert.All(completed.Stages, x => Assert.Equal(ActivityUpgradeStageStatus.Applied, x.Status));
    }

    [Fact]
    public async Task Final_recheck_rejects_lifecycle_race_without_mutating_apply_state()
    {
        Harness? harness = null;
        harness = await Harness.CreatePlannerGeneratedAsync(async () =>
        {
            harness!.ChangeSelectedVersionLifecycle(ActivityDefinitionVersionLifecycle.Retired);
            await harness.SaveVersionLifecycleAsync("new");
        });
        var stage = harness.Plan.Stages.OrderBy(x => x.Order).First();
        var beforePlanVersion = await harness.LoadPlanEnvelopeVersionAsync();
        var beforeActivity = await harness.LoadActivityDraftAsync();
        var beforeWorkflow = await harness.LoadWorkflowDraftAsync();
        var beforeProjectionVersion = await harness.LoadProjectionEnvelopeVersionAsync();
        var applier = harness.CreatePublicApplier();

        var exception = await Assert.ThrowsAsync<ActivityUpgradeApplyException>(async () =>
            await applier.ApplyAsync(new(harness.Plan.PlanId, stage.StageId, "lifecycle-race")));

        Assert.Equal("activity.upgrade.stale-plan", exception.ErrorCode);
        Assert.Equal(beforePlanVersion, await harness.LoadPlanEnvelopeVersionAsync());
        Assert.Equal(beforeActivity.Revision, (await harness.LoadActivityDraftAsync()).Revision);
        Assert.Equal(beforeWorkflow.EnvelopeVersion, (await harness.LoadWorkflowDraftAsync()).EnvelopeVersion);
        Assert.Equal(beforeProjectionVersion, await harness.LoadProjectionEnvelopeVersionAsync());
        var receipt = await harness.FindReceiptAsync("lifecycle-race");
        Assert.Equal(ActivityUpgradeApplyReceiptStatus.Preparing, receipt!.Status);
        Assert.Equal(1, receipt.Revision);
        Assert.Null(receipt.Result);
    }

    [Fact]
    public async Task External_drift_between_planner_stages_is_stale_and_writes_no_apply_result()
    {
        var harness = await Harness.CreatePlannerGeneratedAsync();
        var stages = harness.Plan.Stages.OrderBy(x => x.Order).ToArray();
        var applier = harness.CreatePublicApplier();
        await applier.ApplyAsync(new(harness.Plan.PlanId, stages[0].StageId, "first-before-drift"));
        await harness.ChangeWorkflowDependencyOutsidePublishingAsync("new");
        var planVersion = await harness.LoadPlanEnvelopeVersionAsync();
        var workflowVersion = (await harness.LoadWorkflowDraftAsync()).EnvelopeVersion;
        var projectionVersion = await harness.LoadProjectionEnvelopeVersionAsync();

        var exception = await Assert.ThrowsAsync<ActivityUpgradeApplyException>(async () =>
            await applier.ApplyAsync(new(harness.Plan.PlanId, stages[1].StageId, "second-after-drift")));

        Assert.Equal("activity.upgrade.stale-plan", exception.ErrorCode);
        Assert.Equal(planVersion, await harness.LoadPlanEnvelopeVersionAsync());
        Assert.Equal(workflowVersion, (await harness.LoadWorkflowDraftAsync()).EnvelopeVersion);
        Assert.Equal(projectionVersion, await harness.LoadProjectionEnvelopeVersionAsync());
        var receipt = await harness.FindReceiptAsync("second-after-drift");
        Assert.Equal(ActivityUpgradeApplyReceiptStatus.Preparing, receipt!.Status);
        Assert.Equal(1, receipt.Revision);
        Assert.Null(receipt.Result);
    }

    [Fact]
    public async Task Groundwork_feature_resolves_the_public_upgrade_mutation_registration()
    {
        var harness = await Harness.CreateAsync(1);
        var services = new ServiceCollection();
        new PublishingGroundworkFeature().ConfigureServices(services);
        services.AddSingleton(harness.Subject);
        await using var provider = services.BuildServiceProvider();

        Assert.Same(
            harness.Subject,
            provider.GetRequiredService<IActivityUpgradePlanMutationStore>());
    }

    [Fact]
    public async Task Final_recheck_rejects_workflow_head_advance_race()
    {
        Harness? harness = null;
        harness = await Harness.CreatePlannerGeneratedAsync(() =>
        {
            harness!.AdvanceWorkflowHead();
            return Task.CompletedTask;
        });
        var stage = harness.Plan.Stages.OrderBy(x => x.Order).First();
        var planVersion = await harness.LoadPlanEnvelopeVersionAsync();
        var projectionVersion = await harness.LoadProjectionEnvelopeVersionAsync();

        var exception = await Assert.ThrowsAsync<ActivityUpgradeApplyException>(async () =>
            await harness.CreatePublicApplier().ApplyAsync(
                new(harness.Plan.PlanId, stage.StageId, "head-race")));

        Assert.Equal("activity.upgrade.stale-plan", exception.ErrorCode);
        Assert.Equal(planVersion, await harness.LoadPlanEnvelopeVersionAsync());
        Assert.Equal(projectionVersion, await harness.LoadProjectionEnvelopeVersionAsync());
        Assert.Equal(
            ActivityUpgradeApplyReceiptStatus.Preparing,
            (await harness.FindReceiptAsync("head-race"))!.Status);
    }

    private sealed class Harness(
        InMemoryDocumentStore documents,
        JsonSerializerOptions workflowJson,
        IPayloadSerializer payloads,
        VersionStore versions,
        WorkflowVersionStore workflowVersions,
        ActivityDependencyProjection projection,
        PublishingUpgradePlanStore subject,
        ActivityUpgradePlan plan)
    {
        private InMemoryDocumentStore Documents { get; } = documents;
        private JsonSerializerOptions WorkflowJson { get; } = workflowJson;
        private IPayloadSerializer Payloads { get; } = payloads;
        private VersionStore Versions { get; } = versions;
        private WorkflowVersionStore WorkflowVersions { get; } = workflowVersions;
        private ActivityDependencyProjection Projection { get; } = projection;
        public PublishingUpgradePlanStore Subject { get; } = subject;
        public ActivityUpgradePlan Plan { get; } = plan;

        public static async Task<Harness> CreateAsync(
            long workflowExpectedRevision,
            Func<Task>? onTransactionBegin = null)
        {
            var documents = new InMemoryDocumentStore(CombinedManifest());
            IDocumentStore transactionalStore = onTransactionBegin is null
                ? documents
                : new BeginActionStore(documents, onTransactionBegin);
            var payloads = new JsonPayloadSerializer(new JsonPayloadConverterRegistry());
            var workflowJson = GroundworkDesignDocumentSerialization.Create(payloads);
            var from = Version("old", ActivityDefinitionVersionLifecycle.Active);
            var to = Version("new", ActivityDefinitionVersionLifecycle.Active);
            var versions = new VersionStore(from, to);
            var workflowVersions = new WorkflowVersionStore();
            var projection = new ActivityDependencyProjection(documents, versions);
            var managementProjection = new GroundworkActivityManagementProjectionWriter(
                documents,
                new ImmediateLockProvider(),
                documents);
            var plan = CreatePlan(workflowExpectedRevision);
            await SaveActivityAsync(documents, ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionCollection, Definition());
            await SaveActivityAsync(documents, ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection, ActivityDraft());
            await SaveActivityAsync(documents, ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutCollection, ActivityLayout());
            await SaveActivityAsync(documents, ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateCollection, Authoring());
            await SaveActivityAsync(documents, ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationCollection, from);
            await SaveActivityAsync(documents, ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationCollection, to);
            await activityStore.SaveAsync(ToActivitySave(
                ActivitiesDesignStorageManifest.ActivityUpgradePlanDocumentKind,
                plan.PlanId,
                ActivitiesDesignStorageManifest.SchemaVersion,
                new UpgradePlanDocument(ActivitiesDesignStorageManifest.ActivityUpgradePlanCollection, plan),
                GroundworkActivitiesDesignJson.Options));
            await activityStore.SaveAsync(ToActivitySave(
                ActivitiesDesignStorageManifest.ActivityUpgradeApplyReceiptDocumentKind,
                "receipt",
                ActivitiesDesignStorageManifest.SchemaVersion,
                new ApplyReceiptDocument(
                    ActivitiesDesignStorageManifest.ActivityUpgradeApplyReceiptCollection,
                    Receipt(plan)),
                GroundworkActivitiesDesignJson.Options));
            await activityStore.SaveAsync(ToActivitySave(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
                "workflow-draft",
                WorkflowsDesignStorageManifest.SchemaVersion,
                new WorkflowDraftDocument(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftCollection, WorkflowDraft(), []),
                workflowJson));
            await using (var initialManagementProjection = await managementProjection.PrepareAsync(new(
                             Now,
                             [new(Definition(), Authoring())],
                             [ActivityDraft()],
                             [])))
            {
                await initialManagementProjection.CommitAsync([], []);
            }
            await projection.RebuildAsync(new(
                "seed",
                1,
                Now,
                [
                    DependencyItem("ActivityDraft", "definition", "activity-draft", 2, "activity-root", from),
                    DependencyItem("WorkflowDraft", "workflow-definition", "workflow-draft", 1, "workflow-root", from)
                ]));
            var activityState = new DocumentActivityStateStore(documents);
            var subject = new PublishingUpgradePlanStore(
                transactionalStore,
                documents,
                payloads,
                projection,
                projection,
                activityState,
                activityState,
                versions,
                activityState,
                workflowVersions,
                new EmptyWorkflowLayoutStore(),
                new LeafStructureService(),
                new ActivityProviderRegistry([new TestProvider()]),
                new ActivityContractAuthoringValidator(new EmptyCapabilityCatalog()),
                [new TestManifestRewriter()],
                new Ids(),
                managementProjection,
                new ImmediateLockProvider());
            return new(documents, workflowJson, payloads, versions, workflowVersions, projection, subject, plan);
        }

        public static async Task<Harness> CreatePlannerGeneratedAsync(Func<Task>? onTransactionBegin = null)
        {
            var seeded = await CreateAsync(1, onTransactionBegin);
            var durable = new DurableUpgradePlanStore(seeded.Documents);
            var planner = new ActivityUpgradePlanner(
                seeded.Subject,
                durable,
                durable,
                seeded.Subject,
                new CompatibleDiffBuilder(),
                new AllowAllAuthorization(),
                new Ids(),
                new Clock(Now));
            var plan = await planner.PlanAsync(new(
                [new("old", "new")],
                [
                    new("ActivityDraft", "activity-draft"),
                    new("WorkflowDraft", "workflow-draft")
                ],
                false,
                false,
                AccessProfileFingerprint: "access"));
            Assert.Equal(ActivityUpgradePlanStatus.Ready, plan.Status);
            Assert.Equal(2, plan.Stages.Count);
            return new(
                seeded.Documents,
                seeded.WorkflowJson,
                seeded.Payloads,
                seeded.Versions,
                seeded.WorkflowVersions,
                seeded.Projection,
                seeded.Subject,
                plan);
        }

        private static ActivityDependencyItem DependencyItem(
            string kind,
            string definitionId,
            string draftId,
            long revision,
            string occurrenceId,
            ActivityDefinitionVersionPublication target)
        {
            var owner = new ActivityDefinitionReference(kind, definitionId, DraftId: draftId, Revision: revision);
            var dependency = new ActivityDefinitionReference(
                "ActivityVersion", target.DefinitionId, target.DefinitionVersionId, target.Version,
                TemplateHash: target.TemplateHash, Lifecycle: target.Lifecycle);
            return new($"{kind}:{draftId}:{occurrenceId}:{target.DefinitionVersionId}", owner, dependency, new(occurrenceId, []), true, 1, [owner, dependency]);
        }

        public async Task<ActivityDefinitionDraft> LoadActivityDraftAsync()
        {
            var envelope = await activityStore.LoadAsync(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, "activity-draft");
            return JsonSerializer.Deserialize<GroundworkDocument<ActivityDefinitionDraft>>(envelope!.ContentJson, GroundworkActivitiesDesignJson.Options)!.Entity;
        }

        public async Task<(WorkflowDefinitionDraft Entity, long EnvelopeVersion)> LoadWorkflowDraftAsync()
        {
            var envelope = await activityStore.LoadAsync(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind, "workflow-draft");
            var document = JsonSerializer.Deserialize<WorkflowDraftDocument>(envelope!.ContentJson, workflowJson)!;
            return (document.Entity, envelope.Version);
        }

        public async Task<ActivityUpgradePlan> LoadPlanAsync()
        {
            var envelope = await activityStore.LoadAsync(ActivitiesDesignStorageManifest.ActivityUpgradePlanDocumentKind, Plan.PlanId);
            return JsonSerializer.Deserialize<UpgradePlanDocument>(envelope!.ContentJson, GroundworkActivitiesDesignJson.Options)!.Plan;
        }

        public async Task<long> LoadPlanEnvelopeVersionAsync() =>
            (await activityStore.LoadAsync(
                ActivitiesDesignStorageManifest.ActivityUpgradePlanDocumentKind,
                Plan.PlanId))!.Version;

        public async Task<ActivityUpgradeApplyReceipt> LoadReceiptAsync()
        {
            var envelope = await activityStore.LoadAsync(
                ActivitiesDesignStorageManifest.ActivityUpgradeApplyReceiptDocumentKind,
                "receipt");
            return JsonSerializer.Deserialize<ApplyReceiptDocument>(
                envelope!.ContentJson,
                GroundworkActivitiesDesignJson.Options)!.Receipt;
        }

        public async Task<JsonElement> LoadDocumentJsonAsync(string documentKind, string id)
        {
            var envelope = await activityStore.LoadAsync(documentKind, id);
            return JsonDocument.Parse(envelope!.ContentJson).RootElement.Clone();
        }

        public IActivityUpgradePlanApplier CreatePublicApplier()
        {
            var durable = new DurableUpgradePlanStore(documents);
            return new ApplyActivityUpgradePlanCommand(durable, durable, Subject, new Clock(Now.AddMinutes(1)));
        }

        public Task<ActivityUpgradeApplyReceipt?> FindReceiptAsync(string idempotencyKey)
        {
            var durable = new DurableUpgradePlanStore(documents);
            return durable.FindByIdempotencyKeyAsync(
                Plan.PlanId,
                ActivityUpgradeApplyIdentity.HashIdempotencyKey(idempotencyKey));
        }

        public async Task<ActivityDependencyProjectionState> LoadProjectionAsync()
        {
            var envelope = await activityStore.LoadAsync(
                ActivitiesDesignStorageManifest.ActivityDependencyProjectionDocumentKind,
                ActivityDependencyProjectionState.CurrentId);
            return JsonSerializer.Deserialize<GroundworkDocument<ActivityDependencyProjectionState>>(
                envelope!.ContentJson,
                GroundworkActivitiesDesignJson.Options)!.Entity;
        }

        public async Task<long> LoadProjectionEnvelopeVersionAsync() =>
            (await activityStore.LoadAsync(
                ActivitiesDesignStorageManifest.ActivityDependencyProjectionDocumentKind,
                ActivityDependencyProjectionState.CurrentId))!.Version;

        public async Task ChangeWorkflowDependencyOutsidePublishingAsync(string versionId)
        {
            var envelope = await activityStore.LoadAsync(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind, "workflow-draft");
            var document = JsonSerializer.Deserialize<WorkflowDraftDocument>(envelope!.ContentJson, workflowJson)!;
            document.Entity.State = document.Entity.State with { RootActivity = document.Entity.State.RootActivity! with { ActivityVersionId = versionId } };
            var request = JsonDocumentStoreExtensions.ToSaveDocumentRequest(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
                document.Entity.Id,
                WorkflowsDesignStorageManifest.SchemaVersion,
                document,
                workflowJson);
            await documents.SaveAsync(new(request.DocumentKind, request.Id, request.SchemaVersion, request.ContentJson, envelope.Version));
        }

        public async Task ChangeWorkflowStateOutsidePublishingAsync(WorkflowDefinitionState state)
        {
            var envelope = await activityStore.LoadAsync(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind, "workflow-draft");
            var document = JsonSerializer.Deserialize<WorkflowDraftDocument>(envelope!.ContentJson, workflowJson)!;
            document.Entity.State = state;
            var request = JsonDocumentStoreExtensions.ToSaveDocumentRequest(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
                document.Entity.Id,
                WorkflowsDesignStorageManifest.SchemaVersion,
                document,
                workflowJson);
            await documents.SaveAsync(new(request.DocumentKind, request.Id, request.SchemaVersion, request.ContentJson, envelope.Version));
        }

        public Task<ActivityDependencyProjectionRebuild> RebuildProjectionAsync(IActivityStructureService? structureService = null)
        {
            var coordinator = new GroundworkActivityDependencyProjectionRebuildCoordinator(
                documents,
                payloads,
                versions,
                new ActivityTemplateDependencyDiscovererRegistry([new TestDependencyDiscoverer()]),
                structureService ?? new LeafStructureService(),
                projection,
                new Ids(),
                TimeProvider.System);
            return coordinator.RebuildAsync();
        }

        public void ChangeSelectedVersionLifecycle(ActivityDefinitionVersionLifecycle lifecycle) =>
            versions.FindRequired("new").Lifecycle = lifecycle;

        public void AdvanceWorkflowHead() =>
            workflowVersions.Add(new("workflow-definition", "1.0.0")
            {
                Id = "workflow-head",
                State = WorkflowDefinitionState.Empty,
                CreatedAt = Now.AddMinutes(1)
            });

        public async Task SaveVersionLifecycleAsync(string versionId)
        {
            var envelope = await activityStore.LoadAsync(
                ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind,
                versionId);
            var request = GroundworkDocumentWriter.ToSaveRequest(
                ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationCollection,
                ActivitiesDesignStorageManifest.SchemaVersion,
                versions.FindRequired(versionId),
                GroundworkActivitiesDesignJson.Options);
            var result = await documents.SaveAsync(new(
                request.DocumentKind,
                request.Id,
                request.SchemaVersion,
                request.ContentJson,
                envelope!.Version));
            Assert.Equal(DocumentStoreWriteStatus.Saved, result.Status);
        }

        private static ActivityUpgradePlan CreatePlan(long workflowRevision)
        {
            var activity = new ActivityUpgradeStep("activity-step", 10, new("ActivityDraft", "definition", "activity-draft", 2), ActivityUpgradeAction.UpdateDraft, [], [new("activity-root", "old", "new")], 2, "head", null, [], "stage");
            var workflow = new ActivityUpgradeStep("workflow-step", 20, new("WorkflowDraft", "workflow-definition", "workflow-draft", workflowRevision), ActivityUpgradeAction.UpdateDraft, [activity.StepId], [new("workflow-root", "old", "new")], workflowRevision, null, null, [], "stage");
            return new(
                "plan",
                Now,
                Now.AddMinutes(30),
                ActivityUpgradePlanStatus.Ready,
                [new("old", "new")],
                [],
                [activity, workflow],
                [],
                Binding: new(
                    [new("WorkflowDraft", "workflow-draft")],
                    true,
                    false,
                    "access",
                    [
                        new("WorkflowDraft", "workflow-definition", DraftId: "workflow-draft", Revision: workflowRevision),
                        new(
                            "ActivityVersion",
                            "dependency-definition",
                            "old",
                            "1.0.0",
                            TemplateHash: "hash-old",
                            Lifecycle: ActivityDefinitionVersionLifecycle.Active)
                    ]),
                Stages: [new("stage", 10, ActivityUpgradeStageStatus.Ready, ["activity-step", "workflow-step"], [])]);
        }

        private static ActivityDefinitionDraft ActivityDraft() => new()
        {
            Id = "activity-draft", DefinitionId = "definition", Revision = 2,
            State = new(Contract(), new("test.provider", "1", Json("{}")), new Dictionary<string, string>()),
            CreatedAt = Now, LastModifiedAt = Now
        };
        private static ActivityDefinitionDraftLayout ActivityLayout() => new() { Id = "layout", DraftId = "activity-draft", Revision = 2, Records = [], CreatedAt = Now, LastModifiedAt = Now };
        private static ActivityDefinition Definition() => new()
        {
            Id = "definition", ActivityTypeKey = "test.definition", Category = "Tests", DisplayName = "Definition",
            CreatedAt = Now, LastModifiedAt = Now
        };
        private static ActivityDefinitionAuthoringState Authoring() => new() { Id = "authoring", DefinitionId = "definition", HeadVersionId = "head", ContentAuthority = new(ActivityContentAuthorityKind.Design, "design"), CreatedAt = Now, LastModifiedAt = Now };
        private static WorkflowDefinitionDraft WorkflowDraft() => new()
        {
            Id = "workflow-draft", WorkflowDefinitionId = "workflow-definition",
            State = WorkflowDefinitionState.Empty with { RootActivity = new("workflow-root", "old", [], []) },
            CreatedAt = Now, LastModifiedAt = Now
        };
    }

    private static ActivityDefinitionVersionPublication Version(string id, ActivityDefinitionVersionLifecycle lifecycle) => new()
    {
        Id = id, DefinitionVersionId = id, DefinitionId = "dependency-definition", Version = id == "old" ? "1.0.0" : "2.0.0",
        ActivityTypeKey = "dependency", Contract = Contract(), Provider = new("test.provider", "1", Json("{}")),
        TemplateId = $"template-{id}", TemplateHash = $"hash-{id}", SourceReferenceId = $"source-{id}", ProviderFingerprint = "provider",
        DirectDependencyCount = 0, ClosedTemplateCount = 1, RuntimeRequirements = [], Lifecycle = lifecycle, PublishedAt = Now
    };
    private static ActivityContract Contract() => new("1", [], [], []);
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static ActivityDependencyItem Edge(string ownerVersionId, string dependencyVersionId, string occurrenceId)
    {
        var owner = new ActivityDefinitionReference("ActivityVersion", "dependency-definition", ownerVersionId);
        var dependency = new ActivityDefinitionReference("ActivityVersion", "dependency-definition", dependencyVersionId);
        return new(
            $"{ownerVersionId}:{occurrenceId}:{dependencyVersionId}",
            owner,
            dependency,
            new(occurrenceId, []),
            true,
            1,
            [owner, dependency]);
    }

    private static async Task SaveActivityAsync<TEntity>(
        GroundworkV2ActivityDesignStore store,
        string kind,
        string collection,
        TEntity entity) where TEntity : Elsa.Primitives.Entities.Entity =>
        await store.SaveAsync(GroundworkV2ActivityDesignDocumentWriter.ToSaveRequest(
            kind, collection, ActivitiesDesignStorageManifest.SchemaVersion, entity, GroundworkActivitiesDesignJson.Options));

    /// <summary>
    /// An upgrade apply spans both design catalogs and commits through the publishing lane's transaction,
    /// so all three are declared against one provider, as a single-target host has them.
    /// </summary>
    private static GroundworkV2TestPersistence CreateCatalog() => GroundworkV2TestPersistence.Create(
        "memory",
        ActivitiesDesignStorageManifest.CreateUnits(),
        WorkflowsDesignStorageManifest.CreateUnits(),
        PublishingGroundworkStorageManifest.CreateUnits());

    private sealed record UpgradePlanDocument(string Collection, ActivityUpgradePlan Plan);
    private sealed record ApplyReceiptDocument(string Collection, ActivityUpgradeApplyReceipt Receipt);
    private sealed record WorkflowDraftDocument(string Collection, WorkflowDefinitionDraft Entity, IReadOnlyCollection<DesignMetadataRecord> Layout);

    private static ActivityUpgradeApplyReceipt Receipt(ActivityUpgradePlan plan) => new(
        "receipt",
        plan.PlanId,
        "stage",
        "key-hash",
        "request-fingerprint",
        plan.TenantId,
        plan.Binding!.AccessProfileFingerprint,
        ActivityUpgradeApplyReceiptStatus.Preparing,
        Now,
        Now,
        1);
    private sealed class TestManifestRewriter : IActivityProviderReferenceRewriter
    {
        public string ProviderKey => "test.provider";
        public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string> { "1" };
        public ValueTask<ActivityProviderManifest> RewriteReferencesAsync(ActivityProviderManifest manifest, IReadOnlyList<ActivityUpgradeOccurrenceReplacement> replacements, CancellationToken cancellationToken = default) => ValueTask.FromResult(manifest);
    }
    private sealed class TestProvider : IActivityProvider
    {
        public string ProviderKey => "test.provider";
        public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string> { "1" };
        public ActivityProviderAuthoringCapabilities AuthoringCapabilities { get; } = new("Test", [new("1", true, new HashSet<string> { "1" })], new([]));
        public ValueTask<ActivityContractProposal> ProposeContractAsync(ActivityProviderContractProposalRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(new ActivityContractProposal([], []));
        public ValueTask<IReadOnlyList<ActivityDiagnostic>> ValidateAsync(ActivityProviderManifest manifest, ActivityContract contract, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<ActivityDiagnostic>>([]);
        public ValueTask<ActivityManifestMigration> MigrateAsync(ActivityManifestMigrationRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(new ActivityManifestMigration(request.Source, []));
    }
    private sealed class EmptyCapabilityCatalog : IActivityContractCapabilityCatalog
    {
        public IReadOnlyCollection<ActivityContractTypeCapability> Types => [];
    }
    private sealed class TestDependencyDiscoverer : IActivityTemplateDependencyDiscoverer
    {
        public string ProviderKey => "test.provider";
        public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string> { "1" };
        public ValueTask<ActivityTemplateDependencyDiscovery> DiscoverDependenciesAsync(ActivityTemplateDependencyDiscoveryRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityTemplateDependencyDiscovery([new("old", "activity-root", [])], []));
    }
    private sealed class LeafStructureService : IActivityStructureService
    {
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) => [];
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => activity;
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => activity.Structure;
        public IReadOnlyCollection<Elsa.Expressions.Core.Models.VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
        public bool SupportsScopedVariables(ActivityNode activity) => false;
    }

    private sealed class ParentUsageStructureService(ActivityNode child) : IActivityStructureService
    {
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) =>
            activity.NodeId == "container" ? [new("children", [child])] : [];
        public IReadOnlyCollection<ActivityChildContractMemberUsage> ProjectChildContractMemberUsage(ActivityNode activity) =>
            activity.NodeId == "container"
                ? [new(child.NodeId, [new("Outcome", "approved", "Connected")])]
                : [];
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => activity;
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => activity.Structure;
        public IReadOnlyCollection<Elsa.Expressions.Core.Models.VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
        public bool SupportsScopedVariables(ActivityNode activity) => false;
    }
    private sealed class VersionStore(params ActivityDefinitionVersionPublication[] versions) : IActivityDefinitionVersionPublicationStore
    {
        public ActivityDefinitionVersionPublication FindRequired(string id) =>
            versions.Single(x => StringComparer.Ordinal.Equals(x.DefinitionVersionId, id));
        public Task<ActivityDefinitionVersionPublication?> FindAsync(string definitionVersionId, CancellationToken cancellationToken = default) => Task.FromResult(versions.SingleOrDefault(x => x.DefinitionVersionId == definitionVersionId));
        public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionVersionPublication>>(versions.Where(x => x.DefinitionId == definitionId).ToArray());
    }
    private sealed class DocumentActivityStateStore(InMemoryDocumentStore documents) :
        IActivityDefinitionDraftStore,
        IActivityDefinitionAuthoringStore,
        IActivityDefinitionLayoutStore
    {
        public async Task<ActivityDefinitionDraft?> FindAsync(string draftId, CancellationToken cancellationToken = default) =>
            await LoadAsync<ActivityDefinitionDraft>(
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
                draftId,
                cancellationToken);

        public Task<IReadOnlyList<ActivityDefinitionDraft>> ListByDefinitionAsync(
            string definitionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionDraft>>([]);

        async Task<ActivityDefinitionAuthoringState?> IActivityDefinitionAuthoringStore.FindAsync(
            string definitionId,
            CancellationToken cancellationToken) =>
            await LoadAsync<ActivityDefinitionAuthoringState>(
                ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
                "authoring",
                cancellationToken);

        public Task<IReadOnlyList<ActivityDefinitionAuthoringState>> ListAsync(
            IEnumerable<string> definitionIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionAuthoringState>>([]);

        public async Task<ActivityDefinitionDraftLayout?> FindDraftLayoutAsync(
            string draftId,
            CancellationToken cancellationToken = default) =>
            await LoadAsync<ActivityDefinitionDraftLayout>(
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind,
                "layout",
                cancellationToken);

        public Task<ActivityDefinitionVersionLayout?> FindVersionLayoutAsync(
            string definitionVersionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ActivityDefinitionVersionLayout?>(null);

        private async Task<TEntity?> LoadAsync<TEntity>(
            string kind,
            string id,
            CancellationToken cancellationToken)
            where TEntity : Elsa.Primitives.Entities.Entity
        {
            var envelope = await activityStore.LoadAsync(kind, id, cancellationToken);
            return envelope is null
                ? default
                : JsonSerializer.Deserialize<GroundworkDocument<TEntity>>(
                    envelope.ContentJson,
                    GroundworkActivitiesDesignJson.Options)!.Entity;
        }
    }
    private sealed class CompatibleDiffBuilder : IActivityUpgradeDiffBuilder
    {
        public ValueTask<ActivityVersionDiff?> BuildAsync(
            ActivityUpgradeOwnerSnapshot owner,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ActivityVersionDiff?>(
                owner.Owner.Kind.StartsWith("Activity", StringComparison.Ordinal)
                    ? new(
                        new("ActivityVersion", owner.Owner.DefinitionId, "old"),
                        new("ActivityDraft", owner.Owner.DefinitionId, DraftId: owner.Owner.DraftId, Revision: owner.Owner.Revision),
                        ActivityVersionCompatibility.Compatible,
                        ActivityVersionBump.Minor,
                        true,
                        new("graph", "1", "graph", "1", false),
                        new(0, 1, 0, 0),
                        [],
                        [])
                    : null);
    }
    private sealed class AllowAllAuthorization : IActivityDependencyAuthorizationContext, IActivityDependencyContextAsync
    {
        public string? TenantId => null;
        public string AuthorizationProfile => "access";
        public bool CanRead(ActivityDefinitionReference reference) => reference.TenantId is null;
        public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(AuthorizationProfile);
        public ValueTask<bool> CanReadAsync(ActivityDefinitionReference reference, CancellationToken cancellationToken = default) => ValueTask.FromResult(CanRead(reference));
    }
    private sealed class Clock(DateTimeOffset now) : ISystemClock
    {
        public DateTimeOffset UtcNow => now;
    }
    private sealed class BeginActionStore(InMemoryDocumentStore inner, Func<Task> action) : IDocumentStore
    {
        private int _injected;
        public DocumentStoreAccess Access => inner.Access;
        public TransactionBoundary TransactionBoundary => inner.TransactionBoundary;
        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) => inner.SaveAsync(request, cancellationToken);
        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) => inner.LoadAsync(documentKind, id, cancellationToken);
        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) => inner.DeleteAsync(request, cancellationToken);
        #pragma warning disable GW0004 // Required IDocumentStore compatibility members; the portable query surface is retired but still abstract.
        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) => inner.QueryAsync(query, cancellationToken);
        public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) => inner.QueryAsync(query, cancellationToken);
        public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) => inner.FirstOrDefaultAsync(query, cancellationToken);
        public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) => inner.AnyAsync(query, cancellationToken);
        #pragma warning restore GW0004

        public async Task<IDocumentUnitOfWork> BeginAsync(
            DocumentCommitScope scope,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _injected, 1) == 0)
                await action();
            return await inner.BeginAsync(scope, cancellationToken);
        }
    }
    private sealed class EmptyDraftStore : IActivityDefinitionDraftStore
    {
        public Task<ActivityDefinitionDraft?> FindAsync(string draftId, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinitionDraft?>(null);
        public Task<IReadOnlyList<ActivityDefinitionDraft>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionDraft>>([]);
    }
    private sealed class EmptyAuthoringStore : IActivityDefinitionAuthoringStore
    {
        public Task<ActivityDefinitionAuthoringState?> FindAsync(string definitionId, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinitionAuthoringState?>(null);
        public Task<IReadOnlyList<ActivityDefinitionAuthoringState>> ListAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionAuthoringState>>([]);
    }
    private sealed class EmptyActivityLayoutStore : IActivityDefinitionLayoutStore
    {
        public Task<ActivityDefinitionDraftLayout?> FindDraftLayoutAsync(string draftId, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinitionDraftLayout?>(null);
        public Task<ActivityDefinitionVersionLayout?> FindVersionLayoutAsync(string definitionVersionId, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinitionVersionLayout?>(null);
    }
    private sealed class EmptyWorkflowVersionStore : IWorkflowDefinitionVersionStore
    {
        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowDefinitionVersion?>(null);
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowDefinitionVersion?>(null);
        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkflowDefinitionVersion>>([]);
        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
    private sealed class WorkflowVersionStore : IWorkflowDefinitionVersionStore
    {
        private readonly List<WorkflowDefinitionVersion> _versions = [];
        public void Add(WorkflowDefinitionVersion version) => _versions.Add(version);
        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_versions.Single(x => x.Id == versionId));
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_versions.SingleOrDefault(x => x.Id == versionId));
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            GetAsync(versionId, cancellationToken);
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_versions
                .Where(x => x.DefinitionId == definitionId)
                .OrderByDescending(x => x.SemVerSortKey, StringComparer.Ordinal)
                .FirstOrDefault());
        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinitionVersion>>(
                _versions.Where(x => x.DefinitionId == definitionId).ToArray());
        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(_versions.Any(x => x.DefinitionId == definitionId && x.SemVerSortKey == semVerSortKey));
    }
    private sealed class EmptyWorkflowLayoutStore : IWorkflowDefinitionVersionLayoutStore
    {
        public Task<WorkflowDefinitionVersionLayout?> FindByVersionIdAsync(string workflowDefinitionVersionId, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowDefinitionVersionLayout?>(null);
    }
    private sealed class Ids : IIdentityGenerator
    {
        private int _id;
        public string Generate() => $"generated-{++_id}";
    }

    private sealed class ImmediateLockProvider : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new Handle();
        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());
        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());

        private sealed class Handle : IDistributedSynchronizationHandle
        {
            public CancellationToken HandleLostToken => CancellationToken.None;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
