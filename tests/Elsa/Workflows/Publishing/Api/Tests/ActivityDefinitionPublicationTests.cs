using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
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

        var exception = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() => harness.Publisher.PublishAsync(
            Request("1.0.0+build.2")));

        Assert.Equal("activity.version.conflict", exception.ErrorCode);
        Assert.Equal(0, harness.Compiler.CallCount);
        Assert.Equal(0, harness.Commit.CallCount);
    }

    [Fact]
    public async Task Publisher_denies_a_foreign_exact_draft_before_content_use_without_disclosing_target_facts()
    {
        var harness = PublisherHarness.Create(resourceTenantId: "tenant-b", authorizationTenantId: "tenant-a");

        var exception = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() =>
            harness.Publisher.PublishAsync(Request("1.0.0")));

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
            harness.Publisher.PublishAsync(Request("1.0.0")));

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

        var exception = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() => harness.Publisher.PublishAsync(
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

        var exception = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() => harness.Publisher.PublishAsync(
            Request("1.1.0", head.DefinitionVersionId)));

        Assert.Equal("activity.publication.invalid", exception.ErrorCode);
        Assert.Contains(exception.Diagnostics, x => x.Code == "activity.version.bump-insufficient");
        Assert.Equal(1, harness.Compiler.CallCount);
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

        var exception = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() => harness.Publisher.PublishAsync(
            Request("1.0.0")));

        Assert.Equal("activity.publication.invalid", exception.ErrorCode);
        Assert.Contains(exception.Diagnostics, x => x.Code == diagnostic.Code);
        Assert.Equal(1, harness.Compiler.CallCount);
        Assert.Equal(0, harness.Commit.CallCount);
    }

    [Fact]
    public async Task Publisher_commits_one_exact_source_reference_bound_to_the_new_version_and_template()
    {
        var template = Template();
        var harness = PublisherHarness.Create(compileResult: SuccessfulCompilation(template));

        var result = await harness.Publisher.PublishAsync(Request("1.0.0"));

        Assert.NotNull(harness.Commit.LastCommit);
        var commit = harness.Commit.LastCommit!;
        Assert.Same(result.SourceReference, commit.SourceReference);
        Assert.Equal(template.TemplateId, result.SourceReference.ArtifactId);
        Assert.Equal("ActivityDefinitionVersion", result.SourceReference.SourceKind);
        Assert.Equal(result.Publication.DefinitionVersionId, result.SourceReference.SourceId);
        Assert.Equal("definition-1", result.SourceReference.DefinitionId);
        Assert.Equal(result.Publication.DefinitionVersionId, result.SourceReference.DefinitionVersionId);
        Assert.Equal("1.0.0", result.SourceReference.ArtifactVersion);
        Assert.Equal(result.SourceReference.SourceReferenceId, commit.Design.Publication.SourceReferenceId);
        Assert.Equal(template.TemplateHash, commit.Design.Publication.TemplateHash);
        Assert.Equal(ActivityDefinitionVersionResolutionKind.ReusableTemplateBoundary, commit.Design.Publication.ResolutionKind);
        Assert.Equal(1, harness.Commit.CallCount);
    }

    [Fact]
    public async Task First_publication_returns_a_diff_against_the_explicit_definition_baseline()
    {
        var harness = PublisherHarness.Create(differ: new ActivityVersionDiffer());

        var result = await harness.Publisher.PublishAsync(Request("1.0.0"));

        Assert.NotNull(result.Diff);
        Assert.Equal("ActivityDefinitionBaseline", result.Diff!.From.Kind);
        Assert.Equal("definition-1", result.Diff.From.DefinitionId);
        Assert.Equal(result.Template.TemplateHash, result.Diff.To.TemplateHash);
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
            harness.Publisher.PublishAsync(Request("1.0.1", head.DefinitionVersionId)));

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
        var authoringEnvelope = await harness.Documents.LoadAsync(ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind, "authoring-generated-id");
        var authoring = DeserializeDesign<ActivityDefinitionAuthoringState>(authoringEnvelope!);
        var draft = DeserializeDesign<ActivityDefinitionDraft>((await harness.Documents.LoadAsync(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, "draft-1"))!);
        var projection = DeserializeDesign<ActivityDependencyProjectionState>((await harness.Documents.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDependencyProjectionDocumentKind,
            ActivityDependencyProjectionState.CurrentId))!);
        Assert.Equal("version-1", authoring.HeadVersionId);
        Assert.Equal(ActivityDefinitionDraftStatus.Published, draft.Status);
        Assert.Equal(1, projection.Sequence);
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
        Assert.Null(await harness.Documents.LoadAsync(ActivitiesDesignStorageManifest.ActivityDependencyProjectionDocumentKind, ActivityDependencyProjectionState.CurrentId));
        var authoring = DeserializeDesign<ActivityDefinitionAuthoringState>((await harness.Documents.LoadAsync(ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind, "authoring-generated-id"))!);
        var draft = DeserializeDesign<ActivityDefinitionDraft>((await harness.Documents.LoadAsync(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, "draft-1"))!);
        Assert.Null(authoring.HeadVersionId);
        Assert.Equal(ActivityDefinitionDraftStatus.Active, draft.Status);
    }

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

    private static ActivityContract Contract() => new("1", [], [], []);
    private static ActivityProviderManifest Provider() => new("test.provider", "1", Json("{}"));
    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private static ExecutableNode Boundary() => new(
        "boundary", "boundary", "test.consumer", "1", new("test.consumer", "1", Json("{}")),
        new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>());

    private static PublishActivityDefinitionRequest Request(string version, string? expectedHead = null) =>
        new("draft-1", 4, expectedHead, version);

    private static ActivityResourceMeasurements Measurements() => new(1, 1, 0, 1, 10, 20, 0);

    private static ExecutableActivityTemplate Template() => new(
        "activity-template-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        Boundary(),
        new Dictionary<string, WorkflowExecutableResumeTarget>(),
        [],
        [],
        [new("test.consumer", "1")],
        "provider-fingerprint",
        new Dictionary<string, string>(),
        DateTimeOffset.UnixEpoch);

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
            SpyPublicationCommit commit)
        {
            Publisher = publisher;
            Compiler = compiler;
            Commit = commit;
        }

        public ActivityDefinitionPublisher Publisher { get; }
        public SpyTemplateCompiler Compiler { get; }
        public SpyPublicationCommit Commit { get; }

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
            string? rereadTenantId = null)
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
                State = new(Contract(), Provider(), new Dictionary<string, string>())
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
            var commit = new SpyPublicationCommit();
            var publisher = new ActivityDefinitionPublisher(
                new PublisherDefinitionStore(definition),
                new PublisherAuthoringStore(authoring),
                new PublisherDraftStore(draft, rereadDraft),
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
                TimeProvider.System);
            return new(publisher, compiler, commit);
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

        public Task<ActivityDefinitionDraft?> FindAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ActivityDefinitionDraft?>(draftId == draft.Id
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

    private sealed class SpyPublicationCommit : ICommitActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference>
    {
        public int CallCount { get; private set; }
        public ActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference>? LastCommit { get; private set; }

        public Task<ActivityPublicationResult> ExecuteAsync(
            ActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference> commit,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastCommit = commit;
            return Task.FromResult(new ActivityPublicationResult(
                commit.Design.DefinitionId,
                commit.Design.Publication.DefinitionVersionId,
                commit.Design.DraftId,
                commit.ExecutableTemplate.TemplateId,
                commit.SourceReference.SourceReferenceId,
                commit.Design.Publication.PublishedAt));
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
        ActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference> commit)
    {
        public InMemoryDocumentStore Documents { get; } = documents;
        public GroundworkActivityPublicationCommand Command { get; } = command;
        public ActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference> Commit { get; } = commit;

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
            return new(documents, new(store, documents, payloads, runtimeSerializer, publications, projection), commit);
        }

        private static async Task SeedAsync(IDocumentStore store)
        {
            var now = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
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

        private static ActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference> CreateCommit()
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
            return new(new("draft-1", 4, "definition-1", null, catalog, publication, layout, []), template, source);
        }

        private static StorageManifest CombinedManifest()
        {
            var design = ActivitiesDesignStorageManifest.Create();
            var runtime = ElsaRuntimeStorageManifest.Create();
            return new(
                new("elsa-activity-publication-tests"),
                new("elsa.tests"),
                new("1.0.0"),
                design.StorageUnits.Concat(runtime.StorageUnits).ToArray(),
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
