using System.Linq.Expressions;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Sequence.Models;
using Elsa.Persistence.Core;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Models;
using Elsa.Primitives.Persistence;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class PublishWorkflowRequestHandlerTests
{
    private const string UnknownStructureKind = "test.opaque.structure";
    private const string UnknownStructureSchemaVersion = "1.0.0";
    private static readonly DateTimeOffset ReferenceEvaluationTime = DateTimeOffset.Parse("2026-07-19T12:00:00Z");

    private readonly InMemoryWorkflowExecutableStore _store = new();
    private readonly ActivityDefinitionVersion _writeLineActivity = ActivityVersion("activity-write-line", "Text", new TypeReference("String"));
    private readonly ActivityDefinitionVersion _sequenceActivity = ActivityVersion(
        "activity-sequence",
        typeof(SequenceActivity).FullName!,
        [],
        StructureFacet(SequenceActivity.StructureKind, SequenceActivity.StructureSchemaVersion));
    private readonly ActivityDefinitionVersion _flowchartActivity = ActivityVersion(
        "activity-flowchart",
        typeof(FlowchartActivity).FullName!,
        [],
        StructureFacet(FlowchartActivity.StructureKind, FlowchartActivity.StructureSchemaVersion));
    private readonly IActivityStructureService _activityStructureService = ActivityStructureService();
    private readonly InMemoryWorkflowTriggerBindingStore _bindingStore = new();
    private readonly InMemoryPublicationSlotStore _slotStore = new();
    private readonly InMemoryPublicationRecordStore _publicationStore = new();
    private readonly InMemoryPublicationPolicyStore _policyStore = new();
    private readonly InMemoryPublicationProjectionIntentStore _intentStore = new();
    private readonly PublicationSnapshotReviewService _snapshotReviews = new(
        TimeProvider.System,
        new InMemoryPublicationSnapshotReviewStore());

    [Fact]
    public async Task Publishes_reviewed_snapshot_when_candidate_and_authority_are_unchanged()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var layout = new WorkflowDefinitionVersionLayout
        {
            WorkflowDefinitionVersionId = workflowVersion.Id,
            Records = [new DesignMetadataRecord("write-one", 10, 20)]
        };
        var issued = await _snapshotReviews.IssueAsync(
            _snapshotReviews.ComputeCandidateHash(workflowVersion.State, layout.Records),
            SnapshotPlan());

        var result = await Handler(workflowVersion, layout, _writeLineActivity)
            .Handle(new PublishWorkflow(workflowVersion.Id, PreflightToken: issued.PreflightToken), CancellationToken.None);

        Assert.True(result.WasCreated);
        Assert.NotNull(await _store.FindAsync(result.ArtifactId));
    }

    [Fact]
    public async Task Stale_snapshot_token_fails_before_persisting_the_compiled_artifact()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var issued = await _snapshotReviews.IssueAsync(
            _snapshotReviews.ComputeCandidateHash(WorkflowDefinitionState.Empty, []),
            SnapshotPlan());

        await Assert.ThrowsAsync<PublicationSnapshotReviewException>(() =>
            Handler(workflowVersion)
                .Handle(new PublishWorkflow(workflowVersion.Id, PreflightToken: issued.PreflightToken), CancellationToken.None));

        Assert.Empty(await _store.ListAllAsync());
        Assert.Empty(await _referenceStore.ListAllAsync(WorkflowExecutableReferenceScope.Published));
    }

    [Fact]
    public async Task PublishesRootActivityIntoExecutableArtifact()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var handler = Handler(workflowVersion);

        var view = await handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var executable = await _store.FindAsync(view.ArtifactId);

        Assert.NotNull(executable);
        Assert.Equal("definition-1", view.DefinitionId);
        Assert.Equal("version-1", view.DefinitionVersionId);
        Assert.Equal("write-one", view.RootActivityId);
        Assert.Equal(1, view.NodeCount);
        Assert.Equal("write-one", executable.RootActivity.ExecutableNodeId);
        Assert.Equal("one", executable.NodesById["write-one"].InputBindings["Text"].LiteralValue!.Value.GetString());
        var binding = executable.NodesById["write-one"].InputBindings["Text"];
        Assert.Equal("String", binding.TargetType.Alias);
        Assert.DoesNotContain("typeName", binding.Metadata);
    }

    [Fact]
    public async Task Publication_propagates_tenant_to_compilation_and_source_reference()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var (handler, compiler) = RecordingHandler(workflowVersion);

        var published = await handler.Handle(
            new PublishWorkflow(workflowVersion.Id, TenantId: "tenant-a"),
            CancellationToken.None);

        Assert.Equal("tenant-a", Assert.Single(compiler.Requests).TenantId);
        var reference = await _referenceStore.FindAsync(published.SourceReferenceId!);
        Assert.NotNull(reference);
        Assert.Equal("tenant-a", reference.TenantId);
    }

    [Fact]
    public async Task Publication_without_tenant_keeps_compilation_and_source_reference_unscoped()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var (handler, compiler) = RecordingHandler(workflowVersion);

        var published = await handler.Handle(new PublishWorkflow(workflowVersion.Id), CancellationToken.None);

        Assert.Null(Assert.Single(compiler.Requests).TenantId);
        var reference = await _referenceStore.FindAsync(published.SourceReferenceId!);
        Assert.NotNull(reference);
        Assert.Null(reference.TenantId);
    }

    [Fact]
    public async Task Tenant_source_fact_does_not_change_artifact_identity()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var handler = Handler(workflowVersion);

        var first = await handler.Handle(
            new PublishWorkflow(workflowVersion.Id, TenantId: "tenant-a"),
            CancellationToken.None);
        var second = await handler.Handle(
            new PublishWorkflow(
                workflowVersion.Id,
                PublicationAction.PublishSideBySide,
                SlotName: "blue",
                TenantId: "tenant-b"),
            CancellationToken.None);

        Assert.Equal(first.ArtifactId, second.ArtifactId);
        Assert.Equal(first.ArtifactHash, second.ArtifactHash);
        Assert.NotEqual(first.PublicationId, second.PublicationId);
        Assert.NotEqual(first.SourceReferenceId, second.SourceReferenceId);
        var references = await _referenceStore.ListAllByArtifactAsync(first.ArtifactId);
        Assert.Equal(["tenant-a", "tenant-b"], references.Select(x => x.TenantId).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task RepublishingIdenticalArtifactKeepsExistingPublicationAuthority()
    {
        var handler = Handler(WorkflowVersion(Node("write-one", Text("one"))));

        var first = await handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var second = await handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        Assert.True(first.WasCreated);
        Assert.False(second.WasCreated);
        Assert.Equal(first.PublicationId, second.PublicationId);
        Assert.Equal(first.SourceReferenceId, second.SourceReferenceId);
        Assert.Equal(PublicationStatusView.Active, second.Status);
        Assert.Single(await _referenceStore.ListAllAsync(
            WorkflowExecutableReferenceScope.Published,
            liveOnly: true,
            now: ReferenceEvaluationTime));
    }

    [Fact]
    public async Task Republishing_identical_artifact_to_same_slot_does_not_reuse_another_tenants_authority()
    {
        var handler = Handler(WorkflowVersion(Node("write-one", Text("one"))));

        var first = await handler.Handle(
            new PublishWorkflow("version-1", TenantId: "tenant-a"),
            CancellationToken.None);
        var second = await handler.Handle(
            new PublishWorkflow("version-1", TenantId: "tenant-b"),
            CancellationToken.None);

        Assert.True(first.WasCreated);
        Assert.True(second.WasCreated);
        Assert.Equal(first.ArtifactId, second.ArtifactId);
        Assert.NotEqual(first.PublicationId, second.PublicationId);
        Assert.NotEqual(first.SourceReferenceId, second.SourceReferenceId);
        var liveReference = Assert.Single(await _referenceStore.ListAllAsync(
            WorkflowExecutableReferenceScope.Published,
            liveOnly: true,
            now: ReferenceEvaluationTime));
        Assert.Equal(second.SourceReferenceId, liveReference.SourceReferenceId);
        Assert.Equal("tenant-b", liveReference.TenantId);
    }

    [Fact]
    public async Task SameBehaviorFromTwoDefinitionsYieldsOneArtifactAndTwoIndependentReferences()
    {
        // Each definition owns an independent default slot even when both publications reuse one content-addressed
        // artifact. Neither definition's publication replaces the other definition's authority.
        var firstVersion = WorkflowVersion(Node("write-one", Text("one")), definitionId: "definition-A", versionId: "version-A", version: "1.0.0");
        var secondVersion = WorkflowVersion(Node("write-one", Text("one")), definitionId: "definition-B", versionId: "version-B", version: "3.7.0");

        var first = await Handler(firstVersion).Handle(new PublishWorkflow("version-A"), CancellationToken.None);
        var second = await Handler(secondVersion).Handle(new PublishWorkflow("version-B"), CancellationToken.None);

        Assert.Equal(first.ArtifactId, second.ArtifactId);
        Assert.Equal(first.ArtifactHash, second.ArtifactHash);
        Assert.Single(await _store.ListAllAsync());

        var references = await _referenceStore.ListAllByArtifactAsync(first.ArtifactId);
        Assert.Equal(2, references.Count);
        Assert.Equal(["definition-A", "definition-B"], references.Select(r => r.DefinitionId).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(["version-A", "version-B"], references.Select(r => r.DefinitionVersionId).OrderBy(x => x, StringComparer.Ordinal));
        Assert.All(references, reference =>
        {
            Assert.Equal(first.ArtifactId, reference.ArtifactId);
            Assert.Equal(WorkflowExecutableReferenceScope.Published, reference.Scope);
            Assert.NotNull(reference.PublishedAt);
            Assert.Null(reference.ExpiresAt);
        });
    }

    [Fact]
    public async Task PublishingToOccupiedDefaultSlotReplacesItsLiveAuthority()
    {
        var firstVersion = WorkflowVersion(Node("write-one", Text("one")), "definition-1", "version-1", "1.0.0");
        var secondVersion = WorkflowVersion(Node("write-two", Text("two")), "definition-1", "version-2", "2.0.0");

        var first = await Handler(firstVersion).Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var storedFirst = await _store.FindAsync(first.ArtifactId);
        var second = await Handler(secondVersion).Handle(new PublishWorkflow("version-2"), CancellationToken.None);
        var storedSecond = await _store.FindAsync(second.ArtifactId);

        Assert.NotEqual(first.ArtifactId, second.ArtifactId);
        Assert.NotNull(storedFirst);
        Assert.NotNull(storedSecond);

        var liveReferences = await _referenceStore.ListAllAsync(
            WorkflowExecutableReferenceScope.Published,
            liveOnly: true,
            now: ReferenceEvaluationTime);
        var retiredReference = await _referenceStore.FindAsync(first.SourceReferenceId!);

        Assert.Collection(liveReferences, reference => Assert.Equal(second.SourceReferenceId, reference.SourceReferenceId));
        Assert.NotNull(retiredReference?.DeletedAt);
        Assert.NotEqual(first.SourceReferenceId, second.SourceReferenceId);
    }

    [Fact]
    public async Task ExplicitNamedSlotKeepsDefaultAndNamedAuthoritiesLiveSideBySide()
    {
        var defaultVersion = WorkflowVersion(Node("write-default", Text("default")), "definition-1", "version-1", "1.0.0");
        var namedVersion = WorkflowVersion(Node("write-blue", Text("blue")), "definition-1", "version-2", "2.0.0");

        var defaultPublication = await Handler(defaultVersion).Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var namedPublication = await Handler(namedVersion).Handle(NamedPublish("version-2", "blue"), CancellationToken.None);

        var liveReferences = await _referenceStore.ListAllAsync(
            WorkflowExecutableReferenceScope.Published,
            liveOnly: true,
            now: ReferenceEvaluationTime);

        Assert.Equal(2, liveReferences.Count);
        Assert.Contains(liveReferences, reference => reference.SourceReferenceId == defaultPublication.SourceReferenceId);
        Assert.Contains(liveReferences, reference => reference.SourceReferenceId == namedPublication.SourceReferenceId);
    }

    [Fact]
    public async Task PublishDoesNotWriteSourceReferenceWhileDeletionGuardOwnsArtifact()
    {
        var version = WorkflowVersion(Node("write-one", Text("one")));
        var handler = Handler(version);
        var first = await handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        await _referenceStore.RetireAsync(first.SourceReferenceId!, DateTimeOffset.UtcNow, "test-setup");
        var now = DateTimeOffset.UtcNow;
        var deletionGuard = await _store.TryBeginDeletionAsync(first.ArtifactId, "gc-test", now.AddMinutes(1), now);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseUnavailableException>(() =>
            handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.NotNull(deletionGuard);
        Assert.Equal(first.ArtifactId, exception.ArtifactId);
        Assert.Single(await _referenceStore.ListAllByArtifactAsync(first.ArtifactId));
        Assert.Empty(await _referenceStore.ListAllAsync(
            WorkflowExecutableReferenceScope.Published,
            liveOnly: true,
            now: ReferenceEvaluationTime));
    }

    [Fact]
    public async Task UnpublishingSlotRetiresAuthorityWithoutDeletingItsArtifact()
    {
        var version = WorkflowVersion(Node("write-one", Text("one")));
        var published = await Handler(version).Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        await InvokeSlotLifecycleHandlerAsync("Unpublish", "definition-1", "default");

        var reference = await _referenceStore.FindAsync(published.SourceReferenceId!);
        Assert.NotNull(reference?.DeletedAt);
        Assert.Empty(await _referenceStore.ListAllAsync(
            WorkflowExecutableReferenceScope.Published,
            liveOnly: true,
            now: ReferenceEvaluationTime));
        Assert.NotNull(await _store.FindAsync(published.ArtifactId));
    }

    [Fact]
    public async Task RestoringUnpublishedSlotReestablishesAuthorityAfterLifecycleValidation()
    {
        var version = WorkflowVersion(Node("write-one", Text("one")));
        var published = await Handler(version).Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        await InvokeSlotLifecycleHandlerAsync("Unpublish", "definition-1", "default");

        await InvokeSlotLifecycleHandlerAsync("Restore", "definition-1", "default");

        var liveReference = Assert.Single(await _referenceStore.ListAllAsync(
            WorkflowExecutableReferenceScope.Published,
            liveOnly: true,
            now: ReferenceEvaluationTime));
        Assert.Equal(published.ArtifactId, liveReference.ArtifactId);
        Assert.NotNull(await _store.FindAsync(published.ArtifactId));
    }

    [Fact]
    public async Task PublishedReferenceCarriesLayoutRecordsCopiedFromDefinitionVersion()
    {
        // Acceptance 3 (ADR 0039): the publication reference embeds the layout sidecar copied verbatim from the
        // definition version's layout store.
        var version = WorkflowVersion(Node("write-one", Text("one")));
        var additional = JsonSerializer.SerializeToElement(new { color = "blue" });
        var layout = new WorkflowDefinitionVersionLayout
        {
            WorkflowDefinitionVersionId = "version-1",
            Records = [new DesignMetadataRecord("write-one", 12.5, 34.0, 200, 80, additional)]
        };

        var view = await Handler(version, layout, _writeLineActivity).Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        var reference = Assert.Single(await _referenceStore.ListAllByArtifactAsync(view.ArtifactId));
        var record = Assert.Single(reference.Layout);
        Assert.Equal("write-one", record.NodeId);
        Assert.Equal(12.5, record.X);
        Assert.Equal(34.0, record.Y);
        Assert.Equal(200, record.Width);
        Assert.Equal(80, record.Height);
        Assert.Equal("blue", record.AdditionalProperties!.Value.GetProperty("color").GetString());
    }

    [Fact]
    public async Task PublishedReferenceCarriesVerbatimAuthoredInputSourceOutsideArtifactHash()
    {
        const string authoredJson = "{\"code\":\"return variables.orderId;\",\"options\":{\"strict\":true}}";
        using var document = JsonDocument.Parse(authoredJson);
        var input = new WorkflowArgumentState(
            "Text",
            new ArgumentValue(document.RootElement.Clone(), "JavaScript"),
            null,
            null,
            null,
            true);
        var version = WorkflowVersion(Node("write-one", input));

        var view = await Handler(version).Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        var reference = Assert.Single(await _referenceStore.ListAllByArtifactAsync(view.ArtifactId));
        var authored = Assert.Single(reference.AuthoredInputs);
        Assert.Equal("write-one", authored.ExecutableNodeId);
        Assert.Equal("Text", authored.InputKey);
        Assert.Equal("JavaScript", authored.ExpressionType);
        Assert.Equal(authoredJson, authored.Value.GetRawText());

        var executable = await _store.FindAsync(view.ArtifactId);
        Assert.Equal(view.ArtifactHash, executable!.Identity.ArtifactHash);
    }

    [Fact]
    public async Task CompiledInputBindingCarriesStableInputKeyAndSensitivity()
    {
        var input = new WorkflowArgumentState(
            "Text",
            new ArgumentValue("classified", "Literal"),
            null,
            null,
            null,
            true);

        var view = await Handler(WorkflowVersion(Node("write-one", input)))
            .Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        var executable = await _store.FindAsync(view.ArtifactId);
        var binding = Assert.Single(executable!.RootActivity.InputBindings).Value;
        Assert.Equal("Text", binding.InputKey);
        Assert.True(binding.EffectivePolicy.IsSensitive);
    }

    [Fact]
    public async Task PublishedExecutableArtifactCanBeDispatchedForExecution()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var published = await Handler(workflowVersion).Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var dispatcher = new WorkflowStartDispatcher(
            _store,
            _referenceStore,
            new InProcessWorkflowExecutionActorProvider(),
            new ShortRuntimeExecutionIdGenerator());

        // The publish above appended a live Published source reference the dispatch gates on (ADR 0040).
        var result = await dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest(published.ArtifactId, "test"));

        Assert.Equal(published.ArtifactId, result.PinnedExecutable.ArtifactId);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.CommandDispatch.Status);
        Assert.NotEmpty(result.WorkflowExecutionId);
    }

    [Fact]
    public async Task PublishesSequenceAuthoredStructureIntoExecutableChildSlotAndStructure()
    {
        var root = SequenceNode(
            "sequence",
            [
                Node("write-one", Text("one")),
                Node("write-two", Text("two"))
            ]);
        var workflowVersion = WorkflowVersion(root);
        var handler = Handler(workflowVersion, _writeLineActivity, _sequenceActivity);

        var view = await handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var executable = await _store.FindAsync(view.ArtifactId);

        Assert.NotNull(executable);
        Assert.Equal("sequence", view.RootActivityId);
        Assert.Equal(3, view.NodeCount);
        var childSlot = Assert.Single(executable.RootActivity.ChildSlots);
        Assert.Equal(SequenceActivity.ActivitiesSlotName, childSlot.Name);
        Assert.Equal(["write-one", "write-two"], childSlot.Activities.Select(activity => activity.ExecutableNodeId));

        Assert.NotNull(executable.RootActivity.Structure);
        Assert.Equal(SequenceActivity.StructureKind, executable.RootActivity.Structure.Kind);
        Assert.Equal(["write-one", "write-two"], executable.RootActivity.Structure.Payload.GetProperty("activities").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task PublishesFlowchartAuthoredStructureIntoExecutableChildSlotAndRuntimeStructure()
    {
        var root = FlowchartNode(
            "flowchart",
            [
                Node("write-one", Text("one")),
                Node("write-two", Text("two"))
            ],
            [new FlowchartConnection(new FlowchartEndpoint("write-one", "Done"), new FlowchartEndpoint("write-two", null))],
            "write-one");
        var workflowVersion = WorkflowVersion(root);
        var handler = Handler(workflowVersion, _writeLineActivity, _flowchartActivity);

        var view = await handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var executable = await _store.FindAsync(view.ArtifactId);

        Assert.NotNull(executable);
        Assert.NotNull(executable.RootActivity.Structure);
        Assert.Equal(FlowchartActivity.StructureKind, executable.RootActivity.Structure.Kind);
        Assert.Equal(FlowchartActivity.StructureSchemaVersion, executable.RootActivity.Structure.SchemaVersion);
        Assert.Equal("write-one", executable.RootActivity.Structure.Payload.GetProperty("startNodeId").GetString());
        Assert.False(executable.RootActivity.Structure.Payload.TryGetProperty("activities", out _));
        var childSlot = Assert.Single(executable.RootActivity.ChildSlots);
        Assert.Equal(FlowchartActivity.ActivitiesSlotName, childSlot.Name);
        Assert.Equal(["write-one", "write-two"], childSlot.Activities.Select(activity => activity.ExecutableNodeId));
    }

    [Fact]
    public async Task PublishesUnknownOpaqueStructureWithoutProjectingChildren()
    {
        var root = Node(
            "opaque",
            structure: new ActivityNodeStructure(
                UnknownStructureKind,
                UnknownStructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { marker = "kept" })),
            Text("opaque"));
        var workflowVersion = WorkflowVersion(root);
        var handler = Handler(workflowVersion);

        var view = await handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var executable = await _store.FindAsync(view.ArtifactId);

        Assert.NotNull(executable);
        Assert.Empty(executable.RootActivity.ChildSlots);
        Assert.Equal(UnknownStructureKind, executable.RootActivity.Structure?.Kind);
        Assert.Equal("kept", executable.RootActivity.Structure?.Payload.GetProperty("marker").GetString());
    }

    [Fact]
    public async Task RejectsDeclaredStructureWhoseHandlerIsMissing()
    {
        // The sequence activity's catalog row declares its structure contract (design facet), but this
        // shell has no structure handler registered — the composition mismatch that previously let a
        // composite publish opaquely and fault deep in runtime execution.
        var root = SequenceNode("seq", [Node("write-one", Text("one"))]);
        var workflowVersion = WorkflowVersion(root);
        var versionStore = new FakeVersionStore(workflowVersion);
        var compiler = TestCompiler.Create(
            versionStore,
            new FakeActivityVersionStore([_writeLineActivity, _sequenceActivity]),
            new DefaultActivityStructureService([]),
            TestWellKnownTypeRegistry.Create());
        var handler = Handler(layout: null, compiler, versionStore);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(
            () => handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Contains(SequenceActivity.StructureKind, exception.Message);
        Assert.Contains("no structure handler", exception.Message);
    }

    [Fact]
    public async Task RejectsWorkflowWithoutRootActivity()
    {
        var workflowVersion = WorkflowVersion(rootActivity: null);
        var handler = Handler(workflowVersion);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Contains("root activity", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublishesNonLiteralInputAsExpressionBinding()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", new WorkflowArgumentState("Text", new ArgumentValue("\"Hello \" + \"World\"", "JavaScript"), null, null, null, null)));
        var handler = Handler(workflowVersion);

        var view = await handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var executable = await _store.FindAsync(view.ArtifactId);

        Assert.NotNull(executable);
        var binding = executable.NodesById["write-one"].InputBindings["Text"];
        Assert.Equal(RuntimeInputBindingSource.Expression, binding.Source);
        Assert.Equal("JavaScript", binding.Expression!.Language);
        Assert.Equal("\"Hello \" + \"World\"", binding.Expression.Expression);
    }

    [Fact]
    public async Task RejectsExpressionInputWithoutExpressionText()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", new WorkflowArgumentState("Text", new ArgumentValue("", "JavaScript"), null, null, null, null)));
        var handler = Handler(workflowVersion);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Contains("no expression text", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsUnknownActivityVersionId()
    {
        var workflowVersion = WorkflowVersion(new ActivityNode("missing", "missing-activity", [Text("one")], []));
        var handler = Handler(workflowVersion);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Contains("missing-activity", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Activity definition version", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Workflow definition version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PropagatesTypedCompilationExceptionForUnknownVersionId()
    {
        // #397: publishing a VersionId the store cannot resolve used to bubble a raw ArgumentException from
        // version-source resolution (which ran before the compiler's guarded region) and the handler then
        // rewrapped every compilation failure into a bare ArgumentException, erasing the type. The handler now
        // lets the typed WorkflowExecutableCompilationException propagate untouched.
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var handler = Handler(workflowVersion);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => handler.Handle(new PublishWorkflow("missing-version"), CancellationToken.None));

        Assert.Contains("missing-version", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComputesDifferentArtifactIdWhenSequenceOrderChanges()
    {
        var firstWorkflowVersion = WorkflowVersion(SequenceNode(
            "sequence",
            [
                Node("write-one", Text("one")),
                Node("write-two", Text("two")),
                Node("write-three", Text("three"))
            ]));
        var secondWorkflowVersion = WorkflowVersion(SequenceNode(
            "sequence",
            [
                Node("write-three", Text("three")),
                Node("write-one", Text("one")),
                Node("write-two", Text("two"))
            ]));
        var firstView = await Handler(firstWorkflowVersion, _writeLineActivity, _sequenceActivity).Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var secondView = await Handler(secondWorkflowVersion, _writeLineActivity, _sequenceActivity).Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        Assert.NotEqual(firstView.ArtifactId, secondView.ArtifactId);
        Assert.NotEqual(firstView.ArtifactHash, secondView.ArtifactHash);
    }

    [Fact]
    public async Task ComputesDifferentArtifactIdWhenLiteralInputTypeMetadataChanges()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("1")));
        var stringView = await Handler(workflowVersion, ActivityVersion("activity-write-line", "Text", new TypeReference("String")))
            .Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var integerView = await Handler(workflowVersion, ActivityVersion("activity-write-line", "Text", new TypeReference("Int32")))
            .Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        Assert.NotEqual(stringView.ArtifactId, integerView.ArtifactId);
        Assert.NotEqual(stringView.ArtifactHash, integerView.ArtifactHash);
    }

    private readonly InMemoryWorkflowExecutableSourceReferenceStore _referenceStore = new();

    private static PublishWorkflow NamedPublish(string versionId, string slotName)
    {
        var request = new PublishWorkflow(versionId);
        var slotProperty = typeof(PublishWorkflow).GetProperty("SlotName");
        var actionProperty = typeof(PublishWorkflow).GetProperty("Action");

        Assert.NotNull(slotProperty);
        Assert.NotNull(actionProperty);

        slotProperty.SetValue(request, slotName);
        var actionType = Nullable.GetUnderlyingType(actionProperty.PropertyType) ?? actionProperty.PropertyType;
        actionProperty.SetValue(request, Enum.Parse(actionType, "PublishSideBySide"));
        return request;
    }

    private async Task InvokeSlotLifecycleHandlerAsync(string operation, string definitionId, string slotName)
    {
        var assembly = typeof(PublishWorkflowRequestHandler).Assembly;
        var handlerType = assembly.GetType($"Elsa.Workflows.Publishing.Api.Handlers.{operation}PublicationSlotRequestHandler");
        var requestType = assembly.GetType($"Elsa.Workflows.Publishing.Api.Requests.{operation}PublicationSlot");

        Assert.NotNull(handlerType);
        Assert.NotNull(requestType);

        var request = Activator.CreateInstance(requestType, definitionId, slotName);
        Assert.NotNull(request);

        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowExecutableStore>(_store);
        services.AddSingleton<IWorkflowExecutableSourceReferenceStore>(_referenceStore);
        services.AddSingleton<IWorkflowTriggerBindingStore>(_bindingStore);
        services.AddSingleton<IPublicationSlotStore>(_slotStore);
        services.AddSingleton<IPublicationRecordStore>(_publicationStore);
        services.AddSingleton<IPublicationPolicyStore>(_policyStore);
        services.AddSingleton<IPublicationProjectionIntentStore>(_intentStore);
        services.AddSingleton<IWorkflowExecutableRootWriteLeaseManager>(TestRootWriteLeases.Create(_store));
        var extractor = new WorkflowTriggerBindingExtractor([]);
        services.AddSingleton<IWorkflowTriggerBindingExtractor>(extractor);
        services.AddSingleton<IWorkflowTriggerIndexer>(new WorkflowTriggerIndexer(extractor, _bindingStore));
        new WorkflowsPublishingApiFeature().ConfigureServices(services);

        await using var provider = services.BuildServiceProvider();
        var handler = ActivatorUtilities.CreateInstance(provider, handlerType);
        var handleMethod = handlerType.GetMethod("Handle", [requestType, typeof(CancellationToken)]);
        Assert.NotNull(handleMethod);

        var invocation = handleMethod.Invoke(handler, [request, CancellationToken.None]);
        var task = Assert.IsAssignableFrom<Task>(invocation);
        await task;
    }

    private PublishWorkflowRequestHandler Handler(WorkflowDefinitionVersion workflowVersion) =>
        Handler(workflowVersion, _writeLineActivity);

    private PublishWorkflowRequestHandler Handler(WorkflowDefinitionVersion workflowVersion, params ActivityDefinitionVersion[] activityVersions) =>
        Handler(workflowVersion, layout: null, activityVersions);

    private PublishWorkflowRequestHandler Handler(
        WorkflowDefinitionVersion workflowVersion,
        WorkflowDefinitionVersionLayout? layout,
        params ActivityDefinitionVersion[] activityVersions)
    {
        var versionStore = new FakeVersionStore(workflowVersion);
        var compiler = TestCompiler.Create(
            versionStore,
            new FakeActivityVersionStore(activityVersions.ToList()),
            _activityStructureService,
            TestWellKnownTypeRegistry.Create());
        return Handler(layout, compiler, versionStore);
    }

    private (PublishWorkflowRequestHandler Handler, RecordingWorkflowExecutableCompiler Compiler) RecordingHandler(
        WorkflowDefinitionVersion workflowVersion)
    {
        var versionStore = new FakeVersionStore(workflowVersion);
        var compiler = new RecordingWorkflowExecutableCompiler(TestCompiler.Create(
            versionStore,
            new FakeActivityVersionStore([_writeLineActivity]),
            _activityStructureService,
            TestWellKnownTypeRegistry.Create()));
        return (Handler(layout: null, compiler, versionStore), compiler);
    }

    private PublishWorkflowRequestHandler Handler(
        WorkflowDefinitionVersionLayout? layout,
        IWorkflowExecutableCompiler compiler,
        IWorkflowDefinitionVersionStore versionStore)
    {
        var extractor = new WorkflowTriggerBindingExtractor([]);
        IWorkflowTriggerIndexer indexer = new WorkflowTriggerIndexer(extractor, _bindingStore);
        var reconciler = new PublicationProjectionReconciler(
            _intentStore,
            _store,
            indexer,
            _bindingStore,
            TimeProvider.System);
        return new(
            compiler,
            _store,
            _referenceStore,
            extractor,
            _bindingStore,
            new FakeLayoutStore(layout),
            TestRootWriteLeases.Create(_store),
            _policyStore,
            new PublicationPolicyResolver(),
            _slotStore,
            _publicationStore,
            new PublicationPreflightService(),
            new PublicationActivator(_slotStore, _publicationStore, reconciler, TimeProvider.System),
            TimeProvider.System,
            workflowVersionStore: versionStore,
            snapshotReviews: _snapshotReviews,
            authoredInputsSidecar: new WorkflowExecutableAuthoredInputsSidecar(new ActivityTreeProjector(_activityStructureService)));
    }

    private static WorkflowPublicationPreflightPlan SnapshotPlan() =>
        new(
            new ResolvedPublicationAction(
                "definition-1",
                "snapshot",
                PublicationAction.Replace,
                "default",
                PublicationPolicySource.Host,
                PolicyRevision: 0),
            Slot: null,
            new PublicationPreflightResult(true, [], []),
            CandidateClaims: []);

    private static WorkflowDefinitionVersion WorkflowVersion(ActivityNode? rootActivity) =>
        WorkflowVersion(rootActivity, "definition-1", "version-1", "1.0.0");

    private static WorkflowDefinitionVersion WorkflowVersion(ActivityNode? rootActivity, string definitionId, string versionId, string version) =>
        new(definitionId, version)
        {
            Id = versionId,
            Definition = new WorkflowDefinition { Id = definitionId, Name = "Demo" },
            State = new WorkflowDefinitionState([], rootActivity, [], [], null)
        };

    private static ActivityNode Node(string nodeId, params WorkflowArgumentState[] inputs) =>
        Node(nodeId, structure: null, inputs);

    private static ActivityNode Node(
        string nodeId,
        ActivityNodeStructure? structure,
        params WorkflowArgumentState[] inputs) =>
        new(
            nodeId,
            "activity-write-line",
            inputs,
            Outputs: [],
            Structure: structure);

    private static ActivityNode SequenceNode(
        string nodeId,
        IReadOnlyCollection<ActivityNode> activities) =>
        new(
            nodeId,
            "activity-sequence",
            Inputs: [],
            Outputs: [],
            Structure: new ActivityNodeStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new SequenceAuthoredStructure(activities))));

    private static ActivityNode FlowchartNode(
        string nodeId,
        IReadOnlyCollection<ActivityNode> activities,
        IReadOnlyCollection<FlowchartConnection> connections,
        string? startNodeId) =>
        new(
            nodeId,
            "activity-flowchart",
            Inputs: [],
            Outputs: [],
            Structure: new ActivityNodeStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new FlowchartAuthoredStructure(activities, connections, startNodeId))));

    private static WorkflowArgumentState Text(string value) =>
        new("Text", new ArgumentValue(value, "Literal"), null, null, null, null);

    private static ActivityDefinitionVersion ActivityVersion(string id, string inputName, TypeReference inputType) =>
        ActivityVersion(id, "Test.WriteLine", [new InputDefinition(inputName, inputName, inputType, null, inputName, null, false)]);

    private static ActivityDefinitionVersion ActivityVersion(string id, string activityTypeKey) =>
        ActivityVersion(id, activityTypeKey, []);

    private static ActivityDefinitionVersion ActivityVersion(string id, string activityTypeKey, IReadOnlyCollection<InputDefinition> inputs, params ActivityDesignFacet[] designFacets) =>
        new("1.0.0", "activity-definition-1")
        {
            Id = id,
            Definition = new ActivityDefinition
            {
                Id = "activity-definition-1",
                ActivityTypeKey = activityTypeKey,
                Category = "Test"
            },
            ProviderKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ProviderSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            ConsumerKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ConsumerSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            DescriptorPayload = JsonSerializer.SerializeToElement(new ClrActivityDescriptor(TestActivityAliases.ForActivityVersionId(id))),
            Inputs = inputs,
            DesignFacets = designFacets
        };

    // Mirrors the structure facet ClrAssemblyScanner projects from an [ActivityStructure] declaration.
    private static ActivityDesignFacet StructureFacet(string kind, string schemaVersion) =>
        new(
            kind,
            schemaVersion,
            JsonSerializer.SerializeToElement(new
            {
                mode = "generic",
                supportsScopedVariables = false,
                slots = Array.Empty<object>(),
                initialPayload = new { }
            }));

    private static IActivityStructureService ActivityStructureService()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActivityStructureService, DefaultActivityStructureService>();
        new ActivitiesSequenceFeature().ConfigureServices(services);
        new ActivitiesFlowchartFeature().ConfigureServices(services);

        return services.BuildServiceProvider().GetRequiredService<IActivityStructureService>();
    }

    private sealed class FakeVersionStore(WorkflowDefinitionVersion version) : IWorkflowDefinitionVersionStore
    {
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            version.Id == versionId
                ? Task.FromResult(version)
                : throw new ArgumentException($"Workflow definition version with id '{versionId}' does not exist");

        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeLayoutStore(WorkflowDefinitionVersionLayout? layout) : IWorkflowDefinitionVersionLayoutStore
    {
        public Task<WorkflowDefinitionVersionLayout?> FindByVersionIdAsync(string workflowDefinitionVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(layout);
    }

    private sealed class RecordingWorkflowExecutableCompiler(IWorkflowExecutableCompiler inner) : IWorkflowExecutableCompiler
    {
        public List<WorkflowExecutableCompileRequest> Requests { get; } = [];

        public ValueTask<WorkflowExecutable> CompileAsync(
            WorkflowExecutableCompileRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return inner.CompileAsync(request, cancellationToken);
        }
    }
}
