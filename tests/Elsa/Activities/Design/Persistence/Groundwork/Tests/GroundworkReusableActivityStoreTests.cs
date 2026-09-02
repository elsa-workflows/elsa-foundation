using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Locking.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Exceptions;
using System.Collections.Concurrent;
using Xunit;

namespace Elsa.Activities.Design.Persistence.Groundwork.Tests;

public sealed class GroundworkReusableActivityStoreTests
{
    [Theory]
    [InlineData(null, ActivityDefinitionVersionResolutionKind.AuthorableActivity)]
    [InlineData("draft-legacy", ActivityDefinitionVersionResolutionKind.ReusableTemplateBoundary)]
    public void Legacy_publication_without_resolution_kind_uses_existing_draft_provenance(
        string? sourceDraftId,
        ActivityDefinitionVersionResolutionKind expected)
    {
        var json = JsonSerializer.SerializeToNode(Publication("publication-legacy", "version-legacy", "definition-legacy", "1.0.0"), GroundworkActivitiesDesignJson.Options)!.AsObject();
        json.Remove("resolutionKind");
        if (sourceDraftId is null)
            json.Remove("sourceDraftId");
        else
            json["sourceDraftId"] = sourceDraftId;
        var publication = json.Deserialize<ActivityDefinitionVersionPublication>(GroundworkActivitiesDesignJson.Options)!;
        Assert.Equal(ActivityDefinitionVersionResolutionKind.Unspecified, publication.ResolutionKind);
        Assert.Equal(expected, publication.ResolveWorkflowResolutionKind());
    }

