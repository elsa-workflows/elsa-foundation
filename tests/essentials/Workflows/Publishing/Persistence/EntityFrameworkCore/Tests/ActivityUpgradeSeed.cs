using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using static Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests.ActivityUpgradeFixtures;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// The durable state an upgrade plans and applies against: one Design-owned activity draft, one workflow
/// draft, the two dependency publications the plan replaces between, and the derived projection that
/// binds them. Rows are written through the contexts so every hashed identity and partition column is
/// produced by the production model, not by the test.
/// </summary>
internal static class ActivityUpgradeSeed
{
    public const string ActivityHeadVersionId = "activity-head";

    /// <summary>The revision the seeded workflow draft's persisted <c>LastModifiedAt</c> represents.</summary>
    public static long WorkflowRevision => Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Services.EfWorkflowDraftRevision.Of(Now);

    public static async Task BaseGraphAsync(ActivityUpgradeContexts contexts, IPayloadSerializer payloads, string tenantId = Tenant)
    {
        await using var activities = contexts.Activities([]);
        await using var workflows = contexts.Workflows([]);

        var definition = new ActivityDefinition
        {
            Id = ActivityDefinitionId, TenantId = tenantId, ActivityTypeKey = "test.activity", Category = "Tests",
            DisplayName = "Upgradeable activity", CreatedAt = Now, LastModifiedAt = Now
        };
        var authoring = Authoring(ActivityDefinitionId, ActivityHeadVersionId, tenantId);
        var draft = new ActivityDefinitionDraft
        {
            Id = ActivityDraftId, TenantId = tenantId, DefinitionId = ActivityDefinitionId, Revision = 2,
            State = new(Contract(), Provider(), new Dictionary<string, string>()), CreatedAt = Now, LastModifiedAt = Now
        };
        activities.ActivityDefinitions.Add(definition);
        activities.ActivityDefinitionAuthoringStates.Add(authoring);
        activities.ActivityDefinitionDrafts.Add(draft);
        activities.ActivityDefinitionDraftLayouts.Add(new ActivityDefinitionDraftLayout
        {
            Id = "activity-draft-layout", TenantId = tenantId, DraftId = ActivityDraftId, Revision = 2,
            Records = [], CreatedAt = Now, LastModifiedAt = Now
        });
        var from = Publication(OldVersionId, "1.0.0", ActivityDefinitionVersionLifecycle.Active, tenantId);
        var to = Publication(NewVersionId, "2.0.0", ActivityDefinitionVersionLifecycle.Active, tenantId);
        activities.ActivityDefinitionVersionPublications.Add(from);
        activities.ActivityDefinitionVersionPublications.Add(to);
        // The draft's definition has a published head of its own; the management projection resolves it.
        activities.ActivityDefinitionVersionPublications.Add(
            Publication(ActivityHeadVersionId, "1.0.0", ActivityDefinitionVersionLifecycle.Active, tenantId, ActivityDefinitionId));
        activities.ActivityDependencyProjections.Add(new ActivityDependencyProjectionState
        {
            Id = ActivityDependencyProjectionState.CurrentId,
            RebuildId = "seed",
            Sequence = 1,
            AsOf = Now,
            Items =
            [
                DependencyItem("ActivityDraft", ActivityDefinitionId, ActivityDraftId, 2, ActivityOccurrenceId, from, tenantId),
                DependencyItem("WorkflowDraft", WorkflowDefinitionId, WorkflowDraftId, WorkflowRevision, WorkflowOccurrenceId, from, tenantId)
            ]
        });
        await activities.SaveChangesAsync();
        // The management projection is the Activities Design read model an upgraded draft checkpoints into;
        // its writer refuses a child whose definition has no current revision, so the base graph seeds one.
        await new EfActivityManagementProjectionWriter(activities, TestAccess.Scoped(tenantId))
            .WriteAsync(new(Now, [new(definition, authoring)], [draft], []));

        workflows.Definitions.Add(new WorkflowDefinition
        {
            Id = WorkflowDefinitionId, TenantId = tenantId, Name = "Upgradeable workflow", CreatedAt = Now, LastModifiedAt = Now
        });
        workflows.Drafts.Add(new WorkflowDefinitionDraft
        {
            Id = WorkflowDraftId,
            TenantId = tenantId,
            WorkflowDefinitionId = WorkflowDefinitionId,
            StateSource = payloads.Serialize(WorkflowState(OldVersionId)),
            CreatedAt = Now,
            LastModifiedAt = Now
        });
        await workflows.SaveChangesAsync();
    }

