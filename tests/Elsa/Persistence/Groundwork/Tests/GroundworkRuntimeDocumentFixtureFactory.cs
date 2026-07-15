using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.Options;

namespace Elsa.Persistence.Groundwork.Tests;

/// <summary>
/// Canonical instances and store drivers shared by the golden-fixture drift and compatibility tests.
/// Every builder uses deterministic ids and <see cref="DateTimeOffset.UnixEpoch"/> timestamps so the
/// serialized JSON is byte-stable across runs and machines, which is what makes a committed golden
/// fixture meaningful.
/// </summary>
internal static class GroundworkRuntimeDocumentFixtureFactory
{
    private static readonly IGroundworkRuntimeDocumentSerializer Serializer = GroundworkTestSerialization.Serializer;
    private static readonly RuntimeCheckpointPersistenceDecision ImmediateDecision =
        new(RuntimeCheckpointPersistenceMode.Immediate);

    // Runtime document kinds that already have concrete Groundwork stores, each paired with the
    // deterministic ids a spot-check reads back by. Manifest-only future-store kinds join here when
    // their adapters land.
    public static readonly IReadOnlyList<string> AllKinds =
    [
        ElsaRuntimeStorageManifest.BookmarkStateDocumentKind,
        ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind,
        ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind,
        ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind,
        ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind,
        ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind,
        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyDocumentKind,
        ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
        ElsaRuntimeStorageManifest.DurableValueStateDocumentKind,
        ElsaRuntimeStorageManifest.SchedulerStateDocumentKind,
        ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind,
        ElsaRuntimeStorageManifest.WorkflowHoldStateDocumentKind,
        ElsaRuntimeStorageManifest.IncidentStateDocumentKind,
        ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind,
        ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind,
        ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind,
        ElsaRuntimeStorageManifest.DurableTimerDocumentKind,
        ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind,
        ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind
    ];

    private const string Wf = "wf-1";

    // Captures the exact (SchemaVersion, ContentJson) the real store bridge writes for a kind's canonical
    // instance. The 12 direct-save kinds are captured at the SaveDocumentRequest boundary through a
    // capturing store; the checkpoint marker is written only inside a full atomic commit, so it is driven
    // through the writer against an in-memory store and read back.
    public static async Task<(string SchemaVersion, string ContentJson)> CaptureAsync(string kind)
    {
        if (kind == ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind)
        {
            var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
            await DriveSaveAsync(kind, store);
            var envelope = await store.LoadAsync(kind, CommitId);
            return (envelope!.SchemaVersion, envelope.ContentJson);
        }

        var capturing = new CapturingDocumentStore();
        await DriveSaveAsync(kind, capturing);
        return capturing.Captured(kind);
    }

    // Seeds an in-memory store with the committed fixture content under the LEGACY schema stamp, at the
    // exact composite id the bridge assigns. Tests then prove the historical document either loads through
    // the real read path or is rejected by an explicit clean-break floor. Driving the real save first lets
    // us discover the id without re-implementing id composition.
    public static async Task<InMemoryDocumentStore> SeedLegacyFixtureAsync(string kind, string fixtureContent)
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        await DriveSaveAsync(kind, store);

        foreach (var envelope in store.Snapshot(kind))
        {
            await store.SaveAsync(new SaveDocumentRequest(
                kind,
                envelope.Id,
                ElsaRuntimeDocumentVersions.LegacySchemaVersion,
                fixtureContent));
        }