    [Fact]
    public async Task CreateDefinition_Commits_Definition_Authoring_Draft_And_Layout()
    {
        using var harness = await SeededAsync();
        Assert.Single(harness.Persistence.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind));
        Assert.Single(harness.Persistence.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind));
        Assert.Single(harness.Persistence.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind));
        Assert.Single(harness.Persistence.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind));
        Assert.Equal("definition-1", (await harness.Stores.FindAsync("definition-1"))!.DefinitionId);
        Assert.Equal("draft-1", (await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync("draft-1"))!.Id);
        Assert.Equal("node-1", Assert.Single((await harness.Stores.FindDraftLayoutAsync("draft-1"))!.Records).NodeId);
    }

    [Fact]
    public async Task Authoring_state_reads_are_scoped_and_deterministic()
    {
        using var harness = await SeededAsync();
        var states = await harness.Stores.ListAsync(["definition-1", "missing", "definition-1"]);
        Assert.Single(states);
        Assert.Equal("definition-1", states[0].DefinitionId);
    }

    [Fact]
    public async Task Draft_reads_round_trip_contract_provider_and_revision()
    {
        using var harness = await SeededAsync();
        var draft = await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync("draft-1");
        Assert.NotNull(draft);
        Assert.Equal(0, draft!.Revision);
        Assert.Equal("workflow", draft.State.Provider.ProviderKey);
    }

    [Fact]
    public async Task Draft_lists_are_ordered_by_identity()
    {
        using var harness = await SeededAsync();
        var drafts = await ((IActivityDefinitionDraftStore)harness.Stores).ListByDefinitionAsync("definition-1");
        Assert.Equal(["draft-1"], drafts.Select(draft => draft.Id));
    }

    [Fact]
    public async Task Draft_layout_reads_round_trip_records()
    {
        using var harness = await SeededAsync();
        var layout = await harness.Stores.FindDraftLayoutAsync("draft-1");
        Assert.Equal("node-1", Assert.Single(layout!.Records).NodeId);
    }

    [Fact]
    public async Task Definition_authority_round_trips_without_navigation_artifacts()
    {
        using var harness = await SeededAsync();
        var authoring = await harness.Stores.FindAsync("definition-1");
        Assert.Equal(ActivityContentAuthorityKind.Design, authoring!.ContentAuthority.Kind);
        Assert.Equal("authoring-1", authoring.Id);
    }

    [Fact]
    public async Task Definition_and_draft_rows_share_the_explicit_tenant_scope()
    {
        using var harness = await SeededAsync();
        Assert.Equal("tenant-a", (await harness.Stores.FindAsync("definition-1"))!.TenantId);
        Assert.Equal("tenant-a", (await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync("draft-1"))!.TenantId);
    }

    [Fact]
    public async Task UpdateDefinitionPresentation_Uses_Document_Cas_And_Preserves_Immutable_Identity_State()
    {
        using var harness = await SeededAsync();
        var updated = await harness.Stores.ExecuteAsync(new UpdateActivityDefinitionPresentationRequest(
            "definition-1", "tenant-a", "Finance", "Calculate invoice total", "Updated description",
            new DateTimeOffset(2026, 1, 1, 2, 0, 0, TimeSpan.Zero)));

        var persisted = await new GroundworkActivityDefinitionStore(harness.Persistence.Store).GetAsync("definition-1");
        var authoring = await harness.Stores.FindAsync("definition-1");
        Assert.Equal("Finance", persisted.Category);
        Assert.Equal("Calculate invoice total", persisted.DisplayName);
        Assert.Equal("Updated description", persisted.Description);
        Assert.Equal("Acme.Sample", persisted.ActivityTypeKey);
        Assert.Equal(ActivityContentAuthorityKind.Design, authoring!.ContentAuthority.Kind);
        Assert.Equal("Finance", updated.Category);
    }

    [Fact]
    public async Task UpdateDefinitionPresentation_Rejects_Source_Authority_Without_Writes()
    {
        using var harness = Harness.Create();
        await harness.Stores.ExecuteAsync(CreateRequest(ActivityContentAuthorityKind.ProviderSource));

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Stores.ExecuteAsync(
            new UpdateActivityDefinitionPresentationRequest(
                "definition-1", "tenant-a", "Finance", "Updated", null,
                new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero))));

        Assert.Equal("Samples", (await new GroundworkActivityDefinitionStore(harness.Persistence.Store)
            .GetAsync("definition-1")).Category);
    }

    [Fact]
    public async Task UpdateDefinitionPresentation_Rejects_A_Concurrent_Document_Write_By_Cas()
    {
        using var harness = await SeededAsync();
        var definition = await new GroundworkActivityDefinitionStore(harness.Persistence.Store).GetAsync("definition-1");
        var current = await harness.Persistence.Store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind, definition.Id);
        var request = GroundworkV2ActivityDesignDocumentWriter.ToSaveRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDefinitionCollection,
            ActivitiesDesignStorageManifest.SchemaVersion,
            definition,
            GroundworkActivitiesDesignJson.Options) with { ExpectedVersion = current!.Version };

        await harness.Persistence.Store.SaveAsync(request);
        await Assert.ThrowsAsync<ActivityDesignWriteConflictException>(() => harness.Persistence.Store.SaveAsync(request));
    }

    [Fact]
    public async Task Authoring_reads_preserve_fork_metadata_shape()
    {
        using var harness = await SeededAsync();
        var authoring = await harness.Stores.FindAsync("definition-1");
        Assert.Null(authoring!.ForkedFrom);
        Assert.Equal("design", authoring.ContentAuthority.AuthorityKey);
    }

    [Fact]
    public async Task Draft_state_preserves_authored_label_projection()
    {
        using var harness = await SeededAsync();
        var draft = await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync("draft-1");
        Assert.Equal("initial", draft!.State.Options["label"]);
    }

    [Fact]
    public async Task Layout_rows_preserve_json_node_payloads()
    {
        using var harness = await SeededAsync();
        var record = Assert.Single((await harness.Stores.FindDraftLayoutAsync("draft-1"))!.Records);
        Assert.Equal(JsonValueKind.Object, record.Data.ValueKind);
    }

    [Fact]
    public async Task Scoped_store_does_not_return_rows_from_another_tenant()
    {
        using var harness = await SeededAsync();
        harness.PersistenceAccess.Current = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b"));
        Assert.Null(await harness.Stores.FindAsync("definition-1"));
    }

    [Fact]
    public async Task Fork_candidate_apply_commits_exact_reserved_identity_receipt_and_management_projection_atomically()
    {
        using var harness = Harness.Create();
        var candidate = ForkCandidate();
        await SaveForkSourceAsync(harness, candidate);
        await harness.Stores.ExecuteAsync(new SaveActivityForkCandidateRequest(candidate));

        var request = ForkApply(candidate, "operation-1");
        var applied = await harness.Stores.ExecuteAsync(request);
        var replay = await harness.Stores.ExecuteAsync(request);

        Assert.False(applied.AlreadyApplied);
        Assert.True(replay.AlreadyApplied);
        Assert.Equal(candidate.ReservedDefinition.Id, applied.Receipt.DefinitionId);
        Assert.Equal(candidate.ReservedDefinition.ActivityTypeKey, applied.Receipt.ActivityTypeKey);
        Assert.Equal(candidate.ReservedDraft.Id, applied.Receipt.DraftId);
        Assert.NotNull(await harness.Stores.FindReceiptAsync(request.ReceiptId));
        Assert.Equal(ActivityForkCandidateStatus.Applied, (await harness.Stores.FindCandidateAsync(candidate.Id))!.Status);
        Assert.Equal(candidate.ReservedDefinition.ActivityTypeKey,
            (await new GroundworkActivityDefinitionStore(harness.Persistence.Store)
                .GetAsync(candidate.ReservedDefinition.Id)).ActivityTypeKey);
        Assert.Equal(candidate.ReservedDraft.Id,
            (await ((IActivityDefinitionDraftStore)harness.Stores)
                .FindAsync(candidate.ReservedDraft.Id))!.Id);
        Assert.Single(harness.Persistence.Rows(ActivitiesDesignStorageManifest.ActivityForkReceiptDocumentKind));
    }

    [Fact]
    public async Task Fork_candidate_collision_rejects_without_receipt_or_partial_reserved_identity_writes()
    {
        using var harness = Harness.Create();
        var candidate = ForkCandidate();
        await SaveForkSourceAsync(harness, candidate);
        await harness.Stores.ExecuteAsync(new SaveActivityForkCandidateRequest(candidate));
        await harness.Stores.ExecuteAsync(CreateRequest(tenantId: null));

        await Assert.ThrowsAsync<ActivityForkCollisionException>(() =>
            harness.Stores.ExecuteAsync(ForkApply(candidate, "operation-1")));

        Assert.Null(await harness.Stores.FindReceiptAsync(ActivityForkReceiptIdentity.Compute(null, "actor-a", "operation-1")));
        Assert.Equal(ActivityForkCandidateStatus.Reserved, (await harness.Stores.FindCandidateAsync(candidate.Id))!.Status);
        Assert.Null(await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync(candidate.ReservedDraft.Id));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => new GroundworkActivityDefinitionStore(harness.Persistence.Store)
            .GetAsync(candidate.ReservedDefinition.Id));
    }

    [Fact]
    public async Task Fork_preview_retries_return_the_first_reservation_and_reject_changed_material()
    {
        using var harness = Harness.Create();
        var first = ForkCandidate();
        var retry = ForkCandidate(definitionId: "losing-definition", draftId: "losing-draft");
        var changed = ForkCandidate(
            definitionId: "changed-definition",
            draftId: "changed-draft",
            requestFingerprint: $"sha256:{new string('f', 64)}");

        var reserved = await harness.Stores.ExecuteAsync(new SaveActivityForkCandidateRequest(first));
        var replay = await harness.Stores.ExecuteAsync(new SaveActivityForkCandidateRequest(retry));
        await Assert.ThrowsAsync<ActivityForkPreviewIdempotencyConflictException>(() =>
            harness.Stores.ExecuteAsync(new SaveActivityForkCandidateRequest(changed)));

        Assert.Equal(first.ReservedDefinition.Id, reserved.ReservedDefinition.Id);
        Assert.Equal(first.ReservedDefinition.Id, replay.ReservedDefinition.Id);
        Assert.Single(harness.Persistence.Rows(ActivitiesDesignStorageManifest.ActivityForkCandidateDocumentKind));
    }

    [Fact]
    public async Task Fork_receipt_replay_rejects_changed_candidate_binding_without_writes()
    {
        using var harness = Harness.Create();
        var first = ForkCandidate();
        await SaveForkSourceAsync(harness, first);
        await harness.Stores.ExecuteAsync(new SaveActivityForkCandidateRequest(first));
        var firstRequest = ForkApply(first, "operation-1");
        await harness.Stores.ExecuteAsync(firstRequest);

        var second = ForkCandidate("candidate-2", "target-definition-2", "target-draft-2");
        await harness.Stores.ExecuteAsync(new SaveActivityForkCandidateRequest(second));
        var conflict = ForkApply(second, "operation-1") with { ReceiptId = firstRequest.ReceiptId };

        await Assert.ThrowsAsync<ActivityForkIdempotencyConflictException>(() => harness.Stores.ExecuteAsync(conflict));
        Assert.Null(await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync(second.ReservedDraft.Id));
        Assert.Equal(first.Id, (await harness.Stores.FindReceiptAsync(firstRequest.ReceiptId))!.CandidateId);
    }

    [Fact]
    public async Task Concurrent_fork_candidates_with_one_operation_identity_have_one_exact_winner()
    {
        using var harness = Harness.Create(new YieldingDistributedLockProvider());
        var first = ForkCandidate();
        var second = ForkCandidate("candidate-2", "target-definition-2", "target-draft-2", activityTypeKey: "Acme.Other");
        await SaveForkSourceAsync(harness, first);
        await SaveForkSourceAsync(harness, second);
        await harness.Stores.ExecuteAsync(new SaveActivityForkCandidateRequest(first));
        await harness.Stores.ExecuteAsync(new SaveActivityForkCandidateRequest(second));

        var firstRequest = ForkApply(first, "shared-operation");
        var secondRequest = ForkApply(second, "shared-operation") with { ReceiptId = firstRequest.ReceiptId };
        var attempts = await Task.WhenAll(
            CaptureAsync(() => harness.Stores.ExecuteAsync(firstRequest)),
            CaptureAsync(() => harness.Stores.ExecuteAsync(secondRequest)));

        Assert.Single(attempts, attempt => attempt.Result is not null);
        Assert.Single(attempts, attempt => attempt.Exception is ActivityForkIdempotencyConflictException);
        var receipt = await harness.Stores.FindReceiptAsync(firstRequest.ReceiptId);
        Assert.NotNull(receipt);
        Assert.Contains(receipt!.CandidateId, new[] { first.Id, second.Id });
        Assert.Equal(
            receipt.CandidateId == first.Id ? first.ReservedDefinition.Id : second.ReservedDefinition.Id,
            receipt.DefinitionId);
    }

    [Fact]
    public async Task Fork_candidate_retention_prunes_bounded_candidates_but_preserves_append_only_receipt()
    {
        using var harness = Harness.Create();
        var candidate = ForkCandidate();
        await SaveForkSourceAsync(harness, candidate);
        await harness.Stores.ExecuteAsync(new SaveActivityForkCandidateRequest(candidate));
        var request = ForkApply(candidate, "operation-1");
        await harness.Stores.ExecuteAsync(request);

        var pruned = await harness.Stores.ExecuteAsync(candidate.RetainUntil, 1);

        Assert.Equal(1, pruned);
        Assert.Null(await harness.Stores.FindCandidateAsync(candidate.Id));
        Assert.NotNull(await harness.Stores.FindReceiptAsync(request.ReceiptId));
        Assert.Single(harness.Persistence.Rows(ActivitiesDesignStorageManifest.ActivityForkReceiptDocumentKind));
    }

    [Fact]
    public async Task Replace_Uses_Revision_Cas_And_Updates_Draft_And_Layout_Together()
    {
        using var harness = await SeededAsync();
        await harness.Stores.ExecuteAsync(new ReplaceActivityDraftRequest(
            "draft-1", 0, State("updated"), [LayoutRecord("node-2")]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Stores.ExecuteAsync(
            new ReplaceActivityDraftRequest("draft-1", 0, State("stale"), [LayoutRecord("stale-node")] )));

        var draft = await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync("draft-1");
        var layout = await harness.Stores.FindDraftLayoutAsync("draft-1");
        Assert.Equal(1, draft!.Revision);
        Assert.Equal("updated", draft.State.Options["label"]);
        Assert.Equal(1, layout!.Revision);
        Assert.Equal("node-2", Assert.Single(layout.Records).NodeId);
    }

    [Fact]
    public async Task ApplyContractProposal_Uses_Exact_Provider_Binding_And_Changes_Only_Contract_And_Revisions()
    {
        using var harness = await SeededAsync();
        var before = (await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync("draft-1"))!;
        var fingerprint = ActivityProviderManifestFingerprint.Compute(before.State.Provider);
        var contract = new ActivityContract(
            "proposal",
            [new("note", "Note", new("String", Elsa.Primitives.Models.CollectionKind.Single), true, true, null, "elsa.json")],
            [],
            []);

        var applied = await harness.Stores.ExecuteAsync(new ApplyActivityContractProposalRequest(
            before.Id, before.TenantId, before.Revision, before.State.Provider.ProviderKey,
            before.State.Provider.SchemaVersion, fingerprint, contract));

        Assert.Equal(1, applied.Revision);
        Assert.Equal("proposal", applied.State.Contract.ContractSchemaVersion);
        Assert.Equal("node-1", Assert.Single((await harness.Stores.FindDraftLayoutAsync(before.Id))!.Records).NodeId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Stores.ExecuteAsync(
            new ApplyActivityContractProposalRequest(
                before.Id, before.TenantId, applied.Revision, before.State.Provider.ProviderKey,
                before.State.Provider.SchemaVersion, "sha256:stale", contract)));
    }

    [Fact]
    public async Task ApplyContractProposal_Rejects_Stale_Manifest_Fingerprint_Without_Writes()
    {
        using var harness = await SeededAsync();
        var before = (await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync("draft-1"))!;

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Stores.ExecuteAsync(
            new ApplyActivityContractProposalRequest(
                before.Id, before.TenantId, before.Revision,
                before.State.Provider.ProviderKey,
                before.State.Provider.SchemaVersion,
                "sha256:stale", new("proposal", [], [], []))));

        var persisted = (await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync(before.Id))!;
        Assert.Equal(before.Revision, persisted.Revision);
        Assert.Equal(before.State.Contract.ContractSchemaVersion, persisted.State.Contract.ContractSchemaVersion);
        Assert.Equal(before.Revision, (await harness.Stores.FindDraftLayoutAsync(before.Id))!.Revision);
    }

    [Theory]
    [InlineData(ActivityContentAuthorityKind.Design, "unexpected-head")]
    [InlineData(ActivityContentAuthorityKind.ProviderSource, null)]
    public async Task CreateDraft_Rejects_Stale_Head_Or_Provider_Authority_Without_Writes(
        ActivityContentAuthorityKind authority,
        string? expectedHead)
    {
        using var harness = Harness.Create();
        await harness.Stores.ExecuteAsync(CreateRequest(authority, headVersionId: "head"));
        var request = new CreateActivityDraftRequest(
            Draft("draft-2", "definition-1"),
            DraftLayout("draft-layout-2", "draft-2"),
            expectedHead);

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Stores.ExecuteAsync(request));
        Assert.Null(await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync("draft-2"));
        Assert.Null(await harness.Stores.FindDraftLayoutAsync("draft-2"));
    }

    [Fact]
    public async Task Validation_Is_Pinned_To_The_Current_Draft_Revision()
    {
        using var harness = await SeededAsync();
        await harness.Stores.ExecuteAsync(Validation(0));

        await harness.Stores.ExecuteAsync(new ReplaceActivityDraftRequest(
            "draft-1", 0, State("updated"), [LayoutRecord("node-2")]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Stores.ExecuteAsync(Validation(0, "validation-stale")));

        var stored = await ((IActivityDraftValidationStore)harness.Stores).FindAsync("draft-1", 0);
        Assert.NotNull(stored);
        Assert.Equal("activity.test", Assert.Single(stored!.Diagnostics).Code);
        Assert.Null(await ((IActivityDraftValidationStore)harness.Stores).FindAsync("draft-1", 1));
    }

    [Fact]
    public async Task Publication_Layout_Edge_Lifecycle_And_Dependency_Reads_RoundTrip()
    {
        using var harness = Harness.Create();
        await harness.SaveAsync(new ActivityDefinition { Id = "definition-parent", ActivityTypeKey = "Acme.Parent", Category = "Samples" });
        await harness.SaveAsync(new ActivityDefinitionAuthoringState
        {
            Id = "authoring-parent", DefinitionId = "definition-parent",
            ContentAuthority = new(ActivityContentAuthorityKind.Design, "design"), HeadVersionId = "version-parent"
        });
        await harness.SaveAsync(Publication("publication-parent", "version-parent", "definition-parent", "1.0.0", 1));
        await harness.SaveAsync(Publication("publication-child", "version-child", "definition-child", "2.0.0"));
        await harness.SaveAsync(new ActivityDefinitionVersionLayout
        {
            Id = "version-layout-parent", DefinitionVersionId = "version-parent", Records = [LayoutRecord("node-parent")]
        });
        await harness.SaveAsync(new ActivityDependencyEdge
        {
            Id = "edge-1", OwnerVersionId = "version-parent", OwnerTemplateHash = "hash-version-parent",
            DependencyVersionId = "version-child", DependencyTemplateHash = "hash-version-child", OccurrenceId = "occurrence-1",
            NodeOrigin = [new ActivityNodeOrigin("Node", "child-node")]
        });

        var publication = await ((IActivityDefinitionVersionPublicationStore)harness.Stores).FindAsync("version-parent");
        var layout = await harness.Stores.FindVersionLayoutAsync("version-parent");
        var edges = await harness.Stores.ListOutboundAsync("version-parent");
        var page = await harness.Stores.ReadAsync(new(
            "version-parent", new(ActivityDependencyDirection.Outbound, false, new HashSet<string> { "Versions" }),
            null, null, 0, 10));

        Assert.Equal("version-parent", publication!.DefinitionVersionId);
        Assert.Equal(1, publication.DirectDependencyCount);
        Assert.Equal(2, publication.ClosedTemplateCount);
        Assert.Equal("runtime.graph", Assert.Single(publication.RuntimeRequirements).ConsumerKey);
        Assert.Equal("node-parent", Assert.Single(layout!.Records).NodeId);
        Assert.Equal("edge-1", Assert.Single(edges).Id);
        Assert.Equal(ActivityDependencyConsistencyKind.DerivedProjection, page.Consistency.Kind);
        Assert.Equal("version-child", Assert.Single(page.Items).Dependency.VersionId);

        var retired = await harness.Stores.ExecuteAsync(new ChangeActivityVersionLifecycleRequest(
            "version-parent", ActivityDefinitionVersionLifecycle.Active,
            ActivityDefinitionVersionLifecycle.Retired, "No longer selected", "tenant-a"));
        Assert.Equal(ActivityDefinitionVersionLifecycle.Retired, retired.Lifecycle);
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Stores.ExecuteAsync(
            new ChangeActivityVersionLifecycleRequest(
                "version-parent", ActivityDefinitionVersionLifecycle.Active,
                ActivityDefinitionVersionLifecycle.Revoked, "Stale administrator view", "tenant-a")));
        Assert.Equal(ActivityDefinitionVersionLifecycle.Retired,
            (await ((IActivityDefinitionVersionPublicationStore)harness.Stores)
                .FindAsync("version-parent"))!.Lifecycle);
    }

    [Fact]
    public async Task Mixed_owner_dependency_projection_rebuilds_with_a_bound_watermark()
    {
        using var harness = Harness.Create();
        await harness.SaveAsync(Publication("publication-child", "version-child", "definition-child", "2.0.0"));
        var projection = new GroundworkActivityDependencyProjection(harness.Persistence.Store, harness.Stores);
        var child = new ActivityDefinitionReference("ActivityVersion", "definition-child", "version-child", "2.0.0", TemplateHash: "hash-version-child");
        var items = new[]
        {
            DependencyItem("activity-draft-use", new("ActivityDraft", "definition-activity-owner", DraftId: "draft-a", Revision: 4), child, "node-a"),
            DependencyItem("workflow-version-use", new("WorkflowVersion", "definition-workflow-owner", "workflow-v1", "1.0.0"), child, "node-w")
        };
        var asOf = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

        await projection.RebuildAsync(new("rebuild-1", 17, asOf, items));
        var first = await projection.ReadAsync(new(
            "version-child", new(ActivityDependencyDirection.Inbound, false, new HashSet<string>(["Versions", "Drafts"])),
            null, null, 0, 1));
        var second = await projection.ReadAsync(new(
            "version-child", new(ActivityDependencyDirection.Inbound, false, new HashSet<string>(["Versions", "Drafts"])),
            null, first.Watermark, first.NextOffset!.Value, 1));

        Assert.Equal(17, first.Consistency.AsOfSequence);
        Assert.Equal(asOf, first.Consistency.AsOf);
        Assert.Equal("rebuild-1", first.Consistency.RebuildId);
        Assert.Equal(["ActivityDraft", "WorkflowVersion"], first.Items.Concat(second.Items).Select(x => x.Owner.Kind));
        await projection.RebuildAsync(new("rebuild-2", 18, asOf.AddMinutes(1), items));
        await Assert.ThrowsAsync<ActivityDependencyWatermarkExpiredException>(() => projection.ReadAsync(new(
            "version-child", new(ActivityDependencyDirection.Inbound, false, new HashSet<string>(["Versions"])),
            null, first.Watermark, 0, 10)));
    }

    [Fact]
    public async Task Recommendation_move_picker_and_lifecycle_replacement_share_exact_groundwork_cas()
    {
        using var harness = Harness.Create();
        await harness.Stores.ExecuteAsync(CreateRequest(headVersionId: "version-2", recommendedVersionId: "version-1"));
        var first = Publication("publication-1", "version-1", "definition-1", "1.0.0");
        var second = Publication("publication-2", "version-2", "definition-1", "2.0.0");
        first.TenantId = second.TenantId = "tenant-a";
        await harness.SaveAsync(first);
        await harness.SaveAsync(second);

        await harness.Stores.ExecuteAsync(new SetActivityDefinitionRecommendationRequest(
            "definition-1", "tenant-a", "version-2", "version-1", "version-2",
            ActivityDefinitionVersionLifecycle.Active,
            new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)));
        await harness.Stores.ExecuteAsync(new ChangeActivityVersionLifecycleRequest(
            "version-2", ActivityDefinitionVersionLifecycle.Active,
            ActivityDefinitionVersionLifecycle.Retired, "Superseded", "tenant-a",
            new("version-2", "version-2", ActivityRecommendationDisposition.Replace,
                "version-1", ActivityDefinitionVersionLifecycle.Active)));

        Assert.Equal("version-1", (await harness.Stores.FindAsync("definition-1"))!.RecommendedVersionId);
        Assert.Equal(ActivityDefinitionVersionLifecycle.Retired,
            (await ((IActivityDefinitionVersionPublicationStore)harness.Stores).FindAsync("version-2"))!.Lifecycle);
    }

    [Fact]
    public async Task CreateDefinition_Late_Batch_Conflict_Rolls_Back_Earlier_Writes()
    {
        using var harness = Harness.Create();
        await harness.SaveAsync(new ActivityDefinitionDraftLayout
        {
            Id = "draft-layout-1", DraftId = "unrelated-draft", Revision = 0, Records = []
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Stores.ExecuteAsync(CreateRequest()));
        Assert.Null(await new GroundworkActivityDefinitionStore(harness.Persistence.Store).FindAsync(
            new Elsa.Activities.Design.Persistence.Core.Filters.ActivityDefinitionFilter { Id = "definition-1" }));
        Assert.Null(await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync("draft-1"));
        Assert.Null(await harness.Persistence.Store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind, "authoring-1"));
        Assert.Single(harness.Persistence.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind));
        Assert.NotNull(await harness.Persistence.Store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind, "draft-layout-1"));
    }

    private static async Task<TestHarness> SeededAsync()
    {
        var persistence = ActivityDesignV2TestHarness.Create();
        var stores = CreateStores(persistence);
        await stores.ExecuteAsync(CreateRequest());
        return new TestHarness(persistence, stores);
    }

    private static GroundworkReusableActivityStores CreateStores(ActivityDesignV2TestHarness persistence)
    {
        var locks = new ImmediateDistributedLockProvider();
        return new(
            persistence.Store,
            new FixedClock(),
            locks,
            persistence.Store,
            new GroundworkActivityManagementProjectionWriter(persistence.Store, locks, persistence.Store));
    }

    private static CreateActivityDefinitionRequest CreateRequest(
        ActivityContentAuthorityKind authority = ActivityContentAuthorityKind.Design,
        string? headVersionId = null,
        string? recommendedVersionId = null,
        string? tenantId = "tenant-a")
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return new(
            new ActivityDefinition
            {
                Id = "definition-1", TenantId = tenantId, ActivityTypeKey = "Acme.Sample", Category = "Samples",
                CreatedAt = now, LastModifiedAt = now
            },
            new ActivityDefinitionAuthoringState
            {
                Id = "authoring-1", TenantId = tenantId, DefinitionId = "definition-1",
                ContentAuthority = new(authority, authority == ActivityContentAuthorityKind.Design ? "design" : "provider"),
                HeadVersionId = headVersionId, RecommendedVersionId = recommendedVersionId,
                CreatedAt = now, LastModifiedAt = now
            },
            new ActivityDefinitionDraft
            {
                Id = "draft-1", TenantId = tenantId, DefinitionId = "definition-1", Revision = 0,
                State = new(new("1", [], [], []), new("workflow", "1", JsonElement.Parse("{}")), new Dictionary<string, string> { ["label"] = "initial" }),
                CreatedAt = now, LastModifiedAt = now
            },
            new ActivityDefinitionDraftLayout
            {
                Id = "draft-layout-1", TenantId = tenantId, DraftId = "draft-1", Revision = 0,
                Records = [new("node-1", JsonElement.Parse("{}"))], CreatedAt = now, LastModifiedAt = now
            });
    }

    private static ActivityDefinitionDraft Draft(string id, string definitionId) => new()
    {
        Id = id,
        DefinitionId = definitionId,
        TenantId = "tenant-a",
        Revision = 0,
        State = State("initial"),
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        LastModifiedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
    };

    private static ActivityDefinitionDraftLayout DraftLayout(string id, string draftId) => new()
    {
        Id = id,
        DraftId = draftId,
        TenantId = "tenant-a",
        Revision = 0,
        Records = [LayoutRecord("node-2")],
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        LastModifiedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
    };

    private static ActivityDraftValidationState Validation(long revision, string id = "validation-1") => new()
    {
        Id = id,
        DraftId = "draft-1",
        Revision = revision,
        ValidatedAt = new DateTimeOffset(2026, 1, 1, 0, 1, 0, TimeSpan.Zero),
        Diagnostics =
        [
            new ActivityDiagnostic(
                "activity.test",
                ActivityDiagnosticSeverity.Warning,
                "Test diagnostic",
                new ActivityDiagnosticSubject("Draft", "draft-1", Revision: revision))
        ]
    };

    private static ActivityDefinitionVersionPublication Publication(
        string id,
        string versionId,
        string definitionId,
        string version,
        int directDependencies = 0) => new()
    {
        Id = id, DefinitionVersionId = versionId, DefinitionId = definitionId, Version = version,
        ResolutionKind = ActivityDefinitionVersionResolutionKind.ReusableTemplateBoundary, SourceDraftId = "draft-legacy",
        Contract = Contract(), Provider = Provider(), TemplateId = $"template-{versionId}", TemplateHash = $"hash-{versionId}",
        SourceReferenceId = $"source-{versionId}", ProviderFingerprint = "provider-fingerprint",
        DirectDependencyCount = directDependencies, ClosedTemplateCount = directDependencies + 1,
        RuntimeRequirements = [new ActivityRuntimeRequirementDeclaration("runtime.graph", "1")],
        PublishedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
    };

    private static ActivityForkCandidate ForkCandidate(
        string candidateId = "candidate-1",
        string definitionId = "target-definition-1",
        string draftId = "target-draft-1",
        string? requestFingerprint = null,
        string activityTypeKey = "Acme.Sample")
    {
        var createdAt = new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero);
        var definition = new ActivityDefinition
        {
            Id = definitionId, ActivityTypeKey = activityTypeKey, Category = "Samples", DisplayName = "Forked",
            CreatedAt = createdAt, LastModifiedAt = createdAt
        };
        var authoring = new ActivityDefinitionAuthoringState
        {
            Id = $"authoring-{definitionId}", DefinitionId = definitionId,
            ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design),
            ForkedFrom = new("source-definition", "source-version", "1.0.0"), CreatedAt = createdAt, LastModifiedAt = createdAt
        };
        var draft = new ActivityDefinitionDraft
        {
            Id = draftId, DefinitionId = definitionId, Revision = 1, SourceVersionId = "source-version",
            State = State("initial"), CreatedAt = createdAt, LastModifiedAt = createdAt
        };
        var layout = new ActivityDefinitionDraftLayout
        {
            Id = $"layout-{draftId}", DraftId = draftId, Revision = 1, Records = [LayoutRecord("node-1")],
            CreatedAt = createdAt, LastModifiedAt = createdAt
        };
        var sourceContract = Contract();
        var sourceProvider = Provider();
        return new()
        {
            Id = candidateId, CandidateId = $"public-{candidateId}", PreviewIdempotencyKey = $"preview-{candidateId}",
            RequestFingerprint = requestFingerprint ?? $"sha256:{new string('a', 64)}",
            AccessBindingFingerprint = $"sha256:{new string('b', 64)}", ActorId = "actor-a", AuthorizationProfile = "profile-a",
            SourceDefinitionId = "source-definition", SourceVersionId = "source-version", SourceVersion = "1.0.0",
            SourceLifecycle = ActivityDefinitionVersionLifecycle.Active,
            SourceProviderFingerprint = ActivityProviderManifestFingerprint.Compute(sourceProvider),
            TargetProviderFingerprint = $"sha256:{new string('d', 64)}", ReservedDefinition = definition,
            ReservedAuthoringState = authoring, ReservedDraft = draft, ReservedLayout = layout,
            SourceContractFingerprint = ActivityForkMaterialFingerprint.Compute(sourceContract),
            TargetContractFingerprint = $"sha256:{new string('e', 64)}", ExpiresAt = createdAt.AddMinutes(15),
            RetainUntil = createdAt.AddDays(1), RetentionKey = ActivityForkCandidateIdentity.RetentionKey(createdAt.AddDays(1)),
            CreatedAt = createdAt, LastModifiedAt = createdAt
        };
    }

    private static async Task SaveForkSourceAsync(Harness harness, ActivityForkCandidate candidate)
    {
        var createdAt = candidate.CreatedAt.AddHours(-1);
        await harness.SaveAsync(new ActivityDefinitionAuthoringState
        {
            Id = $"authoring-{candidate.SourceDefinitionId}", DefinitionId = candidate.SourceDefinitionId,
            ContentAuthority = new(ActivityContentAuthorityKind.ProviderSource, "source.provider"),
            HeadVersionId = candidate.SourceVersionId, RecommendedVersionId = candidate.SourceVersionId,
            CreatedAt = createdAt, LastModifiedAt = createdAt
        });
        await harness.SaveAsync(new ActivityDefinitionVersionPublication
        {
            Id = $"publication-{candidate.SourceVersionId}", DefinitionVersionId = candidate.SourceVersionId,
            DefinitionId = candidate.SourceDefinitionId, Version = candidate.SourceVersion, ActivityTypeKey = "Acme.Source",
            Contract = Contract(), Provider = Provider(), TemplateId = $"template-{candidate.SourceVersionId}",
            TemplateHash = $"hash-{candidate.SourceVersionId}", SourceReferenceId = $"source-{candidate.SourceVersionId}",
            ProviderFingerprint = candidate.SourceProviderFingerprint, DirectDependencyCount = 0, ClosedTemplateCount = 1,
            RuntimeRequirements = [], Lifecycle = candidate.SourceLifecycle, PublishedAt = createdAt,
            CreatedAt = createdAt, LastModifiedAt = createdAt
        });
    }

    private static ApplyActivityForkCandidateRequest ForkApply(ActivityForkCandidate candidate, string idempotencyKey) => new(
        candidate.Id, candidate.RequestFingerprint, candidate.AccessBindingFingerprint, candidate.ActorId,
        candidate.AuthorizationProfile, idempotencyKey,
        ActivityForkReceiptIdentity.Compute(candidate.TenantId, candidate.ActorId, idempotencyKey), candidate.CreatedAt.AddMinutes(1));

    private static ActivityDefinitionDraftState State(string label) =>
        new(Contract(), Provider(), new Dictionary<string, string> { ["label"] = label });

    private static ActivityContract Contract() => new("1", [], [], []);

    private static ActivityProviderManifest Provider() => new("workflow", "1", Json("{}"));

    private static ActivityLayoutRecord LayoutRecord(string nodeId) => new(nodeId, Json("{}"));

    private static ActivityDependencyItem DependencyItem(
        string relationshipId,
        ActivityDefinitionReference owner,
        ActivityDefinitionReference dependency,
        string occurrenceId) => new(
        relationshipId, owner, dependency, new(occurrenceId, [new("AuthoredNode", occurrenceId)]), true, 1, [owner, dependency]);

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private sealed record TestHarness(ActivityDesignV2TestHarness Persistence, GroundworkReusableActivityStores Stores) : IDisposable
    {
        public MutableActivityDesignAccess PersistenceAccess => Persistence.Access;
        public void Dispose() => Persistence.Dispose();
    }

    private sealed class Harness : IDisposable
    {
        private Harness(ActivityDesignV2TestHarness persistence, GroundworkReusableActivityStores stores)
        {
            Persistence = persistence;
            Stores = stores;
        }

        public ActivityDesignV2TestHarness Persistence { get; }

        public GroundworkReusableActivityStores Stores { get; }

        public static Harness Create(IDistributedLockProvider? lockProvider = null)
        {
            var persistence = ActivityDesignV2TestHarness.Create();
            var locks = lockProvider ?? new ImmediateDistributedLockProvider();
            return new(
                persistence,
                new GroundworkReusableActivityStores(
                    persistence.Store,
                    new FixedClock(),
                    locks,
                    persistence.Store,
                    new GroundworkActivityManagementProjectionWriter(persistence.Store, locks, persistence.Store)));
        }

        public Task SaveAsync<TEntity>(TEntity entity) where TEntity : Entity
        {
            var (kind, collection) = entity switch
            {
                ActivityDefinition => (ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionCollection),
                ActivityDefinitionAuthoringState => (ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateCollection),
                ActivityDefinitionDraft => (ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection),
                ActivityDefinitionDraftLayout => (ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutCollection),
                ActivityDraftValidationState => (ActivitiesDesignStorageManifest.ActivityDraftValidationDocumentKind, ActivitiesDesignStorageManifest.ActivityDraftValidationCollection),
                ActivityDefinitionVersionPublication => (ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationCollection),
                ActivityDefinitionVersionLayout => (ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutCollection),
                ActivityDependencyEdge => (ActivitiesDesignStorageManifest.ActivityDependencyEdgeDocumentKind, ActivitiesDesignStorageManifest.ActivityDependencyEdgeCollection),
                _ => throw new ArgumentOutOfRangeException(nameof(entity))
            };
            var request = GroundworkV2ActivityDesignDocumentWriter.ToSaveRequest(
                kind, collection, ActivitiesDesignStorageManifest.SchemaVersion, entity, GroundworkActivitiesDesignJson.Options);
            return Persistence.Store.SaveAsync(request);
        }

        public void Dispose() => Persistence.Dispose();
    }

    private sealed class FixedClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 1, 1, 1, 0, 0, TimeSpan.Zero);
    }

    private static async Task<(ActivityForkApplyResult? Result, Exception? Exception)> CaptureAsync(
        Func<Task<ActivityForkApplyResult>> operation)
    {
        try
        {
            return (await operation(), null);
        }
        catch (Exception exception)
        {
            return (null, exception);
        }
    }

    private sealed class YieldingDistributedLockProvider : IDistributedLockProvider
    {
        private readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new(StringComparer.Ordinal);

        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            var gate = locks.GetOrAdd(name, static _ => new(1, 1));
            return gate.Wait(timeout ?? TimeSpan.Zero, cancellationToken) ? new Handle(gate) : null;
        }

        public async ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var gate = locks.GetOrAdd(name, static _ => new(1, 1));
            return await gate.WaitAsync(timeout ?? TimeSpan.Zero, cancellationToken) ? new Handle(gate) : null;
        }

        public async ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var gate = locks.GetOrAdd(name, static _ => new(1, 1));
            if (!await gate.WaitAsync(timeout ?? Timeout.InfiniteTimeSpan, cancellationToken))
                throw new TimeoutException($"Timed out acquiring test lock '{name}'.");
            return new Handle(gate);
        }

        private sealed class Handle(SemaphoreSlim gate) : IDistributedSynchronizationHandle
        {
            private SemaphoreSlim? held = gate;
            public CancellationToken HandleLostToken => CancellationToken.None;
            public void Dispose() => Interlocked.Exchange(ref held, null)?.Release();
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