    /// <summary>Adds the published A -> B chain the reusable-activity upgrade journey is rooted at.</summary>
    public static async Task PublishedChainAsync(ActivityUpgradeContexts contexts, string tenantId = Tenant)
    {
        await using var activities = contexts.Activities([]);
        var projected = new List<EfActivityManagementDefinitionChange>();
        foreach (var (definitionId, versionId) in new[] { ("b-definition", "b-v1"), ("a-definition", "a-v1") })
        {
            var definition = new ActivityDefinition
            {
                Id = definitionId, TenantId = tenantId, ActivityTypeKey = $"test.{definitionId}", Category = "Tests",
                DisplayName = definitionId, CreatedAt = Now, LastModifiedAt = Now
            };
            var authoring = Authoring(definitionId, versionId, tenantId);
            activities.ActivityDefinitions.Add(definition);
            activities.ActivityDefinitionAuthoringStates.Add(authoring);
            projected.Add(new(definition, authoring));
            activities.ActivityDefinitionVersionPublications.Add(new ActivityDefinitionVersionPublication
            {
                Id = versionId,
                TenantId = tenantId,
                DefinitionVersionId = versionId,
                DefinitionId = definitionId,
                Version = "1.0.0",
                ActivityTypeKey = $"test.{definitionId}",
                ResolutionKind = ActivityDefinitionVersionResolutionKind.ReusableTemplateBoundary,
                SourceDraftId = $"source-draft-{versionId}",
                Contract = Contract(),
                Provider = Provider(),
                TemplateId = $"template-{versionId}",
                TemplateHash = $"hash-{versionId}",
                SourceReferenceId = $"source-{versionId}",
                ProviderFingerprint = "provider",
                DirectDependencyCount = 1,
                ClosedTemplateCount = 1,
                RuntimeRequirements = [],
                ResumeTargetCount = 0,
                Lifecycle = ActivityDefinitionVersionLifecycle.Active,
                PublishedAt = Now,
                CreatedAt = Now,
                LastModifiedAt = Now
            });
        }

        await activities.SaveChangesAsync();
        var projection = await activities.ActivityDependencyProjections.SingleAsync(x => x.Id == ActivityDependencyProjectionState.CurrentId);
        var oldVersion = await activities.ActivityDefinitionVersionPublications.SingleAsync(x => x.DefinitionVersionId == OldVersionId);
        var b = await activities.ActivityDefinitionVersionPublications.SingleAsync(x => x.DefinitionVersionId == "b-v1");
        var a = await activities.ActivityDefinitionVersionPublications.SingleAsync(x => x.DefinitionVersionId == "a-v1");
        projection.Items =
        [
            .. projection.Items,
            VersionDependencyItem(b, "b-root", oldVersion, tenantId),
            VersionDependencyItem(a, "a-root", b, tenantId)
        ];
        await activities.SaveChangesAsync();
        await new EfActivityManagementProjectionWriter(activities, TestAccess.Scoped(tenantId))
            .WriteAsync(new(Now, projected, [], []));
    }

    public static ActivityDefinitionAuthoringState Authoring(string definitionId, string? headVersionId, string? tenantId) => new()
    {
        Id = $"authoring-{definitionId}",
        TenantId = tenantId,
        DefinitionId = definitionId,
        HeadVersionId = headVersionId,
        ContentAuthority = new(ActivityContentAuthorityKind.Design, "elsa.design"),
        CreatedAt = Now,
        LastModifiedAt = Now
    };

    /// <summary>
    /// The two-step plan: one activity draft and one workflow draft in one stage,
    /// bound to the workflow-draft root whose closure discovery reproduces exactly. The workflow step's
    /// expected revision can be moved on its own so the apply reaches the compare-and-swap with drift, after
    /// the activity lane has already written.
    /// </summary>
    public static ActivityUpgradePlan TwoStepPlan(
        long workflowRevision,
        long? workflowStepRevision = null,
        string planId = "plan",
        string? tenantId = Tenant)
    {
        var stepRevision = workflowStepRevision ?? workflowRevision;
        var activity = new ActivityUpgradeStep(
            "activity-step", 10,
            new("ActivityDraft", ActivityDefinitionId, ActivityDraftId, 2),
            ActivityUpgradeAction.UpdateDraft, [],
            [new(ActivityOccurrenceId, OldVersionId, NewVersionId)],
            2, ActivityHeadVersionId, null, [], "stage");
        var workflow = new ActivityUpgradeStep(
            "workflow-step", 20,
            new("WorkflowDraft", WorkflowDefinitionId, WorkflowDraftId, stepRevision),
            ActivityUpgradeAction.UpdateDraft, [activity.StepId],
            [new(WorkflowOccurrenceId, OldVersionId, NewVersionId)],
            stepRevision, null, null, [], "stage");
        return new(
            planId,
            Now,
            Now.AddMinutes(30),
            ActivityUpgradePlanStatus.Ready,
            [new(OldVersionId, NewVersionId)],
            [],
            [activity, workflow],
            [],
            TenantId: tenantId,
            Binding: new(
                [new("WorkflowDraft", WorkflowDraftId)],
                true,
                false,
                "access",
                [
                    new("WorkflowDraft", WorkflowDefinitionId, DraftId: WorkflowDraftId, Revision: workflowRevision, TenantId: tenantId),
                    new("ActivityVersion", DependencyDefinitionId, OldVersionId, "1.0.0", TemplateHash: $"hash-{OldVersionId}", TenantId: tenantId, Lifecycle: ActivityDefinitionVersionLifecycle.Active)
                ]),
            Stages: [new("stage", 10, ActivityUpgradeStageStatus.Ready, ["activity-step", "workflow-step"], [])]);
    }

    public static ActivityUpgradeApplyReceipt Receipt(ActivityUpgradePlan plan, string receiptId = "receipt") => new(
        receiptId,
        plan.PlanId,
        "stage",
        "key-hash",
        "request-fingerprint",
        plan.TenantId,
        plan.Binding!.AccessProfileFingerprint,
        ActivityUpgradeApplyReceiptStatus.Preparing,
        Now,
        Now,
        1,
        LeaseExpiresAt: Now.AddMinutes(2));

    public static async Task PersistAsync(ActivityUpgradeContexts contexts, ActivityUpgradePlan plan, ActivityUpgradeApplyReceipt? receipt = null, string tenantId = Tenant)
    {
        await using var activities = contexts.Activities([]);
        var stores = new EfActivityDesignStores(activities, TestAccess.Scoped(tenantId));
        await ((IActivityUpgradePlanStore)stores).SaveAsync(plan);
        if (receipt is not null)
            await ((IActivityUpgradeApplyReceiptStore)stores).TryCreateAsync(receipt);
    }
}