        return store;
    }

    private static async Task DriveSaveAsync(string kind, IDocumentStore store)
    {
        switch (kind)
        {
            case ElsaRuntimeStorageManifest.BookmarkStateDocumentKind:
                await new GroundworkBookmarkStateStore(store, Serializer).SaveAsync(Bookmark());
                break;
            case ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind:
                await new GroundworkWorkflowExecutableStore(store, Serializer).SaveAsync(Executable());
                break;
            case ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind:
                await new GroundworkExecutableActivityTemplateStore(store, Serializer).SaveAsync(ActivityTemplate());
                break;
            case ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind:
                await new GroundworkWorkflowExecutableSourceReferenceStore(store, Serializer).SaveAsync(Reference());
                break;
            case ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind:
                await new GroundworkActivityExecutionStateStore(store, Serializer).SaveAsync(ActivityState());
                break;
            case ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind:
                await new GroundworkActivityExecutionInspectionStore(store, Serializer).SaveAsync(Projection());
                break;
            case ElsaRuntimeStorageManifest.ActivityExecutionHierarchyDocumentKind:
                await new GroundworkActivityExecutionHierarchyStore(store, Serializer).SaveAsync(HierarchyRecord());
                break;
            case ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind:
                await new GroundworkWorkflowExecutionStateStore(store, Serializer).SaveAsync(WorkflowState());
                break;
            case ElsaRuntimeStorageManifest.DurableValueStateDocumentKind:
                await new GroundworkDurableValueStateStore(store, Serializer).SaveAsync(DurableValue());
                break;
            case ElsaRuntimeStorageManifest.SchedulerStateDocumentKind:
                await new GroundworkSchedulerStateStore(store, Serializer).SaveAsync(Scheduler());
                break;
            case ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind:
                await new GroundworkExecutionLivenessStateStore(store, Serializer).SaveAsync(Operational());
                break;
            case ElsaRuntimeStorageManifest.WorkflowHoldStateDocumentKind:
                await new GroundworkWorkflowHoldStateStore(store, Serializer).SaveAsync(ControlPlane());
                break;
            case ElsaRuntimeStorageManifest.IncidentStateDocumentKind:
                await new GroundworkIncidentStateStore(store, Serializer).SaveAsync(Incident());
                break;
            case ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind:
                await new GroundworkRuntimePostCommitOutboxStore(store, Serializer).SavePendingAsync(OutboxItem());
                break;
            case ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind:
                await new GroundworkWorkflowSchedulerWorkQueue(store, Serializer).EnqueueAsync(WorkItem());
                break;
            case ElsaRuntimeStorageManifest.DurableTimerDocumentKind:
                await new GroundworkDurableTimerStore(store, Serializer).SaveAsync(Timer());
                break;
            case ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind:
                await new GroundworkWorkflowTriggerBindingStore(store, Serializer).SaveAsync(TriggerBinding());
                break;
            case ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind:
                await new GroundworkRecurringTriggerScheduleStore(store, Serializer).SaveAsync(Schedule());
                break;
            case ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind:
                await CheckpointWriter(store).CommitAsync(Commit(), ImmediateDecision);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown runtime document kind.");
        }
    }

    // Constructs the real store for a kind and asserts the seeded legacy fixture reads back with a
    // spot-checked field, proving the historical document loads through the bridge. Returns the spot value.
    public static async Task<object?> ReadSpotCheckAsync(string kind, IDocumentStore store) => kind switch
    {
        ElsaRuntimeStorageManifest.BookmarkStateDocumentKind =>
            (await new GroundworkBookmarkStateStore(store, Serializer).FindAsync(Wf, "bm-1"))?.StimulusType,
        ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind =>
            (await new GroundworkWorkflowExecutableStore(store, Serializer).FindAsync("artifact-1"))?.Identity.DefinitionId,
        ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind =>
            (await new GroundworkExecutableActivityTemplateStore(store, Serializer).FindAsync("template-1"))?.TemplateHash,
        ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind =>
            (await new GroundworkWorkflowExecutableSourceReferenceStore(store, Serializer).FindAsync("sourceref-1"))?.ArtifactId,
        ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind =>
            (await new GroundworkActivityExecutionStateStore(store, Serializer).FindAsync(Wf, "ae-1"))?.Status,
        ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind =>
            (await new GroundworkActivityExecutionInspectionStore(store, Serializer).FindAsync(Wf, "ae-1"))?.ActivityExecutionId,
        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyDocumentKind =>
            (await new GroundworkActivityExecutionHierarchyStore(store, Serializer, CursorCodec())
                .FindBoundaryAsync(Wf, "ae-1"))?.DefinitionVersionId,
        ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind =>
            (await new GroundworkWorkflowExecutionStateStore(store, Serializer).FindAsync(Wf))?.Status,
        ElsaRuntimeStorageManifest.DurableValueStateDocumentKind =>
            (await new GroundworkDurableValueStateStore(store, Serializer).FindAsync(Wf, "dv-1"))?.ValueId,
        ElsaRuntimeStorageManifest.SchedulerStateDocumentKind =>
            (await new GroundworkSchedulerStateStore(store, Serializer).FindAsync(Wf))?.Version,
        ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind =>
            (await new GroundworkExecutionLivenessStateStore(store, Serializer).FindAsync(Wf, "op-1"))?.OperationalStateId,
        ElsaRuntimeStorageManifest.WorkflowHoldStateDocumentKind =>
            (await new GroundworkWorkflowHoldStateStore(store, Serializer).FindAsync("cp-1"))?.WorkflowExecutionId,
        ElsaRuntimeStorageManifest.IncidentStateDocumentKind =>
            (await new GroundworkIncidentStateStore(store, Serializer).FindAsync(Wf, "inc-1"))?.Status,
        ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind =>
            (await new GroundworkRuntimePostCommitOutboxStore(store, Serializer)
                .GetDeliverableAsync(new RuntimePostCommitOutboxQuery(DateTimeOffset.UnixEpoch, 10)))
                .SingleOrDefault()?.OutboxItemId,
        ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind =>
            (await new GroundworkWorkflowSchedulerWorkQueue(store, Serializer)
                .ListAsync(new RuntimeSchedulerWorkQuery(Wf)))
                .SingleOrDefault()?.WorkItemId,
        ElsaRuntimeStorageManifest.DurableTimerDocumentKind =>
            (await new GroundworkDurableTimerStore(store, Serializer).FindAsync(Wf, "timer-1"))?.StimulusHash,
        ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind =>
            (await new GroundworkWorkflowTriggerBindingStore(store, Serializer)
                .ListByArtifactAsync("artifact-1"))
                .SingleOrDefault()?.StimulusType,
        ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind =>
            (await new GroundworkRecurringTriggerScheduleStore(store, Serializer).FindAsync("artifact-1:node-1"))?.StimulusHash,
        // The checkpoint marker has no typed domain store; the writer's dedup reads it via LoadAsync, so
        // that is the appropriate read path. The spot value is the commitId parsed from the loaded content.
        ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind =>
            await ReadCheckpointCommitIdAsync(store),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown runtime document kind.")
    };

    // The expected spot value that ReadSpotCheckAsync returns for a successfully loaded fixture.
    public static object ExpectedSpotValue(string kind) => kind switch
    {
        ElsaRuntimeStorageManifest.BookmarkStateDocumentKind => "Http",
        ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind => "definition-1",
        ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind => "hash-template-1",
        ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind => "artifact-1",
        ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind => ActivityExecutionStatus.Running,
        ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind => "ae-1",
        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyDocumentKind => "activity-ver-1",
        ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind => WorkflowExecutionStatus.Completed,
        ElsaRuntimeStorageManifest.DurableValueStateDocumentKind => "value-dv-1",
        ElsaRuntimeStorageManifest.SchedulerStateDocumentKind => 7L,
        ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind => "op-1",
        ElsaRuntimeStorageManifest.WorkflowHoldStateDocumentKind => Wf,
        ElsaRuntimeStorageManifest.IncidentStateDocumentKind => IncidentStatus.Open,
        ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind => "item-1",
        ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind => "work-1",
        ElsaRuntimeStorageManifest.DurableTimerDocumentKind => "timer-hash-1",
        ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind => "Event",
        ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind => "schedule-hash-1",
        ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind => CommitId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown runtime document kind.")
    };

    private static async Task<object?> ReadCheckpointCommitIdAsync(IDocumentStore store)
    {
        var envelope = await store.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, CommitId);
        if (envelope is null)
            return null;

        using var document = JsonDocument.Parse(envelope.ContentJson);
        return document.RootElement.GetProperty("commitId").GetString();
    }

    // --- Canonical builders (deterministic ids and timestamps) ---

    private const string CommitId = "commit-1";

    private static WorkflowTriggerBinding TriggerBinding() => new(
        TriggerBindingId: WorkflowTriggerBinding.BuildId("artifact-1", "node-trigger", "hash-order-approved"),
        ArtifactId: "artifact-1",
        DefinitionId: "definition-1",
        ArtifactVersion: "1",
        ArtifactHash: "hash-artifact-1",
        ExecutableNodeId: "node-trigger",
        StimulusType: "Event",
        StimulusHash: "hash-order-approved",
        CorrelationScope: "order-42",
        Metadata: new Dictionary<string, string> { ["tag"] = "v1" },
        CreatedAt: DateTimeOffset.UnixEpoch);

    private static BookmarkState Bookmark() => new(
        BookmarkId: "bm-1",
        WorkflowExecutionId: Wf,
        ActivityExecutionId: "ae-1",
        ExecutableNodeId: "node-1",
        ResumeTargetId: "resume-1",
        StimulusType: "Http",
        StimulusHash: "hash-1",
        Payload: JsonSerializer.SerializeToElement(new { url = "/orders" }),
        Metadata: new Dictionary<string, string> { ["tag"] = "v1" },
        CreatedAt: DateTimeOffset.UnixEpoch,
        ExpiresAt: DateTimeOffset.UnixEpoch.AddDays(1));

    private static WorkflowExecutable Executable()
    {
        var child = new ExecutableNode(
            executableNodeId: "child",
            authoredActivityId: "authored-child",
            activityType: "Elsa.SendEmail",
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("Elsa.Activities.SendEmailDescriptor", RuntimeActivityDescriptor.InitialSchemaVersion, Json("""{ "kind": "Send" }""")),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["to"] = new(
                    inputName: "to",
                    source: RuntimeInputBindingSource.DurableValue,
                    durableValue: new RuntimeDurableValueReference("customerEmail"))
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string> { ["role"] = "leaf" });

        var root = new ExecutableNode(
            executableNodeId: "root",
            authoredActivityId: "authored-root",
            activityType: "Elsa.Sequence",
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("Elsa.Activities.SequenceDescriptor", RuntimeActivityDescriptor.InitialSchemaVersion, Json("""{ "kind": "Send" }""")),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot("Body", [child])]);

        return new WorkflowExecutable(
            identity: new WorkflowExecutableIdentity(
                ArtifactId: "artifact-1",
                DefinitionId: "definition-1",
                DefinitionVersionId: "version-1",
                ArtifactVersion: "1",
                ArtifactHash: "hash-artifact-1"),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>
            {
                ["resume-1"] = new("resume-1", "node-child", "Bookmark", new Dictionary<string, string> { ["stimulus"] = "Http" }, "local-resume-1")
            },
            createdAt: DateTimeOffset.UnixEpoch,
            compatibilityMetadata: new Dictionary<string, string> { ["slice"] = "slice-1" });
    }

    private static ExecutableActivityTemplate ActivityTemplate()
    {
        var root = new ExecutableNode(
            executableNodeId: "template-root",
            authoredActivityId: "authored-template-root",
            activityType: "Elsa.Sequence",
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(
                "Elsa.Activities.Sequence",
                RuntimeActivityDescriptor.InitialSchemaVersion,
                Json("""{ "kind": "Sequence" }""")),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string> { ["role"] = "template-root" });

        return new ExecutableActivityTemplate(
            "template-1",
            "hash-template-1",
            root,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            [],
            [new ExecutableActivityTemplateIdentity("template-1", "hash-template-1")],
            [new RuntimeRequirement("Elsa.Activities.Sequence", RuntimeActivityDescriptor.InitialSchemaVersion)],
            "provider/1",
            new Dictionary<string, string> { ["wire"] = "stable-descriptor" },
            DateTimeOffset.UnixEpoch);
    }

    private static WorkflowExecutableSourceReference Reference() => new(
        SourceReferenceId: "sourceref-1",
        ArtifactId: "artifact-1",
        SourceKind: "WorkflowDefinitionVersion",
        SourceId: "version-1",
        SourceVersion: "1",
        DefinitionId: "definition-1",
        DefinitionVersionId: "version-1",
        ArtifactVersion: "1",
        CreatedAt: DateTimeOffset.UnixEpoch,
        PublishedAt: DateTimeOffset.UnixEpoch,
        Scope: WorkflowExecutableReferenceScope.Published,
        ExpiresAt: null,
        DeletedAt: null,
        DeletedReason: null,
        Layout: [new WorkflowExecutableLayoutRecord("root", 10, 20, 100, 60, Json("""{ "collapsed": false }"""))]);

    private static ActivityExecutionState ActivityState() => new(
        Execution: new ActivityExecution("ae-1", Wf, "node-ae-1", "authored", "Elsa.Log", "1.0.0"),
        Status: ActivityExecutionStatus.Running,
        SubStatus: null,
        ExecutionSequence: 1,
        ScheduledAt: DateTimeOffset.UnixEpoch,
        StartedAt: DateTimeOffset.UnixEpoch,
        CompletedAt: null,
        SchedulingActivityExecutionId: null,
        ParentActivityExecutionId: null,
        BranchId: null,
        IterationId: null,
        Provenance: ActivitySchedulingProvenance.From(
            Wf,
            parentActivityExecutionId: null,
            schedulingActivityExecutionId: null,
            branchId: null,
            iterationId: null,
            executionPathId: null,
            executionScopeId: "scope-1",
            schedulingCause: "test",
            attempt: new ActivityExecutionAttemptLineage(1, "ae-1", null)),
        CallStackDepth: 0,
        BookmarkIds: [],
        IncidentIds: [],
        FaultCount: 0,
        AggregateFaultCount: 0,
        Metadata: new Dictionary<string, string> { ["tag"] = "v1" },
        ExecutionScopeId: "scope-1",
        Attempt: new ActivityExecutionAttemptLineage(1, "ae-1", null));

    private static ActivityExecutionInspectionProjection Projection() => new(
        ActivityExecutionId: "ae-1",
        WorkflowExecutionId: Wf,
        ExecutableNodeId: "node-ae-1",
        AuthoredActivityId: "authored-a",
        ActivityType: "Elsa.Test",
        ActivityTypeVersion: "1.0.0",
        Status: ActivityExecutionStatus.Completed,
        SubStatus: null,
        ExecutionSequence: 1,
        ScheduledAt: DateTimeOffset.UnixEpoch,
        StartedAt: DateTimeOffset.UnixEpoch,
        CompletedAt: DateTimeOffset.UnixEpoch,
        FirstCheckpointId: "checkpoint-first",
        LastCheckpointId: "checkpoint-last",
        LastCommittedAt: DateTimeOffset.UnixEpoch,
        Provenance: ActivitySchedulingProvenance.From(
            Wf,
            parentActivityExecutionId: null,
            schedulingActivityExecutionId: null,
            branchId: null,
            iterationId: null,
            executionPathId: null,
            executionScopeId: "scope-1",
            schedulingCause: "test",
            attempt: new ActivityExecutionAttemptLineage(1, "ae-1", null)),
        OutcomeNames: ["Done"],
        Bookmarks: [],
        Incidents: [],
        ValueSnapshots: [],
        Metadata: new Dictionary<string, string>(),
        ExecutionScopeId: "scope-1",
        Attempt: new ActivityExecutionAttemptLineage(1, "ae-1", null));

    private static ActivityExecutionHierarchyRecord HierarchyRecord() =>
        ActivityExecutionHierarchyProjector.FromInspection(Projection() with
        {
            ExecutionScopeId = "ae-1",
            Provenance = ActivitySchedulingProvenance.From(
                Wf, null, null, null, null, null, "ae-1", "test",
                attempt: new ActivityExecutionAttemptLineage(1, "ae-1", null)),
            Metadata = new Dictionary<string, string>
            {
                ["activity.definitionId"] = "activity-def-1",
                ["activity.definitionVersionId"] = "activity-ver-1",
                ["activity.version"] = "1.0.0",
                ["activity.templateHash"] = "template-hash-1"
            }
        });

    private static HmacActivityExecutionHierarchyCursorCodec CursorCodec() => new(
        Options.Create(new ActivityExecutionHierarchyCursorOptions
        {
            SigningKey = "fixture-signing-key-that-is-at-least-thirty-two-bytes"
        }));

    private static WorkflowExecutionState WorkflowState() => new(
        Wf,
        new WorkflowExecutableIdentity(
            ArtifactId: "artifact-wf-1",
            DefinitionId: "definition-1",
            DefinitionVersionId: "version-1",
            ArtifactVersion: "1",
            ArtifactHash: "hash-wf-1"),
        WorkflowExecutionStatus.Completed,
        SubStatus: null,
        CreatedAt: DateTimeOffset.UnixEpoch,
        StartedAt: DateTimeOffset.UnixEpoch,
        UpdatedAt: DateTimeOffset.UnixEpoch,
        CompletedAt: DateTimeOffset.UnixEpoch,
        CorrelationId: "correlation-1",
        ParentWorkflowExecutionId: null,
        TenantId: "tenant-1",
        SystemMetadata: new Dictionary<string, string> { ["tag"] = "v1" });

    private static DurableValueState DurableValue() => new(
        "dv-1",
        Wf,
        "value-dv-1",
        new RuntimeValueTypeDescriptor("int", null, null),
        DurableValueLifecycle.Instance,
        DurableValueStorage.Inline,
        Json("42"),
        externalReference: null,
        sourceActivityExecutionId: null,
        capturedAt: DateTimeOffset.UnixEpoch,
        metadata: new Dictionary<string, string> { ["tag"] = "v1" });

    private static SchedulerState Scheduler() => new(
        Wf,
        7,
        pendingWork:
        [
            new ScheduledActivityWorkItem(
                WorkItemId: "work-wf-1",
                WorkflowExecutionId: Wf,
                ExecutableNodeId: "node-wf-1",
                ActivityExecutionId: null,
                SchedulingActivityExecutionId: null,
                BranchId: null,
                IterationId: null,
                EnqueuedAt: DateTimeOffset.UnixEpoch,
                Reason: "scheduled")
        ]);

    private static ExecutionLivenessState Operational() => new(
        "op-1",
        Wf,
        executionLease: new RuntimeExecutionLease(
            leaseId: "lease-1",
            workflowExecutionId: Wf,
            ownerId: "owner-1",
            acquiredAt: DateTimeOffset.UnixEpoch,
            expiresAt: DateTimeOffset.UnixEpoch.AddMinutes(5),
            fencingToken: 1),
        heartbeat: null,
        drain: null,
        interruptedExecution: null);

    private static WorkflowHoldState ControlPlane() => new(
        "cp-1",
        Wf,
        metadata: new Dictionary<string, string> { ["tag"] = "v1" });

    private static IncidentState Incident() => new(
        "inc-1",
        Wf,
        activityExecutionId: null,
        executableNodeId: null,
        IncidentSeverity.Error,
        IncidentStatus.Open,
        IncidentResolutionAction.None,
        failureType: "System.Exception",
        message: "boom",
        createdAt: DateTimeOffset.UnixEpoch,
        resolvedAt: null);

    private static RuntimePostCommitOutboxItem OutboxItem() => new(
        outboxItemId: "item-1",
        intent: new RuntimePostCommitIntent(
            intentId: "intent-item-1",
            workflowExecutionId: Wf,
            kind: "publish",
            recordedAt: DateTimeOffset.UnixEpoch,
            activityExecutionId: null,
            idempotencyKey: null,
            payload: null),
        status: RuntimePostCommitOutboxStatus.Pending,
        recordedAt: DateTimeOffset.UnixEpoch,
        availableAt: DateTimeOffset.UnixEpoch,
        retryPolicy: null);

    private static RuntimeSchedulerWorkItem WorkItem() => new(
        workItemId: "work-1",
        workflowExecutionId: Wf,
        commandId: "command-1",
        commandKind: WorkflowExecutionCommandKind.RunSchedulerWork,
        envelopeId: "envelope-1",
        idempotencyKey: "wf-1:command-1",
        enqueuedAt: DateTimeOffset.UnixEpoch,
        recordedAt: DateTimeOffset.UnixEpoch,
        sequence: 1,
        payload: JsonSerializer.SerializeToElement(new { command = "resume" }),
        commandMetadata: new Dictionary<string, string> { ["source"] = "test" },
        envelopeMetadata: new Dictionary<string, string> { ["transport"] = "in-process" },
        executionScopeId: "scope-1",
        attempt: new ActivityExecutionAttemptLineage(1, "ae-1", null));

    private static DurableTimer Timer() => new(
        TimerId: "timer-1",
        WorkflowExecutionId: Wf,
        StimulusType: "DurableTimer",
        StimulusHash: "timer-hash-1",
        DueTime: DateTimeOffset.UnixEpoch.AddMinutes(5),
        CreatedAt: DateTimeOffset.UnixEpoch,
        Input: JsonSerializer.SerializeToElement(new { reason = "delay" }),
        Metadata: new Dictionary<string, string> { ["tag"] = "v1" });

    private static RecurringTriggerSchedule Schedule() => new(
        ScheduleId: "artifact-1:node-1",
        ArtifactId: "artifact-1",
        StimulusType: "Timer",
        StimulusHash: "schedule-hash-1",
        Kind: RecurringScheduleKind.Interval,
        Expression: "PT5M",
        NextOccurrence: DateTimeOffset.UnixEpoch.AddMinutes(5),
        CreatedAt: DateTimeOffset.UnixEpoch);

    private static RuntimeCheckpointCommit Commit()
    {
        var checkpoint = new RuntimeCheckpoint(
            CheckpointId: "cp-commit-1",
            Name: "checkpoint",
            WorkflowExecutionId: Wf,
            OccurredAt: DateTimeOffset.UnixEpoch,
            ActivityExecutionIds: ["ae-1"],
            Metadata: new Dictionary<string, string>());

        return new RuntimeCheckpointCommit(
            CommitId,
            checkpoint,
            new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());
    }

    private static GroundworkRuntimeCheckpointWriter CheckpointWriter(IDocumentStore store) => new(
        store,
        Serializer,
        new GroundworkWorkflowExecutionStateStore(store, Serializer),
        new GroundworkSchedulerStateStore(store, Serializer),
        new GroundworkActivityExecutionStateStore(store, Serializer),
        new GroundworkBookmarkStateStore(store, Serializer),
        new GroundworkDurableValueStateStore(store, Serializer),
        new GroundworkIncidentStateStore(store, Serializer),
        new GroundworkExecutionLivenessStateStore(store, Serializer));

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    // Records the last (SchemaVersion, ContentJson) saved per document kind at the SaveDocumentRequest
    // boundary. Only SaveAsync is exercised by the direct-save bridges; every other member throws so an
    // unexpected access surfaces loudly instead of silently returning empty data.
    private sealed class CapturingDocumentStore : IDocumentStore
    {
        private readonly Dictionary<string, (string SchemaVersion, string ContentJson)> _captured = new(StringComparer.Ordinal);
        public DocumentStoreAccess Access { get; } = DocumentStoreAccess.Global;

        public (string SchemaVersion, string ContentJson) Captured(string kind) => _captured[kind];

        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default)
        {
            _captured[request.DocumentKind] = (request.SchemaVersion, request.ContentJson);
            var now = DateTimeOffset.UnixEpoch;
            var envelope = new DocumentEnvelope(request.DocumentKind, request.Id, request.SchemaVersion, 1, request.ContentJson, now, now);
            return Task.FromResult(DocumentStoreWriteResult.Saved(envelope));
        }

        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
            // The outbox bridge probes for an existing item before saving; report "not found" so the
            // save proceeds and is captured. Nothing is retained, so this always reflects an empty store.
            Task.FromResult<DocumentEnvelope?>(null);

        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The capturing store only records saves.");

        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
            // Immutable content-addressed stores probe a secondary unique index before their first save.
            // The capture boundary represents an empty store, so that probe has no matches.
            Task.FromResult<IReadOnlyList<DocumentEnvelope>>([]);

        public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The capturing store only records saves.");

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The capturing store only records saves.");

        public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The capturing store only records saves.");

        public TransactionBoundary TransactionBoundary => TransactionBoundary.CrossUnitAtomic;

        public Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The capturing store only records saves.");
    }
}
