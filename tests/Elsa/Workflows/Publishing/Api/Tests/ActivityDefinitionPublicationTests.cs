using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Locking.Core;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Entities;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Publishing.Persistence.Groundwork;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Manifests;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class ActivityDefinitionPublicationTests
{
    [Fact]
    public async Task Publisher_rejects_a_build_metadata_precedence_collision_without_compiling_or_committing()
    {
        var existing = Publication("definition-1", "version-existing", "test.activity", Template());
        existing = CopyPublication(existing, "1.0.0+build.1");
        var harness = PublisherHarness.Create(existingPublications: [existing]);

        var exception = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() => harness.PublishAsync(
            Request("1.0.0+build.2")));

        Assert.Equal("activity.version.conflict", exception.ErrorCode);
        Assert.Equal(2, harness.Compiler.CallCount);
        Assert.Equal(0, harness.Commit.CallCount);
    }

    [Fact]
    public async Task Publisher_denies_a_foreign_exact_draft_before_content_use_without_disclosing_target_facts()
    {
        var harness = PublisherHarness.Create(resourceTenantId: "tenant-b", authorizationTenantId: "tenant-a");

        var exception = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() =>
            harness.PublishAsync(Request("1.0.0")));

        Assert.Equal("activity.tenant.reference-denied", exception.ErrorCode);
        Assert.Empty(exception.Diagnostics);
        Assert.DoesNotContain("tenant-b", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.Compiler.CallCount);
        Assert.Equal(0, harness.Commit.CallCount);
    }

    [Fact]
    public async Task Publisher_rechecks_tenant_authorization_after_the_publish_lock_and_draft_reread()
    {
        var harness = PublisherHarness.Create(
            resourceTenantId: "tenant-a",
            authorizationTenantId: "tenant-a",
            rereadTenantId: "tenant-b");

        var exception = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() =>
            harness.PublishAsync(Request("1.0.0")));

        Assert.Equal("activity.tenant.reference-denied", exception.ErrorCode);
        Assert.Empty(exception.Diagnostics);
        Assert.Equal(0, harness.Compiler.CallCount);
        Assert.Equal(0, harness.Commit.CallCount);
    }

    [Theory]
    [InlineData(false, 4, "activity.draft.layout-not-found")]
    [InlineData(true, 3, "activity.draft.stale-layout")]
    public async Task Publisher_rejects_missing_or_stale_layout_without_compiling_or_committing(
        bool hasLayout,
        long layoutRevision,
        string expectedCode)
    {
        var harness = PublisherHarness.Create(
            layout: hasLayout ? PublisherHarness.Layout(layoutRevision) : null,
            omitLayout: !hasLayout);

        var exception = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() => harness.PublishAsync(
            Request("1.0.0")));

        Assert.Equal(expectedCode, exception.ErrorCode);
        Assert.Equal(0, harness.Compiler.CallCount);
        Assert.Equal(0, harness.Commit.CallCount);
    }

    [Fact]
    public async Task Publisher_rejects_an_insufficient_required_bump_without_committing()
    {
        var head = CopyPublication(Publication("definition-1", "version-head", "test.activity", Template()), "1.0.0");
        var harness = PublisherHarness.Create(
            headVersionId: head.DefinitionVersionId,
            existingPublications: [head],
            requiredBump: ActivityVersionBump.Major);

        var exception = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() => harness.PublishAsync(
            Request("1.1.0", head.DefinitionVersionId)));

        Assert.Equal("activity.publication.invalid", exception.ErrorCode);
        Assert.Contains(exception.Diagnostics, x => x.Code == "activity.version.bump-insufficient");
        Assert.Equal(2, harness.Compiler.CallCount);
        Assert.Equal(0, harness.Commit.CallCount);
    }

    [Fact]
    public async Task Publisher_propagates_admission_rejection_and_never_enters_the_commit_boundary()
    {
        var diagnostic = new ActivityDiagnostic(
            "activity.template.admission-rejected",
            ActivityDiagnosticSeverity.Error,
            "Rejected by host admission policy.",
            new("ActivityDraft", "draft-1", "definition-1", Revision: 4));
        var harness = PublisherHarness.Create(compileResult: new(
            null,
            Measurements(),
            [],
            [diagnostic]));

        var exception = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() => harness.PublishAsync(
            Request("1.0.0")));

        Assert.Equal("activity.publication.invalid", exception.ErrorCode);
        Assert.Contains(exception.Diagnostics, x => x.Code == diagnostic.Code);
        Assert.Equal(2, harness.Compiler.CallCount);
        Assert.Equal(0, harness.Commit.CallCount);
    }

    [Fact]
    public async Task Publisher_commits_one_exact_source_reference_bound_to_the_new_version_and_template()
    {
        var template = Template();
        var harness = PublisherHarness.Create(compileResult: SuccessfulCompilation(template));

        var result = await harness.PublishAsync(Request("1.0.0"));

        Assert.NotNull(harness.Commit.LastCommit);
        var commit = harness.Commit.LastCommit!;
        Assert.Equal(ActivityPublicationReceiptStatus.Applied, result.Status);
        Assert.Equal(template.TemplateId, commit.SourceReference.ArtifactId);
        Assert.Equal("ActivityDefinitionVersion", commit.SourceReference.SourceKind);
        Assert.Equal(commit.Design.Publication.DefinitionVersionId, commit.SourceReference.SourceId);
        Assert.Equal("definition-1", commit.SourceReference.DefinitionId);
        Assert.Equal(commit.Design.Publication.DefinitionVersionId, commit.SourceReference.DefinitionVersionId);
        Assert.Equal("1.0.0", commit.SourceReference.ArtifactVersion);
        Assert.Equal(commit.SourceReference.SourceReferenceId, commit.Design.Publication.SourceReferenceId);
        Assert.Equal(template.TemplateHash, commit.Design.Publication.TemplateHash);
        Assert.Equal(ActivityDefinitionVersionResolutionKind.ReusableTemplateBoundary, commit.Design.Publication.ResolutionKind);
        Assert.Equal(1, harness.Commit.CallCount);
    }

    [Fact]
    public async Task Design_owned_contract_inputs_and_outputs_project_into_the_committed_catalog_version()
    {
        // #930: a design-owned (e.g. elsa.activity-graph) published version must surface its public
        // contract inputs/outputs as canvas descriptors so the workflow editor exposes editable
        // properties, exactly like CLR-scanned versions. The catalog projection previously left them empty.
        var contract = new Elsa.Activities.Design.Core.Models.ActivityContract(
            "1",
            [new Elsa.Activities.Design.Core.Models.ActivityInputContract(
                ReferenceKey: "Name",
                Name: "Name",
                Type: new("String"),
                IsRequired: true,
                IsNullable: false,
                Default: new("Literal", Json("\"World\"")),
                StorageDriverKey: "elsa.json",
                DisplayName: "Person name",
                Description: "The name to greet.")],
            [new ActivityOutputContract(
                ReferenceKey: "Greeting",
                Name: "Greeting",
                Type: new("String"),
                IsRequired: false,
                IsNullable: true,
                StorageDriverKey: "elsa.json",
                DisplayName: "Greeting text")],
            []);
        var harness = PublisherHarness.Create(contract: contract);

        await harness.PublishAsync(Request("1.0.0"));

        var catalog = harness.Commit.LastCommit!.Design.CatalogVersion;
        var input = Assert.Single(catalog.Inputs);
        Assert.Equal("Name", input.ReferenceKey);
        Assert.Equal("Name", input.Name);
        Assert.Equal("String", input.Type.Alias);
        Assert.True(input.IsRequired);
        Assert.False(input.IsNullable);
        Assert.Equal("Person name", input.DisplayName);
        Assert.Equal("The name to greet.", input.Description);
        Assert.Equal("elsa.json", input.StorageDriverType);
        Assert.Equal("Literal", input.DefaultSyntax);
        Assert.Equal("World", input.DefaultValue!.Value.GetString());

        var output = Assert.Single(catalog.Outputs);
        Assert.Equal("Greeting", output.ReferenceKey);
        Assert.Equal("String", output.Type.Alias);
        Assert.False(output.IsRequired);
        Assert.Equal("Greeting text", output.DisplayName);
    }

    [Fact]
    public async Task First_publication_returns_a_diff_against_the_explicit_definition_baseline()
    {
        var harness = PublisherHarness.Create(differ: new ActivityVersionDiffer());

        var result = await harness.Publisher.PreflightAsync(new("draft-1", 4, null));

        Assert.NotNull(result.Diff);
        Assert.Equal("ActivityDefinitionBaseline", result.Diff!.From.Kind);
        Assert.Equal("definition-1", result.Diff.From.DefinitionId);
        Assert.Equal(Template().TemplateHash, result.Diff.To.TemplateHash);
    }

    [Fact]
    public async Task Preflight_exposes_first_publication_baseline_exact_version_and_runtime_readiness()
    {
        var harness = PublisherHarness.Create(
            differ: new ActivityVersionDiffer(),
            runtimeReady: false);

        var preflight = await harness.Publisher.PreflightAsync(
            new("draft-1", 4, null));

        Assert.False(preflight.HasBaseline);
        Assert.Equal("ActivityDefinitionBaseline", preflight.Diff?.From.Kind);
        Assert.Equal("1.0.0", preflight.MinimumVersion);
        Assert.Equal(["1.0.0"], preflight.ValidVersions);
        Assert.StartsWith("sha256:", preflight.ReviewToken, StringComparison.Ordinal);
        Assert.False(preflight.IsPublishable);
        Assert.Equal("Missing", Assert.Single(preflight.Runtime).Status);
        Assert.Contains(
            preflight.Diagnostics,
            x => x.Code == "activity.runtime.consumer-missing");
    }

    [Fact]
    public async Task Reviewed_publish_is_idempotent_and_reusing_the_key_for_other_material_conflicts()
    {
        var harness = PublisherHarness.Create(
            differ: new ActivityVersionDiffer(),
            runtimeReady: true);
        var preflight = await harness.Publisher.PreflightAsync(
            new("draft-1", 4, null));
        var request = new PublishActivityDefinitionRequest(
            "draft-1",
            4,
            null,
            "1.0.0",
            preflight.ReviewToken,
            "publish-operation-1");

        var first = await harness.Publisher.PublishReviewedAsync(request);
        var replay = await harness.Publisher.PublishReviewedAsync(request);

        Assert.Equal(ActivityPublicationReceiptStatus.Applied, first.Status);
        Assert.Equal(first, replay);
        Assert.Equal(1, harness.Commit.CallCount);
        var conflict = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() =>
            harness.Publisher.PublishReviewedAsync(request with { Version = "1.0.1" }));
        Assert.Equal("activity.publication.idempotency-conflict", conflict.ErrorCode);
        Assert.Equal(1, harness.Commit.CallCount);
    }

    [Fact]
    public async Task Reviewed_publish_accepts_any_unique_exact_semver_at_or_above_the_minimum()
    {
        var harness = PublisherHarness.Create(differ: new ActivityVersionDiffer());
        var preflight = await harness.Publisher.PreflightAsync(new("draft-1", 4, null));

        var receipt = await harness.Publisher.PublishReviewedAsync(new(
            "draft-1",
            4,
            null,
            "7.3.2",
            preflight.ReviewToken,
            "publish-operation-higher-version"));

        Assert.Equal(ActivityPublicationReceiptStatus.Applied, receipt.Status);
        Assert.Equal("7.3.2", receipt.Outcome?.Version);
        Assert.Equal("7.3.2", harness.Commit.LastCommit?.Design.Publication.Version);
    }

    [Fact]
    public async Task Stale_review_token_records_a_stale_receipt_without_publication_writes()
    {
        var harness = PublisherHarness.Create(
            differ: new ActivityVersionDiffer(),
            runtimeReady: true);

        var exception = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() =>
            harness.Publisher.PublishReviewedAsync(new(
                "draft-1",
                4,
                null,
                "1.0.0",
                "sha256:stale",
                "publish-operation-stale")));

        Assert.Equal("activity.publication.review-stale", exception.ErrorCode);
        Assert.Equal(0, harness.Commit.CallCount);
        var receipt = await harness.Publisher.GetReceiptAsync("publish-operation-stale");
        Assert.Equal(ActivityPublicationReceiptStatus.Stale, receipt.Status);
        Assert.Null(receipt.Outcome);
    }

    [Fact]
    public async Task Preflight_applies_required_semver_bump_to_minimum_and_valid_exact_choices()
    {
        var head = CopyPublication(
            Publication("definition-1", "version-head", "test.activity", Template()),
            "1.4.7");
        var harness = PublisherHarness.Create(
            headVersionId: head.DefinitionVersionId,
            existingPublications: [head],
            requiredBump: ActivityVersionBump.Major,
            runtimeReady: true);

        var preflight = await harness.Publisher.PreflightAsync(
            new("draft-1", 4, head.DefinitionVersionId));

        Assert.True(preflight.HasBaseline);
        Assert.Equal("2.0.0", preflight.MinimumVersion);
        Assert.Equal(["2.0.0"], preflight.ValidVersions);
    }

    [Fact]
    public async Task Preflight_reports_missing_storage_readiness_as_a_blocking_diagnostic()
    {
        var template = Template([new("acme.secure-value")]);
        var harness = PublisherHarness.Create(
            compileResult: SuccessfulCompilation(template),
            runtimeReady: true);

        var preflight = await harness.Publisher.PreflightAsync(
            new("draft-1", 4, null));

        var storage = Assert.Single(preflight.Storage);
        Assert.Equal("acme.secure-value", storage.Key);
        Assert.Equal("Missing", storage.Status);
        Assert.False(preflight.IsPublishable);
        Assert.Contains(
            preflight.Diagnostics,
            x => x.Code == "activity.runtime.storage-driver-missing");
    }

    [Theory]
    [InlineData("code")]
    [InlineData("severity")]
    [InlineData("message")]
    [InlineData("remediation")]
    [InlineData("subject-kind")]
    [InlineData("subject-id")]
    [InlineData("subject-definition")]
    [InlineData("subject-version")]
    [InlineData("subject-revision")]
    [InlineData("provider-key")]
    [InlineData("json-pointer")]
    [InlineData("reference-key")]
    [InlineData("node-origin-kind")]
    [InlineData("node-origin-id")]
    [InlineData("dependency-definition")]
    [InlineData("dependency-version-id")]
    [InlineData("dependency-version")]
    [InlineData("dependency-template-hash")]
    [InlineData("metadata")]
    public async Task Review_token_binds_every_returned_safe_diagnostic_field(string field)
    {
        var diagnostic = new ActivityDiagnostic(
            "activity.test.warning",
            ActivityDiagnosticSeverity.Warning,
            "Original message.",
            new("ActivityDraft", "draft-1", "definition-1", "version-1", 4),
            new(
                "test.provider",
                "/nodes/0",
                "reference-1",
                [new("GraphNode", "node-1")],
                [new("dependency-1", "dependency-version-1", "1.0.0", "sha256:dependency")]),
            "Original remediation.",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["fact"] = "original" });
        var changed = field switch
        {
            "code" => diagnostic with { Code = "activity.test.changed" },
            "severity" => diagnostic with { Severity = ActivityDiagnosticSeverity.Error },
            "message" => diagnostic with { Message = "Changed message." },
            "remediation" => diagnostic with { Remediation = "Changed remediation." },
            "subject-kind" => diagnostic with { Subject = diagnostic.Subject with { Kind = "ActivityVersion" } },
            "subject-id" => diagnostic with { Subject = diagnostic.Subject with { Id = "draft-2" } },
            "subject-definition" => diagnostic with { Subject = diagnostic.Subject with { DefinitionId = "definition-2" } },
            "subject-version" => diagnostic with { Subject = diagnostic.Subject with { VersionId = "version-2" } },
            "subject-revision" => diagnostic with { Subject = diagnostic.Subject with { Revision = 5 } },
            "provider-key" => diagnostic with
            {
                Location = diagnostic.Location! with { ProviderKey = "changed.provider" }
            },
            "json-pointer" => diagnostic with
            {
                Location = diagnostic.Location! with { JsonPointer = "/nodes/1" }
            },
            "reference-key" => diagnostic with
            {
                Location = diagnostic.Location! with { ReferenceKey = "reference-2" }
            },
            "node-origin-kind" => diagnostic with
            {
                Location = diagnostic.Location! with { NodeOrigin = [new("ChangedNode", "node-1")] }
            },
            "node-origin-id" => diagnostic with
            {
                Location = diagnostic.Location! with { NodeOrigin = [new("GraphNode", "node-2")] }
            },
            "dependency-definition" => diagnostic with
            {
                Location = diagnostic.Location! with
                {
                    DependencyPath = [diagnostic.Location.DependencyPath![0] with { DefinitionId = "dependency-2" }]
                }
            },
            "dependency-version-id" => diagnostic with
            {
                Location = diagnostic.Location! with
                {
                    DependencyPath = [diagnostic.Location.DependencyPath![0] with { VersionId = "dependency-version-2" }]
                }
            },
            "dependency-version" => diagnostic with
            {
                Location = diagnostic.Location! with
                {
                    DependencyPath = [diagnostic.Location.DependencyPath![0] with { Version = "2.0.0" }]
                }
            },
            "dependency-template-hash" => diagnostic with
            {
                Location = diagnostic.Location! with
                {
                    DependencyPath = [diagnostic.Location.DependencyPath![0] with { TemplateHash = "sha256:changed" }]
                }
            },
            "metadata" => diagnostic with
            {
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["fact"] = "changed" }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        var originalHarness = PublisherHarness.Create(
            compileResult: SuccessfulCompilation() with { Diagnostics = [diagnostic] });
        var changedHarness = PublisherHarness.Create(
            compileResult: SuccessfulCompilation() with { Diagnostics = [changed] });

        var original = await originalHarness.Publisher.PreflightAsync(new("draft-1", 4, null));
        var updated = await changedHarness.Publisher.PreflightAsync(new("draft-1", 4, null));

        Assert.NotEqual(original.ReviewToken, updated.ReviewToken);
    }

    [Fact]
    public async Task Invalid_readiness_records_a_rejected_receipt_and_unknown_keys_are_reconcilable()
    {
        var harness = PublisherHarness.Create(runtimeReady: false);
        var preflight = await harness.Publisher.PreflightAsync(
            new("draft-1", 4, null));

        await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() =>
            harness.Publisher.PublishReviewedAsync(new(
                "draft-1",
                4,
                null,
                "1.0.0",
                preflight.ReviewToken,
                "publish-operation-rejected")));

        Assert.Equal(
            ActivityPublicationReceiptStatus.Rejected,
            (await harness.Publisher.GetReceiptAsync("publish-operation-rejected")).Status);
        Assert.Equal(
            ActivityPublicationReceiptStatus.OutcomeUnknown,
            (await harness.Publisher.GetReceiptAsync("publish-operation-unknown")).Status);
        Assert.Equal(0, harness.Commit.CallCount);
    }

    [Fact]
    public async Task Unexpected_commit_failure_records_a_safe_failed_receipt()
    {
        var harness = PublisherHarness.Create(commitFails: true);
        var preflight = await harness.Publisher.PreflightAsync(
            new("draft-1", 4, null));

        await Assert.ThrowsAsync<IOException>(() =>
            harness.Publisher.PublishReviewedAsync(new(
                "draft-1",
                4,
                null,
                "1.0.0",
                preflight.ReviewToken,
                "publish-operation-failed")));

        var receipt = await harness.Publisher.GetReceiptAsync("publish-operation-failed");
        Assert.Equal(ActivityPublicationReceiptStatus.Failed, receipt.Status);
        Assert.Equal("activity.operation.failed", receipt.ErrorCode);
        Assert.Empty(receipt.Diagnostics);
        Assert.Null(receipt.Outcome);
    }

    [Fact]
    public async Task Receipt_identity_is_scoped_by_tenant_before_idempotent_replay()
    {
        var store = new InMemoryActivityPublicationReceiptStore();
        var receipt = (await Harness.CreateAsync()).Commit.Receipt;
        var tenantA = receipt with { TenantId = "tenant-a" };
        var tenantB = receipt with { TenantId = "tenant-b" };

        Assert.True(await store.TryCreateAsync(tenantA));
        Assert.True(await store.TryCreateAsync(tenantB));
        Assert.Equal("tenant-a", (await store.FindAsync("tenant-a", receipt.IdempotencyKey))?.TenantId);
        Assert.Equal("tenant-b", (await store.FindAsync("tenant-b", receipt.IdempotencyKey))?.TenantId);
        Assert.Null(await store.FindAsync("tenant-c", receipt.IdempotencyKey));
    }

    [Fact]
    public async Task Reviewed_replay_is_authorized_and_the_same_key_is_independent_across_tenants()
    {
        const string idempotencyKey = "shared-operation";
        var receipts = new InMemoryActivityPublicationReceiptStore();
        var tenantA = PublisherHarness.Create(
            resourceTenantId: "tenant-a",
            authorizationTenantId: "tenant-a",
            receiptStore: receipts);
        var tenantB = PublisherHarness.Create(
            resourceTenantId: "tenant-b",
            authorizationTenantId: "tenant-b",
            receiptStore: receipts);
        var tenantAPreflight = await tenantA.Publisher.PreflightAsync(new("draft-1", 4, null));
        var tenantBPreflight = await tenantB.Publisher.PreflightAsync(new("draft-1", 4, null));
        var tenantARequest = new PublishActivityDefinitionRequest(
            "draft-1", 4, null, "1.0.0", tenantAPreflight.ReviewToken, idempotencyKey);
        var tenantBRequest = new PublishActivityDefinitionRequest(
            "draft-1", 4, null, "1.0.0", tenantBPreflight.ReviewToken, idempotencyKey);

        var tenantAFirst = await tenantA.Publisher.PublishReviewedAsync(tenantARequest);
        var tenantAReplay = await tenantA.Publisher.PublishReviewedAsync(tenantARequest);
        var hiddenFromTenantB = await tenantB.Publisher.GetReceiptAsync(idempotencyKey);
        var tenantBFirst = await tenantB.Publisher.PublishReviewedAsync(tenantBRequest);

        Assert.Equal("tenant-a", tenantAFirst.TenantId);
        Assert.Equal(tenantAFirst, tenantAReplay);
        Assert.Equal(ActivityPublicationReceiptStatus.OutcomeUnknown, hiddenFromTenantB.Status);
        Assert.Equal("tenant-b", tenantBFirst.TenantId);
        Assert.Equal(1, tenantA.Commit.CallCount);
        Assert.Equal(1, tenantB.Commit.CallCount);
    }

    [Fact]
    public async Task Tenant_caller_owns_apply_replay_and_receipt_lookup_for_an_authorized_global_resource()
    {
        var harness = PublisherHarness.Create(
            resourceTenantId: null,
            authorizationTenantId: "tenant-a");
        var preflight = await harness.Publisher.PreflightAsync(new("draft-1", 4, null));
        var request = new PublishActivityDefinitionRequest(
            "draft-1",
            4,
            null,
            "1.0.0",
            preflight.ReviewToken,
            "tenant-global-operation");

        var applied = await harness.Publisher.PublishReviewedAsync(request);
        var found = await harness.Publisher.GetReceiptAsync(request.IdempotencyKey);
        var replay = await harness.Publisher.PublishReviewedAsync(request);

        Assert.Equal("tenant-a", applied.TenantId);
        Assert.Equal("tenant-a", harness.Commit.LastCommit?.OperationTenantId);
        Assert.Equal(applied, found);
        Assert.Equal(applied, replay);
        Assert.Equal(1, harness.Commit.CallCount);
    }

    [Fact]
    public async Task Applied_receipt_lookup_does_not_depend_on_the_published_draft_remaining_available()
    {
        var harness = PublisherHarness.Create();
        var preflight = await harness.Publisher.PreflightAsync(new("draft-1", 4, null));
        var request = new PublishActivityDefinitionRequest(
            "draft-1", 4, null, "1.0.0", preflight.ReviewToken, "draft-independent-receipt");
        var applied = await harness.Publisher.PublishReviewedAsync(request);
        harness.Drafts.IsAvailable = false;

        var found = await harness.Publisher.GetReceiptAsync(request.IdempotencyKey);
        var replay = await harness.Publisher.PublishReviewedAsync(request);

        Assert.Equal(applied, found);
        Assert.Equal(applied, replay);
        Assert.Equal(1, harness.Commit.CallCount);
    }

    [Fact]
    public async Task Implementation_only_change_requires_minor_and_diff_receives_provider_runtime_and_layout_facts()
    {
        var oldTemplate = Template();
        var head = CopyPublication(
            Publication("definition-1", "version-head", "test.activity", oldTemplate),
            "1.0.0",
            templateHash: "sha256:old-implementation");
        var providerChange = new ActivityVersionChange(
            "test.provider:implementation-plan-changed",
            ActivityVersionChangeArea.Implementation,
            "ImplementationPlanChanged",
            new(),
            null,
            null,
            ActivityVersionChangeImpact.Additive,
            ActivityVersionBump.Minor,
            "The provider implementation plan changed.");
        var compilation = SuccessfulCompilation() with { ProviderCompatibilityChanges = [providerChange] };
        var capturingDiffer = new CapturingDiffer(new ActivityVersionDiffer());
        var harness = PublisherHarness.Create(
            headVersionId: head.DefinitionVersionId,
            existingPublications: [head],
            compileResult: compilation,
            differ: capturingDiffer);

        var exception = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() =>
            harness.PublishAsync(Request("1.0.1", head.DefinitionVersionId)));

        Assert.Equal("activity.publication.invalid", exception.ErrorCode);
        Assert.Contains(exception.Diagnostics, x => x.Code == "activity.version.bump-insufficient");
        var request = Assert.IsType<ActivityVersionDiffRequest>(capturingDiffer.Request);
        Assert.Equal(compilation.Template!.ProviderFingerprint, request.ToImplementation!.ProviderFingerprint);
        Assert.Equal(compilation.Template.RuntimeRequirements.Count, request.ToImplementation.RuntimeRequirements!.Count);
        Assert.NotNull(request.ToImplementation.LayoutHash);
        Assert.Equal(compilation.ProviderCompatibilityChanges, request.ProviderCompatibilityChanges);
        Assert.Contains(providerChange.ChangeId, (await capturingDiffer.Result!).Changes.Select(x => x.ChangeId));
        Assert.Equal(ActivityVersionBump.Minor, (await capturingDiffer.Result!).RequiredBump);
    }

    [Fact]
    public async Task Behavior_hash_deduplicates_across_version_identity_and_root_excludes_publication_identity()
    {
        var provider = new PureBehaviorCompiler();
        var compiler = new ActivityTemplateCompiler(
            new ActivityTemplateProviderCompilerRegistry([provider]),
            new ActivityTemplateDependencyDiscovererRegistry([provider]),
            new EmptyPublicationStore(),
            new EmptyTemplateReader(),
            new AcceptAdmissionPolicy(),
            TimeProvider.System);
        var first = await compiler.CompileAsync(CompileRequest("definition-a", "type-a", "draft-a", "version-a", "1.0.0"));
        var second = await compiler.CompileAsync(CompileRequest("definition-b", "type-b", "draft-b", "version-b", "2.0.0"));

        Assert.True(first.IsSuccessful);
        Assert.True(second.IsSuccessful);
        Assert.Equal(first.Template!.TemplateHash, second.Template!.TemplateHash);
        Assert.Equal(first.Template.TemplateId, second.Template.TemplateId);
        Assert.Equal(provider.CompilerFingerprint, first.Template.ProviderFingerprint);
        Assert.Equal(64, first.Template.TemplateId["activity-template-".Length..].Length);
        var payload = first.Template.Root.Descriptor.Payload;
        Assert.False(payload.TryGetProperty("definitionId", out _));
        Assert.False(payload.TryGetProperty("definitionVersionId", out _));
        Assert.False(payload.TryGetProperty("version", out _));
        Assert.False(payload.TryGetProperty("templateHash", out _));
    }

    [Fact]
    public async Task Cycle_diagnostic_reports_the_full_iteratively_discovered_exact_path()
    {
        var provider = new CycleCompiler();
        var childTemplate = new ExecutableActivityTemplate(
            "template-b", "hash-b", Boundary(), new Dictionary<string, WorkflowExecutableResumeTarget>(),
            [new("definition-a", "version-a-old", "1.0.0", "template-a-old", "hash-a-old", "back-to-a", ActivityInvocationOrigin.Empty)],
            [], [], "fingerprint", new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
        var publication = Publication("definition-b", "version-b", "type-b", childTemplate);
        var compiler = new ActivityTemplateCompiler(
            new ActivityTemplateProviderCompilerRegistry([provider]),
            new ActivityTemplateDependencyDiscovererRegistry([provider]),
            new SinglePublicationStore(publication),
            new SingleTemplateReader(childTemplate),
            new AcceptAdmissionPolicy(),
            TimeProvider.System);

        var result = await compiler.CompileAsync(CompileRequest("definition-a", "type-a", "draft-a", "version-a-new", "2.0.0"));

        var diagnostic = Assert.Single(result.Diagnostics, x => x.Code == "activity.dependency.cycle");
        Assert.Equal(
            ["version-a-new", "version-b", "version-a-old", "version-a-new"],
            diagnostic.Location!.DependencyPath!.Select(x => x.VersionId));
    }

    [Fact]
    public async Task Admission_rejection_returns_a_diagnostic_and_no_executable_template()
    {
        var provider = new PureBehaviorCompiler();
        var compiler = new ActivityTemplateCompiler(
            new ActivityTemplateProviderCompilerRegistry([provider]),
            new ActivityTemplateDependencyDiscovererRegistry([provider]),
            new EmptyPublicationStore(),
            new EmptyTemplateReader(),
            new RejectAdmissionPolicy(),
            TimeProvider.System);

        var result = await compiler.CompileAsync(CompileRequest("definition", "type", "draft", "version", "1.0.0"));

        Assert.Null(result.Template);
        Assert.Contains(result.Diagnostics, x => x.Code == "activity.template.admission-rejected");
    }

    [Fact]
    public async Task Negative_provider_resource_measurements_are_rejected_before_admission()
    {
        var provider = new PureBehaviorCompiler(new(-1, 0, 0, 0, 0, 0, 0));
        var compiler = new ActivityTemplateCompiler(
            new ActivityTemplateProviderCompilerRegistry([provider]),
            new ActivityTemplateDependencyDiscovererRegistry([provider]),
            new EmptyPublicationStore(),
            new EmptyTemplateReader(),
            new AcceptAdmissionPolicy(),
            TimeProvider.System);

        var result = await compiler.CompileAsync(CompileRequest("definition", "type", "draft", "version", "1.0.0"));

        Assert.Null(result.Template);
        var diagnostic = Assert.Single(result.Diagnostics, x => x.Code == "activity.provider.resource-measurements-invalid");
        Assert.Contains(nameof(ActivityResourceMeasurements.LocalNodeCount), diagnostic.Metadata!["invalidMeasurements"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Atomic_commit_finds_authoring_by_definition_when_document_id_differs()
    {
        var harness = await Harness.CreateAsync();

        var result = await harness.Command.ExecuteAsync(harness.Commit);

        Assert.Equal("version-1", result.DefinitionVersionId);
        Assert.NotNull(await harness.Documents.LoadAsync(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind, "version-1"));
        Assert.NotNull(await harness.Documents.LoadAsync(ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind, harness.Commit.ExecutableTemplate.TemplateId));
        Assert.NotNull(await harness.Documents.LoadAsync(ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind, "source-ref-1"));
        Assert.NotNull(await harness.Documents.LoadAsync(
            PublishingGroundworkStorageManifest.ActivityPublicationReceiptDocumentKind,
            ReceiptId(harness.Commit.Receipt.IdempotencyKey)));
        var authoringEnvelope = await harness.Documents.LoadAsync(ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind, "authoring-generated-id");
        var authoring = DeserializeDesign<ActivityDefinitionAuthoringState>(authoringEnvelope!);
        var draft = DeserializeDesign<ActivityDefinitionDraft>((await harness.Documents.LoadAsync(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, "draft-1"))!);
        var projection = DeserializeDesign<ActivityDependencyProjectionState>((await harness.Documents.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDependencyProjectionDocumentKind,
            ActivityDependencyProjectionState.CurrentId))!);
        Assert.Equal("version-1", authoring.HeadVersionId);
        Assert.Equal("version-1", authoring.RecommendedVersionId);
        Assert.Equal(ActivityDefinitionDraftStatus.Published, draft.Status);
        Assert.Equal(1, projection.Sequence);
    }

    [Fact]
    public async Task Atomic_commit_uses_the_tenant_operation_scope_for_an_authorized_global_resource()
    {
        var harness = await Harness.CreateAsync();
        var commit = harness.Commit with
        {
            OperationTenantId = "tenant-a",
            Receipt = harness.Commit.Receipt with { TenantId = "tenant-a" }
        };

        var result = await harness.Command.ExecuteAsync(commit);

        Assert.Equal("version-1", result.DefinitionVersionId);
        Assert.NotNull(await harness.Documents.LoadAsync(
            PublishingGroundworkStorageManifest.ActivityPublicationReceiptDocumentKind,
            ReceiptId(commit.Receipt.IdempotencyKey, "tenant-a")));
    }

    [Fact]
    public async Task Late_atomic_failpoint_rolls_back_version_template_reference_and_head()
    {
        var harness = await Harness.CreateAsync(injectLateLayoutConflict: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Command.ExecuteAsync(harness.Commit));

        Assert.Null(await harness.Documents.LoadAsync(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind, "version-1"));
        Assert.Null(await harness.Documents.LoadAsync(ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind, "version-1"));
        Assert.Null(await harness.Documents.LoadAsync(ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind, harness.Commit.ExecutableTemplate.TemplateId));
        Assert.Null(await harness.Documents.LoadAsync(ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind, "source-ref-1"));
        Assert.Null(await harness.Documents.LoadAsync(
            PublishingGroundworkStorageManifest.ActivityPublicationReceiptDocumentKind,
            ReceiptId(harness.Commit.Receipt.IdempotencyKey)));
        Assert.Null(await harness.Documents.LoadAsync(ActivitiesDesignStorageManifest.ActivityDependencyProjectionDocumentKind, ActivityDependencyProjectionState.CurrentId));
        var authoring = DeserializeDesign<ActivityDefinitionAuthoringState>((await harness.Documents.LoadAsync(ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind, "authoring-generated-id"))!);
        var draft = DeserializeDesign<ActivityDefinitionDraft>((await harness.Documents.LoadAsync(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, "draft-1"))!);
        Assert.Null(authoring.HeadVersionId);
        Assert.Null(authoring.RecommendedVersionId);
        Assert.Equal(ActivityDefinitionDraftStatus.Active, draft.Status);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("outcome")]
    [InlineData("idempotency-key")]
    [InlineData("request-fingerprint")]
    [InlineData("review-token")]
    [InlineData("tenant")]
    [InlineData("receipt-draft")]
    [InlineData("expected-revision")]
    [InlineData("expected-head")]
    [InlineData("outcome-definition")]
    [InlineData("outcome-definition-version")]
    [InlineData("requested-version")]
    [InlineData("outcome-version")]
    [InlineData("outcome-draft")]
    [InlineData("template-id")]
    [InlineData("template-hash")]
    [InlineData("source-reference")]
    [InlineData("published-at")]
    [InlineData("updated-at")]
    public async Task Atomic_commit_rejects_tampered_receipt_material_without_writes(string field)
    {
        var harness = await Harness.CreateAsync();
        var receipt = harness.Commit.Receipt;
        var outcome = receipt.Outcome!;
        var tampered = field switch
        {
            "status" => receipt with { Status = ActivityPublicationReceiptStatus.Failed },
            "outcome" => receipt with { Outcome = null },
            "idempotency-key" => receipt with { IdempotencyKey = "" },
            "request-fingerprint" => receipt with { RequestFingerprint = "sha256:wrong-but-nonempty" },
            "review-token" => receipt with { ReviewToken = "" },
            "tenant" => receipt with { TenantId = "other-tenant" },
            "receipt-draft" => WithCanonicalFingerprint(receipt with { DraftId = "other-draft" }),
            "expected-revision" => WithCanonicalFingerprint(receipt with
            {
                ExpectedDraftRevision = receipt.ExpectedDraftRevision + 1
            }),
            "expected-head" => WithCanonicalFingerprint(receipt with
            {
                ExpectedDefinitionHeadVersionId = "other-head"
            }),
            "outcome-definition" => receipt with { Outcome = outcome with { DefinitionId = "other-definition" } },
            "outcome-definition-version" => receipt with { Outcome = outcome with { DefinitionVersionId = "other-version" } },
            "requested-version" => WithCanonicalFingerprint(receipt with { RequestedVersion = "9.0.0" }),
            "outcome-version" => receipt with { Outcome = outcome with { Version = "9.0.0" } },
            "outcome-draft" => receipt with { Outcome = outcome with { DraftId = "other-draft" } },
            "template-id" => receipt with { Outcome = outcome with { TemplateId = "other-template" } },
            "template-hash" => receipt with { Outcome = outcome with { TemplateHash = "sha256:tampered" } },
            "source-reference" => receipt with { Outcome = outcome with { SourceReferenceId = "other-source" } },
            "published-at" => receipt with { Outcome = outcome with { PublishedAt = outcome.PublishedAt.AddSeconds(1) } },
            "updated-at" => receipt with { UpdatedAt = receipt.UpdatedAt.AddSeconds(1) },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.Command.ExecuteAsync(harness.Commit with { Receipt = tampered }));

        Assert.Null(await harness.Documents.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
            "version-1"));
        Assert.Null(await harness.Documents.LoadAsync(
            PublishingGroundworkStorageManifest.ActivityPublicationReceiptDocumentKind,
            ReceiptId(receipt.IdempotencyKey)));
    }

    private static ActivityPublicationReceipt WithCanonicalFingerprint(ActivityPublicationReceipt receipt) =>
        receipt with
        {
            RequestFingerprint = ActivityPublicationRequestFingerprint.Compute(
                receipt.DraftId,
                receipt.ExpectedDraftRevision,
                receipt.ExpectedDefinitionHeadVersionId,
                receipt.RequestedVersion,
                receipt.ReviewToken)
        };

    private static ActivityTemplateCompilerRequest CompileRequest(
        string definitionId,
        string activityTypeKey,
        string draftId,
        string versionId,
        string version)
    {
        var state = new ActivityDefinitionDraftState(Contract(), Provider(), new Dictionary<string, string>());
        return new(
            new ActivityDefinition { Id = definitionId, ActivityTypeKey = activityTypeKey, Category = "Test" },
            new ActivityDefinitionDraft { Id = draftId, DefinitionId = definitionId, Revision = 3, State = state },
            versionId,
            version,
            0);
    }

    private static Elsa.Activities.Design.Core.Models.ActivityContract Contract() => new("1", [], [], []);
    private static ActivityProviderManifest Provider() => new("test.provider", "1", Json("{}"));
    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
    private static string ReceiptId(string idempotencyKey, string? tenantId = null) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            tenantId is null
                ? $"global\n{idempotencyKey}"
                : $"tenant\n{tenantId.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n{tenantId}\n{idempotencyKey}")));

    private static ExecutableNode Boundary() => new(
        "boundary", "boundary", "test.consumer", "1", new("test.consumer", "1", Json("{}")),
        new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>());

    private static PublishActivityDefinitionRequest Request(string version, string? expectedHead = null) =>
        new("draft-1", 4, expectedHead, version);

    private static ActivityResourceMeasurements Measurements() => new(1, 1, 0, 1, 10, 20, 0);

    private static ExecutableActivityTemplate Template(
        IReadOnlyCollection<RuntimeStorageDriverRequirement>? storageRequirements = null) => new(
        "activity-template-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        Boundary(),
        new Dictionary<string, WorkflowExecutableResumeTarget>(),
        [],
        [],
        [new("test.consumer", "1")],
        "provider-fingerprint",
        new Dictionary<string, string>(),
        DateTimeOffset.UnixEpoch,
        storageRequirements);

    private static ActivityTemplateCompilerResult SuccessfulCompilation(ExecutableActivityTemplate? template = null) =>
        new(template ?? Template(), Measurements(), [], []);

    private static ActivityDefinitionVersionPublication CopyPublication(
        ActivityDefinitionVersionPublication source,
        string version,
        string? templateHash = null) => new()
        {
            Id = source.Id,
            DefinitionId = source.DefinitionId,
            DefinitionVersionId = source.DefinitionVersionId,
            Version = version,
            ActivityTypeKey = source.ActivityTypeKey,
            ResolutionKind = source.ResolutionKind,
            SourceDraftId = source.SourceDraftId,
            SourceVersionId = source.SourceVersionId,
            Contract = source.Contract,
            Provider = source.Provider,
            TemplateId = source.TemplateId,
            TemplateHash = templateHash ?? source.TemplateHash,
            SourceReferenceId = source.SourceReferenceId,
            ProviderFingerprint = source.ProviderFingerprint,
            DirectDependencyCount = source.DirectDependencyCount,
            ClosedTemplateCount = source.ClosedTemplateCount,
            RuntimeRequirements = source.RuntimeRequirements,
            Lifecycle = source.Lifecycle
        };

    private static ActivityDefinitionVersionPublication Publication(
        string definitionId,
        string versionId,
        string activityTypeKey,
        ExecutableActivityTemplate template) => new()
        {
            Id = versionId,
            DefinitionId = definitionId,
            DefinitionVersionId = versionId,
            Version = "1.0.0",
            ActivityTypeKey = activityTypeKey,
            ResolutionKind = ActivityDefinitionVersionResolutionKind.ReusableTemplateBoundary,
            SourceDraftId = $"draft-{versionId}",
            Contract = Contract(),
            Provider = Provider(),
            TemplateId = template.TemplateId,
            TemplateHash = template.TemplateHash,
            SourceReferenceId = $"source-{versionId}",
            ProviderFingerprint = "fingerprint",
            DirectDependencyCount = template.DirectDependencies.Count,
            ClosedTemplateCount = template.ClosedTemplates.Count,
            RuntimeRequirements = [],
            Lifecycle = ActivityDefinitionVersionLifecycle.Active
        };

    private static TEntity DeserializeDesign<TEntity>(DocumentEnvelope envelope) where TEntity : Entity =>
        JsonSerializer.Deserialize<GroundworkDocument<TEntity>>(envelope.ContentJson, GroundworkActivitiesDesignJson.Options)!.Entity;

    private sealed class PureBehaviorCompiler(ActivityResourceMeasurements? measurements = null) : IActivityTemplateProviderCompiler, IActivityTemplateDependencyDiscoverer
    {
        public string ProviderKey => "test.provider";
        public string CompilerFingerprint => "test.provider/compiler/1";
        public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string> { "1" };

        public ValueTask<ActivityTemplateCompilation> CompileAsync(ActivityTemplateCompilationRequest request, CancellationToken cancellationToken = default)
        {
            var root = new ExecutableNode(
                "boundary",
                "boundary",
                "test.consumer",
                "1",
                new("test.consumer", "1", Json("{\"plan\":1}")),
                new Dictionary<string, RuntimeInputBinding>(),
                new Dictionary<string, RuntimeOutputCapture>(),
                new Dictionary<string, string>());
            return ValueTask.FromResult(new ActivityTemplateCompilation(
                root,
                new Dictionary<string, WorkflowExecutableResumeTarget>(),
                [],
                [new RuntimeRequirement("test.consumer", "1")],
                [],
                measurements ?? new(1, 1, 0, 1, 10, 0, 0),
                request.ProviderFingerprint,
                [],
                []));
        }

        public ValueTask<ActivityTemplateDependencyDiscovery> DiscoverDependenciesAsync(
            ActivityTemplateDependencyDiscoveryRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityTemplateDependencyDiscovery([], []));
    }

    private sealed class CycleCompiler : IActivityTemplateProviderCompiler, IActivityTemplateDependencyDiscoverer
    {
        public string ProviderKey => "test.provider";
        public string CompilerFingerprint => "test.provider/compiler/1";
        public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string> { "1" };
        public ValueTask<ActivityTemplateCompilation> CompileAsync(ActivityTemplateCompilationRequest request, CancellationToken cancellationToken = default) => throw new Xunit.Sdk.XunitException("Cycle detection must precede provider compilation.");
        public ValueTask<ActivityTemplateDependencyDiscovery> DiscoverDependenciesAsync(ActivityTemplateDependencyDiscoveryRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityTemplateDependencyDiscovery(
                [new("version-b", "use-b", [new("AuthoredNode", "use-b")])], []));
    }

    private sealed class SinglePublicationStore(ActivityDefinitionVersionPublication publication) : IActivityDefinitionVersionPublicationStore
    {
        public Task<ActivityDefinitionVersionPublication?> FindAsync(string definitionVersionId, CancellationToken cancellationToken = default) => Task.FromResult(definitionVersionId == publication.DefinitionVersionId ? publication : null);
        public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionVersionPublication>>(definitionId == publication.DefinitionId ? [publication] : []);
    }

    private sealed class SingleTemplateReader(ExecutableActivityTemplate template) : IExecutableActivityTemplateReader
    {
        public ValueTask<ExecutableActivityTemplate?> FindAsync(string templateId, CancellationToken cancellationToken = default) => ValueTask.FromResult(templateId == template.TemplateId ? template : null);
        public ValueTask<ExecutableActivityTemplate?> FindByHashAsync(string templateHash, CancellationToken cancellationToken = default) => ValueTask.FromResult(templateHash == template.TemplateHash ? template : null);
        public ValueTask<RuntimeStorePage<ExecutableActivityTemplate>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TestRuntimeStorePages.Page(request, new[] { template }, candidate => candidate.TemplateId, "activity-template"));
    }

    private sealed class EmptyPublicationStore : IActivityDefinitionVersionPublicationStore
    {
        public Task<ActivityDefinitionVersionPublication?> FindAsync(string definitionVersionId, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinitionVersionPublication?>(null);
        public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionVersionPublication>>([]);
    }

    private sealed class EmptyTemplateReader : IExecutableActivityTemplateReader
    {
        public ValueTask<ExecutableActivityTemplate?> FindAsync(string templateId, CancellationToken cancellationToken = default) => ValueTask.FromResult<ExecutableActivityTemplate?>(null);
        public ValueTask<ExecutableActivityTemplate?> FindByHashAsync(string templateHash, CancellationToken cancellationToken = default) => ValueTask.FromResult<ExecutableActivityTemplate?>(null);
        public ValueTask<RuntimeStorePage<ExecutableActivityTemplate>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TestRuntimeStorePages.Page(request, Array.Empty<ExecutableActivityTemplate>(), template => template.TemplateId, "activity-template"));
    }

    private sealed class AcceptAdmissionPolicy : IActivityTemplateAdmissionPolicy
    {
        public ValueTask<ActivityAdmissionDecision> EvaluateAsync(ActivityResourceMeasurements measurements, ActivityAdmissionContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityAdmissionDecision(true, []));
    }

    private sealed class RejectAdmissionPolicy : IActivityTemplateAdmissionPolicy
    {
        public ValueTask<ActivityAdmissionDecision> EvaluateAsync(ActivityResourceMeasurements measurements, ActivityAdmissionContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityAdmissionDecision(false, []));
    }

    private sealed class PublisherHarness
    {
        private PublisherHarness(
            ActivityDefinitionPublisher publisher,
            SpyTemplateCompiler compiler,
            SpyPublicationCommit commit,
            PublisherDraftStore drafts,
            InMemoryActivityPublicationReceiptStore receipts)
        {
            Publisher = publisher;
            Compiler = compiler;
            Commit = commit;
            Drafts = drafts;
            Receipts = receipts;
        }

        public ActivityDefinitionPublisher Publisher { get; }
        public SpyTemplateCompiler Compiler { get; }
        public SpyPublicationCommit Commit { get; }
        public PublisherDraftStore Drafts { get; }
        public InMemoryActivityPublicationReceiptStore Receipts { get; }

        public async Task<ActivityPublicationReceipt> PublishAsync(
            PublishActivityDefinitionRequest request)
        {
            var preflight = await Publisher.PreflightAsync(new(
                request.DraftId,
                request.ExpectedDraftRevision,
                request.ExpectedDefinitionHeadVersionId));
            return await Publisher.PublishReviewedAsync(request with
            {
                ReviewToken = preflight.ReviewToken,
                IdempotencyKey = $"test-operation-{Guid.NewGuid():N}"
            });
        }

        public static PublisherHarness Create(
            ActivityDefinitionDraftLayout? layout = null,
            bool omitLayout = false,
            string? headVersionId = null,
            IReadOnlyList<ActivityDefinitionVersionPublication>? existingPublications = null,
            ActivityVersionBump requiredBump = ActivityVersionBump.None,
            ActivityTemplateCompilerResult? compileResult = null,
            IActivityVersionDiffer? differ = null,
            string? resourceTenantId = null,
            string? authorizationTenantId = null,
            string? rereadTenantId = null,
            bool runtimeReady = true,
            bool commitFails = false,
            InMemoryActivityPublicationReceiptStore? receiptStore = null,
            Elsa.Activities.Design.Core.Models.ActivityContract? contract = null)
        {
            var definition = new ActivityDefinition
            {
                Id = "definition-1",
                ActivityTypeKey = "test.activity",
                Category = "Test",
                DisplayName = "Test Activity",
                TenantId = resourceTenantId
            };
            var draft = new ActivityDefinitionDraft
            {
                Id = "draft-1",
                DefinitionId = definition.Id,
                Revision = 4,
                Status = ActivityDefinitionDraftStatus.Active,
                TenantId = resourceTenantId,
                State = new(contract ?? Contract(), Provider(), new Dictionary<string, string>())
            };
            var authoring = new ActivityDefinitionAuthoringState
            {
                Id = definition.Id,
                DefinitionId = definition.Id,
                ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design),
                HeadVersionId = headVersionId,
                TenantId = resourceTenantId
            };
            var rereadDraft = rereadTenantId is null ? draft : new ActivityDefinitionDraft
            {
                Id = draft.Id,
                DefinitionId = draft.DefinitionId,
                Revision = draft.Revision,
                Status = draft.Status,
                State = draft.State,
                TenantId = rereadTenantId
            };
            var publications = new PublisherPublicationStore(existingPublications ?? []);
            var compiler = new SpyTemplateCompiler(compileResult ?? SuccessfulCompilation());
            var drafts = new PublisherDraftStore(draft, rereadDraft);
            var receipts = receiptStore ?? new InMemoryActivityPublicationReceiptStore();
            var commit = new SpyPublicationCommit(receipts, commitFails);
            var publisher = new ActivityDefinitionPublisher(
                new PublisherDefinitionStore(definition),
                new PublisherAuthoringStore(authoring),
                drafts,
                publications,
                new PublisherLayoutStore(omitLayout ? null : layout ?? Layout(draft.Revision, resourceTenantId)),
                new EmptyDependencyStore(),
                new ValidDraftValidator(),
                differ ?? new RequiredBumpDiffer(requiredBump),
                compiler,
                new TestActivityPublishingAuthorizationContext(authorizationTenantId),
                commit,
                new ImmediateLockProvider(),
                new SequentialIdentityGenerator(),
                TimeProvider.System,
                receipts,
                runtimeReady ? [new ReadyActivityActivationStrategy()] : null);
            return new(publisher, compiler, commit, drafts, receipts);
        }

        public static ActivityDefinitionDraftLayout Layout(long revision, string? tenantId = null) => new()
        {
            Id = "layout-draft-1",
            DraftId = "draft-1",
            Revision = revision,
            TenantId = tenantId,
            Records = [new("boundary", Json("{\"x\":10,\"y\":20}"))]
        };
    }

    private sealed class PublisherDefinitionStore(ActivityDefinition definition) : IActivityDefinitionStore
    {
        public Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default) =>
            id == definition.Id ? Task.FromResult(definition) : throw new KeyNotFoundException(id);
        public Task<ActivityDefinition?> FindAsync(Elsa.Activities.Design.Persistence.Core.Filters.ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinition>> ListAsync(Elsa.Activities.Design.Persistence.Core.Filters.ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class PublisherAuthoringStore(ActivityDefinitionAuthoringState authoring) : IActivityDefinitionAuthoringStore
    {
        public Task<ActivityDefinitionAuthoringState?> FindAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ActivityDefinitionAuthoringState?>(definitionId == authoring.DefinitionId ? authoring : null);
        public Task<IReadOnlyList<ActivityDefinitionAuthoringState>> ListAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class PublisherDraftStore(ActivityDefinitionDraft draft, ActivityDefinitionDraft? rereadDraft = null) : IActivityDefinitionDraftStore
    {
        private int _findCount;
        public bool IsAvailable { get; set; } = true;

        public Task<ActivityDefinitionDraft?> FindAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ActivityDefinitionDraft?>(IsAvailable && draftId == draft.Id
                ? Interlocked.Increment(ref _findCount) == 1 ? draft : rereadDraft ?? draft
                : null);
        public Task<IReadOnlyList<ActivityDefinitionDraft>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class PublisherPublicationStore(IReadOnlyList<ActivityDefinitionVersionPublication> publications) : IActivityDefinitionVersionPublicationStore
    {
        public Task<ActivityDefinitionVersionPublication?> FindAsync(string definitionVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(publications.SingleOrDefault(x => x.DefinitionVersionId == definitionVersionId));
        public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersionPublication>>(publications.Where(x => x.DefinitionId == definitionId).ToArray());
    }

    private sealed class PublisherLayoutStore(ActivityDefinitionDraftLayout? layout) : IActivityDefinitionLayoutStore
    {
        public Task<ActivityDefinitionDraftLayout?> FindDraftLayoutAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult(layout?.DraftId == draftId ? layout : null);
        public Task<ActivityDefinitionVersionLayout?> FindVersionLayoutAsync(string definitionVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ActivityDefinitionVersionLayout?>(layout is null ? null : new()
            {
                Id = definitionVersionId,
                DefinitionVersionId = definitionVersionId,
                Records = layout.Records.ToArray()
            });
    }

    private sealed class EmptyDependencyStore : IActivityDirectDependencyStore
    {
        public Task<IReadOnlyList<ActivityDependencyEdge>> ListOutboundAsync(string ownerVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDependencyEdge>>([]);
    }

    private sealed class ValidDraftValidator : IActivityDraftValidator
    {
        public ValueTask<ActivityDraftValidation> ValidateAsync(ActivityDraftValidationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityDraftValidation(request.DraftId, request.Revision, true, DateTimeOffset.UnixEpoch, []));
    }

    private sealed class RequiredBumpDiffer(ActivityVersionBump requiredBump) : IActivityVersionDiffer
    {
        public ValueTask<ActivityVersionDiff> DiffAsync(ActivityVersionDiffRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityVersionDiff(
                request.From,
                request.To,
                requiredBump == ActivityVersionBump.None ? ActivityVersionCompatibility.Identical : ActivityVersionCompatibility.Breaking,
                requiredBump,
                requiredBump != ActivityVersionBump.None,
                new(request.FromProvider.ProviderKey, request.FromProvider.SchemaVersion, request.ToProvider.ProviderKey, request.ToProvider.SchemaVersion, false),
                new(requiredBump == ActivityVersionBump.Major ? 1 : 0, 0, 0, 0),
                [],
                []));
    }

    private sealed class CapturingDiffer(IActivityVersionDiffer inner) : IActivityVersionDiffer
    {
        public ActivityVersionDiffRequest? Request { get; private set; }
        public Task<ActivityVersionDiff>? Result { get; private set; }

        public ValueTask<ActivityVersionDiff> DiffAsync(
            ActivityVersionDiffRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            Result = inner.DiffAsync(request, cancellationToken).AsTask();
            return new(Result);
        }
    }

    private sealed class SpyTemplateCompiler(ActivityTemplateCompilerResult result) : IActivityTemplateCompiler
    {
        public int CallCount { get; private set; }
        public ValueTask<ActivityTemplateCompilerResult> CompileAsync(ActivityTemplateCompilerRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ReadyActivityActivationStrategy : IActivityActivationStrategy
    {
        public string ConsumerKey => "test.consumer";
        public IReadOnlyCollection<string> SupportedSchemaVersions => ["1"];
        public bool RequiresInputHydration => false;

        public ValueTask<ActivityActivationLease> ActivateAsync(
            ActivityActivationStrategyRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new ActivityActivationLease(new ReadyActivity()));
        }
    }

    private sealed class ReadyActivity : Activity
    {
        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }

    private sealed class SpyPublicationCommit(
        IActivityPublicationReceiptStore receipts,
        bool fails) :
        ICommitActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt>
    {
        public int CallCount { get; private set; }
        public ActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt>? LastCommit { get; private set; }

        public async Task<ActivityPublicationResult> ExecuteAsync(
            ActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt> commit,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (fails)
                throw new IOException("simulated infrastructure failure");
            LastCommit = commit;
            if (!await receipts.TryCreateAsync(commit.Receipt, cancellationToken))
                throw new InvalidOperationException("Duplicate receipt.");
            return new ActivityPublicationResult(
                commit.Design.DefinitionId,
                commit.Design.Publication.DefinitionVersionId,
                commit.Design.DraftId,
                commit.ExecutableTemplate.TemplateId,
                commit.SourceReference.SourceReferenceId,
                commit.Design.Publication.PublishedAt);
        }
    }

    private sealed class SequentialIdentityGenerator : IIdentityGenerator
    {
        private int _next;
        public string Generate() => Interlocked.Increment(ref _next).ToString();
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

    private sealed class Harness(
        InMemoryDocumentStore documents,
        GroundworkActivityPublicationCommand command,
        ActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt> commit)
    {
        public InMemoryDocumentStore Documents { get; } = documents;
        public GroundworkActivityPublicationCommand Command { get; } = command;
        public ActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt> Commit { get; } = commit;

        public static async Task<Harness> CreateAsync(bool injectLateLayoutConflict = false)
        {
            var documents = new InMemoryDocumentStore(CombinedManifest());
            await SeedAsync(documents);
            var payloads = new JsonPayloadSerializer(new JsonPayloadConverterRegistry());
            var runtimeSerializer = new GroundworkRuntimeDocumentSerializer();
            IDocumentStore store = documents;
            if (injectLateLayoutConflict)
            {
                var conflict = GroundworkDocumentWriter.ToSaveRequest(
                    ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutDocumentKind,
                    ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutCollection,
                    ActivitiesDesignStorageManifest.SchemaVersion,
                    new ActivityDefinitionVersionLayout { Id = "version-1", DefinitionVersionId = "conflict", Records = [] },
                    GroundworkActivitiesDesignJson.Options);
                store = new BeginConflictStore(documents, conflict);
            }

            var commit = CreateCommit();
            var publications = new PublisherPublicationStore([]);
            var projection = new GroundworkActivityDependencyProjection(store, publications);
            var managementProjection = new GroundworkActivityManagementProjectionWriter(
                store,
                new ImmediateLockProvider(),
                documents);
            return new(
                documents,
                new(
                    store,
                    documents,
                    payloads,
                    runtimeSerializer,
                    publications,
                    projection,
                    managementProjection,
                    new PublishingGroundworkDocumentSerializer()),
                commit);
        }

        private static async Task SeedAsync(IDocumentStore store)
        {
            var now = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
            var definition = new ActivityDefinition
            {
                Id = "definition-1",
                ActivityTypeKey = "test.activity",
                Category = "Tests",
                DisplayName = "Test activity",
                CreatedAt = now,
                LastModifiedAt = now
            };
            var authoring = new ActivityDefinitionAuthoringState
            {
                Id = "authoring-generated-id",
                DefinitionId = "definition-1",
                ContentAuthority = new(ActivityContentAuthorityKind.Design, "elsa.design"),
                CreatedAt = now,
                LastModifiedAt = now
            };
            var draft = new ActivityDefinitionDraft
            {
                Id = "draft-1",
                DefinitionId = "definition-1",
                Revision = 4,
                State = new(Contract(), Provider(), new Dictionary<string, string>()),
                CreatedAt = now,
                LastModifiedAt = now
            };
            await store.SaveAsync(GroundworkDocumentWriter.ToSaveRequest(
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionCollection,
                ActivitiesDesignStorageManifest.SchemaVersion,
                definition,
                GroundworkActivitiesDesignJson.Options));
            await store.SaveAsync(GroundworkDocumentWriter.ToSaveRequest(
                ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateCollection,
                ActivitiesDesignStorageManifest.SchemaVersion,
                authoring,
                GroundworkActivitiesDesignJson.Options));
            await store.SaveAsync(GroundworkDocumentWriter.ToSaveRequest(
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection,
                ActivitiesDesignStorageManifest.SchemaVersion,
                draft,
                GroundworkActivitiesDesignJson.Options));
        }

        private static ActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt> CreateCommit()
        {
            var now = new DateTimeOffset(2026, 7, 15, 12, 5, 0, TimeSpan.Zero);
            const string hash = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            const string templateId = "activity-template-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            var root = new ExecutableNode(
                "boundary",
                "boundary",
                "test.consumer",
                "1",
                new("test.consumer", "1", Json("{\"plan\":1}")),
                new Dictionary<string, RuntimeInputBinding>(),
                new Dictionary<string, RuntimeOutputCapture>(),
                new Dictionary<string, string>());
            var template = new ExecutableActivityTemplate(
                templateId,
                hash,
                root,
                new Dictionary<string, WorkflowExecutableResumeTarget>(),
                [],
                [],
                [new RuntimeRequirement("test.consumer", "1")],
                "provider-fingerprint",
                new Dictionary<string, string>(),
                now);
            var source = new WorkflowExecutableSourceReference(
                "source-ref-1",
                templateId,
                "ActivityDefinitionVersion",
                "version-1",
                "1.0.0",
                "definition-1",
                "version-1",
                "1.0.0",
                now,
                now,
                WorkflowExecutableReferenceScope.Published);
            var catalog = new ActivityDefinitionVersion("1.0.0", "definition-1")
            {
                Id = "version-1",
                ProviderKey = "test.provider",
                ProviderSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
                ConsumerKey = "test.consumer",
                ConsumerSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
                DescriptorPayload = root.Descriptor.Payload,
                SourceKind = "ActivityDefinitionDraft",
                SourceId = "draft-1",
                Hash = hash,
                CreatedAt = now,
                LastModifiedAt = now
            };
            var publication = new ActivityDefinitionVersionPublication
            {
                Id = "version-1",
                DefinitionVersionId = "version-1",
                DefinitionId = "definition-1",
                Version = "1.0.0",
                ActivityTypeKey = "test.activity",
                ResolutionKind = ActivityDefinitionVersionResolutionKind.ReusableTemplateBoundary,
                SourceDraftId = "draft-1",
                Contract = Contract(),
                Provider = Provider(),
                TemplateId = templateId,
                TemplateHash = hash,
                SourceReferenceId = "source-ref-1",
                ProviderFingerprint = "provider-fingerprint",
                DirectDependencyCount = 0,
                ClosedTemplateCount = 0,
                RuntimeRequirements = [new("test.consumer", "1")],
                ResourceMeasurements = new(1, 1, 0, 1, 10, 20, 0),
                ResumeTargetCount = 0,
                PublishedAt = now,
                CreatedAt = now,
                LastModifiedAt = now
            };
            var layout = new ActivityDefinitionVersionLayout
            {
                Id = "version-1",
                DefinitionVersionId = "version-1",
                Records = [new("boundary", Json("{\"x\":10,\"y\":20}"))],
                CreatedAt = now,
                LastModifiedAt = now
            };
            var receipt = new ActivityPublicationReceipt(
                null,
                "publish-operation-1",
                ActivityPublicationRequestFingerprint.Compute(
                    "draft-1",
                    4,
                    null,
                    "1.0.0",
                    "sha256:review"),
                ActivityPublicationReceiptStatus.Applied,
                "draft-1",
                4,
                null,
                "sha256:review",
                "1.0.0",
                new(
                    "definition-1",
                    "version-1",
                    "draft-1",
                    "1.0.0",
                    templateId,
                    hash,
                    "source-ref-1",
                    now),
                null,
                [],
                now);
            return new(
                null,
                new("draft-1", 4, "definition-1", null, catalog, publication, layout, []),
                template,
                source,
                receipt);
        }

        private static StorageManifest CombinedManifest()
        {
            var design = ActivitiesDesignStorageManifest.Create();
            var runtime = ElsaRuntimeStorageManifest.Create();
            var publishing = PublishingGroundworkStorageManifest.Create();
            return new(
                new("elsa-activity-publication-tests"),
                new("elsa.tests"),
                new("1.0.0"),
                design.StorageUnits.Concat(runtime.StorageUnits).Concat(publishing.StorageUnits).ToArray(),
                new HashSet<string> { "optimistic-concurrency" },
                []);
        }
    }

    private sealed class BeginConflictStore(InMemoryDocumentStore inner, SaveDocumentRequest conflict) : IDocumentStore
    {
        private int _injected;
        public DocumentStoreAccess Access => inner.Access;
        public TransactionBoundary TransactionBoundary => inner.TransactionBoundary;
        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) => inner.SaveAsync(request, cancellationToken);
        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) => inner.LoadAsync(documentKind, id, cancellationToken);
        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) => inner.DeleteAsync(request, cancellationToken);
        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) => inner.QueryAsync(query, cancellationToken);
        public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) => inner.QueryAsync(query, cancellationToken);
        public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) => inner.FirstOrDefaultAsync(query, cancellationToken);
        public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) => inner.AnyAsync(query, cancellationToken);

        public async Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _injected, 1) == 0)
                Assert.Equal(DocumentStoreWriteStatus.Saved, (await inner.SaveAsync(conflict, cancellationToken)).Status);
            return await inner.BeginAsync(scope, cancellationToken);
        }
    }
}
