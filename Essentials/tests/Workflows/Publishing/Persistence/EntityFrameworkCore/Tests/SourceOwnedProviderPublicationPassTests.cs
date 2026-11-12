using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Core.Reconciliation.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Reconciliation.Handlers;
using Elsa.Activities.Design.Reconciliation.Options;
using Elsa.Activities.Design.Reconciliation.Services;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Options = Microsoft.Extensions.Options.Options;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// One reconciliation pass over three provider-backed source versions, published through the real template compiler and
/// the EF source commit: one the provider cannot compile, one whose selected model fails the provider's capability gate,
/// and one that is valid. Only the valid one publishes; each refusal is recorded against its own version, and the pass
/// is not stopped by either.
/// </summary>
public sealed class SourceOwnedProviderPublicationPassTests : IAsyncLifetime
{
    private static readonly TestAccess Access = TestAccess.Scoped("default");
    private SqliteTestDatabase publishing = null!;
    private SqliteTestDatabase activitiesDesign = null!;
    private SqliteTestDatabase runtime = null!;

    public async Task InitializeAsync()
    {
        publishing = await SqliteTestDatabase.CreateAsync<PublishingSnapshotReviewSqliteDbContext>(options => new(options));
        activitiesDesign = await SqliteTestDatabase.CreateAsync<ActivitiesDesignSqliteDbContext>(options => new(options));
        runtime = await SqliteTestDatabase.CreateAsync<RuntimeSqliteDbContext>(options => new(options));
    }

    public async Task DisposeAsync()
    {
        await publishing.DisposeAsync();
        await activitiesDesign.DisposeAsync();
        await runtime.DisposeAsync();
    }

    [Fact]
    public async Task Only_the_valid_version_publishes_and_each_refusal_is_recorded_for_its_version()
    {
        var diagnostics = new InMemoryActivityReconciliationDiagnosticStore();

        await ReconcileAsync(diagnostics);

        await using var design = PublishingNativeProvider.Sqlite.Design(activitiesDesign.ConnectionString, []);
        var published = Assert.Single(await design.ActivityDefinitionVersions.AsNoTracking().ToListAsync());
        Assert.Equal("Acme.Valid", TypeKeyOf(published.Id));
        var publication = Assert.Single(await design.ActivityDefinitionVersionPublications.AsNoTracking().ToListAsync());
        Assert.Equal(published.Id, publication.DefinitionVersionId);
        Assert.Equal(ActivityDefinitionVersionResolutionKind.ReusableTemplateBoundary, publication.ResolutionKind);
        Assert.Equal(AcmeProvider.Fingerprint, publication.ProviderFingerprint);
        Assert.Empty(diagnostics.Find(published.Id));

        var refused = diagnostics.List();
        Assert.Equal(2, refused.Count);
        Assert.Equal(
            [("acme.compile.invalid", "Acme.Broken"), ("acme.model.capability", "Acme.Incapable")],
            refused.Select(entry => (Code: Assert.Single(entry.Value).Code, TypeKey: TypeKeyOf(entry.Key)))
                .OrderBy(x => x.Code, StringComparer.Ordinal));

        // The next pass writes nothing new and still reports both refusals.
        await ReconcileAsync(diagnostics);
        await using var again = PublishingNativeProvider.Sqlite.Design(activitiesDesign.ConnectionString, []);
        Assert.Single(await again.ActivityDefinitionVersionPublications.AsNoTracking().ToListAsync());
        Assert.Equal(refused.Keys.Order(StringComparer.Ordinal), diagnostics.List().Keys.Order(StringComparer.Ordinal));
    }

    private static string TypeKeyOf(string versionId) =>
        Source.Versions.Single(x => ActivityCatalogStableIds.VersionId(x.ActivityTypeKey, x.Version) == versionId).ActivityTypeKey;

    [Theory]
    [InlineData("tenant-A")]
    [InlineData(null)]
    public async Task Source_ownership_survives_publication_and_reopen(string? tenantId)
    {
        var access = TestAccess.Scoped(tenantId ?? "default");
        var (definition, version) = SourceEntities(tenantId);
        string templateHash;
        string templateId;
        string sourceReferenceId;
        await using (var scope = OpenScope(access))
        {
            Assert.True((await CreatePublisher(scope).PublishAsync(definition, version)).IsPublished);
            var publication = Assert.Single(await scope.Design.ActivityDefinitionVersionPublications.AsNoTracking().ToListAsync());
            templateHash = publication.TemplateHash;
            templateId = publication.TemplateId;
            sourceReferenceId = publication.SourceReferenceId;
        }

        // Fresh contexts over the same files prove durable ownership, not tracked in-memory entities.
        await using (var reopened = OpenScope(access))
        {
            Assert.Equal(tenantId, Assert.Single(await reopened.Design.ActivityDefinitions.AsNoTracking().ToListAsync()).TenantId);
            Assert.Equal(tenantId, Assert.Single(await reopened.Design.ActivityDefinitionVersions.AsNoTracking().ToListAsync()).TenantId);
            Assert.Equal(tenantId, Assert.Single(await reopened.Design.ActivityDefinitionAuthoringStates.AsNoTracking().ToListAsync()).TenantId);
            Assert.Equal(tenantId, Assert.Single(await reopened.Design.ActivityDefinitionVersionPublications.AsNoTracking().ToListAsync()).TenantId);
            Assert.Equal(tenantId, Assert.Single(await reopened.Design.ActivityDefinitionVersionLayouts.AsNoTracking().ToListAsync()).TenantId);
            Assert.Equal(version.Hash, Assert.Single(await reopened.Design.ActivityDefinitionVersions.AsNoTracking().ToListAsync()).Hash);
            var scopeKey = EfRelationalIdentity.Encode(tenantId ?? "default");
            Assert.Equal(scopeKey, Assert.Single(await reopened.Runtime.ExecutableActivityTemplates.AsNoTracking().ToListAsync()).ScopeKey);
            Assert.Equal(scopeKey, Assert.Single(await reopened.Runtime.ExecutableActivityTemplateHashClaims.AsNoTracking().ToListAsync()).ScopeKey);
            Assert.Equal(scopeKey, Assert.Single(await reopened.Runtime.WorkflowExecutableSourceReferences.AsNoTracking().ToListAsync()).ScopeKey);
            Assert.Equal(tenantId, (await reopened.SourceReferences.FindAsync(sourceReferenceId))!.TenantId);
            Assert.Equal(templateHash, (await reopened.Templates.FindAsync(templateId))!.TemplateHash);
            await AssertCatalogVisibilityAsync(reopened, definition.Id, version.Id, visible: true);
            Assert.True((await CreatePublisher(reopened).PublishAsync(definition, version)).IsPublished);
            Assert.Single(await reopened.Design.ActivityDefinitionVersionPublications.AsNoTracking().ToListAsync());
        }

        await using (var foreign = OpenScope(TestAccess.Scoped("tenant-B")))
        {
            await AssertCatalogVisibilityAsync(foreign, definition.Id, version.Id, visible: tenantId is null);
            Assert.Null(await foreign.Templates.FindAsync(templateId));
            Assert.Null(await foreign.SourceReferences.FindAsync(sourceReferenceId));
        }
        await using (var global = OpenScope(new TestAccess(PersistenceAccessContext.Global)))
            await AssertCatalogVisibilityAsync(global, definition.Id, version.Id, visible: tenantId is null);
    }

    [Fact]
    public async Task Foreign_scope_is_refused_before_any_source_publication_write()
    {
        await using var scope = OpenScope(TestAccess.Scoped("tenant-B"));
        var (definition, version) = SourceEntities("tenant-A");

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreatePublisher(scope).PublishAsync(definition, version));

        await AssertNoPublicationWritesAsync(scope);
    }

    [Theory]
    [InlineData("definition")]
    [InlineData("authoring")]
    [InlineData("version")]
    [InlineData("publication")]
    [InlineData("layout")]
    [InlineData("source-reference")]
    public async Task Mixed_source_commit_ownership_is_refused_before_runtime_or_design_writes(string participant)
    {
        await using var scope = OpenScope(TestAccess.Scoped("tenant-A"));
        var (definition, version) = SourceEntities("tenant-A");
        var capture = new CaptureSourceCommit();
        await CreatePublisher(scope, capture).PublishAsync(definition, version);
        var commit = Assert.IsType<SourceActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference>>(capture.Commit);
        // Establish one valid ownership set independently of the publisher conversion under test.
        commit.Definition.TenantId = commit.AuthoringState.TenantId = commit.CatalogVersion.TenantId =
            commit.Publication.TenantId = commit.Layout.TenantId = "tenant-A";
        commit = commit with { SourceReference = commit.SourceReference with { TenantId = "tenant-A" } };
        // Null is otherwise readable as a global design row, so scope checks alone cannot catch this mismatch.
        switch (participant)
        {
            case "definition": commit.Definition.TenantId = null; break;
            case "authoring": commit.AuthoringState.TenantId = null; break;
            case "version": commit.CatalogVersion.TenantId = null; break;
            case "publication": commit.Publication.TenantId = null; break;
            case "layout": commit.Layout.TenantId = null; break;
            case "source-reference": commit = commit with { SourceReference = commit.SourceReference with { TenantId = null } }; break;
        }

        await Assert.ThrowsAsync<ArgumentException>(() => scope.SourceCommand.ExecuteAsync(commit));

        await AssertNoPublicationWritesAsync(scope);
    }

    private ActivityPublicationScope OpenScope(TestAccess access) => new(
        PublishingNativeProvider.Sqlite.Publishing(publishing.ConnectionString, []),
        PublishingNativeProvider.Sqlite.Design(activitiesDesign.ConnectionString, []),
        PublishingNativeProvider.Sqlite.Runtime(runtime.ConnectionString, []), access);

    private static async Task AssertCatalogVisibilityAsync(ActivityPublicationScope scope, string definitionId, string versionId, bool visible)
    {
        Assert.Equal(visible, await scope.DesignStores.FindAsync(new ActivityDefinitionFilter { Id = definitionId }) is not null);
        Assert.Equal(visible, (await ((IActivityDefinitionVersionStore)scope.DesignStores).ListByDefinitionAsync(definitionId)).Count == 1);
        Assert.Equal(visible, await ((IActivityDefinitionAuthoringStore)scope.DesignStores).FindAsync(definitionId) is not null);
        Assert.Equal(visible, await ((IActivityDefinitionVersionPublicationStore)scope.DesignStores).FindAsync(versionId) is not null);
        Assert.Equal(visible, await scope.DesignStores.FindVersionLayoutAsync(versionId) is not null);
    }

    private static async Task AssertNoPublicationWritesAsync(ActivityPublicationScope scope)
    {
        Assert.Empty(await scope.Design.ActivityDefinitions.AsNoTracking().ToListAsync());
        Assert.Empty(await scope.Design.ActivityDefinitionVersions.AsNoTracking().ToListAsync());
        Assert.Empty(await scope.Design.ActivityDefinitionAuthoringStates.AsNoTracking().ToListAsync());
        Assert.Empty(await scope.Design.ActivityDefinitionVersionPublications.AsNoTracking().ToListAsync());
        Assert.Empty(await scope.Design.ActivityDefinitionVersionLayouts.AsNoTracking().ToListAsync());
        Assert.Empty(await scope.Runtime.ExecutableActivityTemplates.AsNoTracking().ToListAsync());
        Assert.Empty(await scope.Runtime.ExecutableActivityTemplateHashClaims.AsNoTracking().ToListAsync());
        Assert.Empty(await scope.Runtime.WorkflowExecutableSourceReferences.AsNoTracking().ToListAsync());
    }

    private static (ActivityDefinition, ActivityDefinitionVersion) SourceEntities(string? tenantId)
    {
        var definition = new ActivityDefinition { Id = "source-definition", TenantId = tenantId, ActivityTypeKey = "Acme.Private", Category = "Tests" };
        var version = new ActivityDefinitionVersion("1.0.0", definition.Id)
        {
            Id = "source-version", TenantId = tenantId, Definition = definition,
            ProviderKey = AcmeProvider.Key, ProviderSchemaVersion = "1",
            ConsumerKey = AcmeProvider.Consumer, ConsumerSchemaVersion = "1",
            DescriptorPayload = JsonSerializer.SerializeToElement(new { judgement = "valid", model = "large" }),
            SourceKind = "Configuration", SourceId = "source-configuration", Hash = "source-content-hash"
        };
        return (definition, version);
    }

    private sealed class CaptureSourceCommit : ICommitSourceActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference>
    {
        public SourceActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference>? Commit { get; private set; }
        public Task ExecuteAsync(SourceActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference> commit, CancellationToken cancellationToken = default)
        {
            Commit = commit;
            return Task.CompletedTask;
        }
    }

    private async Task ReconcileAsync(IActivityReconciliationDiagnosticStore diagnostics)
    {
        await using var scope = new ActivityPublicationScope(
            PublishingNativeProvider.Sqlite.Publishing(publishing.ConnectionString, []),
            PublishingNativeProvider.Sqlite.Design(activitiesDesign.ConnectionString, []),
            PublishingNativeProvider.Sqlite.Runtime(runtime.ConnectionString, []),
            Access);
        var stores = scope.DesignStores;
        var publisher = CreatePublisher(scope);
        var identities = new TestIdentityGenerator();
        var hasher = new DefaultActivityDefinitionHasher();
        await new ActivityVersionReconciler(
            NullLogger<ActivityVersionReconciler>.Instance,
            hasher,
            new HandlingPublisher<ActivityVersionsReconciling>(new CollectActivityVersions(
                stores,
                new ActivityDefinitionFactory(identities),
                new ActivityDefinitionVersionFactory(identities, hasher),
                WorkflowReconciliationPass.Serializer,
                [new Source()])),
            Options.Create(new ActivityVersionReconcilerOptions()),
            stores,
            stores,
            stores,
            stores,
            stores,
            [publisher],
            diagnostics).Reconcile(CancellationToken.None);
    }

    private static SourceOwnedActivityVersionPublisher CreatePublisher(ActivityPublicationScope scope,
        ICommitSourceActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference>? command = null)
    {
        var stores = scope.DesignStores;
        var provider = new AcmeProvider();
        var compiler = new ActivityTemplateCompiler(
            new ActivityTemplateProviderCompilerRegistry([provider]),
            new ActivityTemplateDependencyDiscovererRegistry([provider]),
            stores,
            stores,
            scope.Templates,
            new AcceptAllActivityTemplateAdmissionPolicy(),
            TimeProvider.System,
            [],
            new NoAuthoredGraphAnalyzer());
        return new SourceOwnedActivityVersionPublisher(
            command ?? scope.SourceCommand,
            stores,
            ReconcilerColdStartRace.CreateNodeCompiler(),
            TimeProvider.System,
            NullLogger<SourceOwnedActivityVersionPublisher>.Instance,
            [provider],
            compiler,
            new AcceptingValidator(),
            [new AcmeActivationStrategy()],
            new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]));
    }

    /// <summary>Three provider-backed activities, each saying in its payload how the provider should judge it.</summary>
    private sealed class Source : IActivityReconciliationSource
    {
        public static readonly ActivityVersionReconciliationModel[] Versions =
        [
            Model("Acme.Broken", "broken"),
            Model("Acme.Incapable", "incapable"),
            Model("Acme.Valid", "valid")
        ];

        public string SourceId => "acme-configuration";

        public string SourceKind => "Configuration";

        public ValueTask<IEnumerable<ActivityVersionReconciliationModel>> Read(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IEnumerable<ActivityVersionReconciliationModel>>(Versions);

        private static ActivityVersionReconciliationModel Model(string activityTypeKey, string judgement) => new(
            Id: null,
            Version: "1.0.0",
            ActivityTypeKey: activityTypeKey,
            DisplayName: activityTypeKey,
            Category: "Tests",
            Description: null,
            ProviderKey: AcmeProvider.Key,
            ProviderSchemaVersion: "1",
            ConsumerKey: AcmeProvider.Consumer,
            ConsumerSchemaVersion: "1",
            Descriptor: JsonSerializer.SerializeToElement(new { judgement, model = judgement == "incapable" ? "small" : "large" }),
            Inputs: [],
            Outputs: [],
            DesignFacets: []);
    }

    /// <summary>
    /// A provider compiler with its own gates, as the agent provider has: content it cannot read fails to compile,
    /// and a selected model the capability source says cannot serve the content is refused before any template exists.
    /// </summary>
    private sealed class AcmeProvider : IActivityTemplateProviderCompiler, IActivityTemplateDependencyDiscoverer
    {
        public const string Key = "acme.provider";
        public const string Consumer = "acme.consumer";
        public const string Fingerprint = "acme-compiler-1";

        public string ProviderKey => Key;
        public string CompilerFingerprint => Fingerprint;
        public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string>(["1"]);

        public ValueTask<ActivityTemplateCompilation> CompileAsync(ActivityTemplateCompilationRequest request, CancellationToken cancellationToken = default)
        {
            var subject = new ActivityDiagnosticSubject(request.SubjectKind, request.DraftId, request.DefinitionId, Revision: request.Revision);
            var measurements = new ActivityResourceMeasurements(1, 1, 0, 1, request.Provider.Payload.GetRawText().Length, 0, 0);
            var judgement = request.Provider.Payload.GetProperty("judgement").GetString();
            if (judgement == "broken")
                return Refuse("acme.compile.invalid", "The content cannot be compiled.");
            if (request.Provider.Payload.GetProperty("model").GetString() == "small")
                return Refuse("acme.model.capability", "The selected model cannot serve this content.");

            var root = new ExecutableNode("root", "root", request.ActivityTypeKey, request.CandidateVersion,
                new RuntimeActivityDescriptor(Consumer, "1", request.Provider.Payload),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
            return ValueTask.FromResult(new ActivityTemplateCompilation(root, new Dictionary<string, WorkflowExecutableResumeTarget>(), [],
                [new(Consumer, "1")], [], measurements, request.ProviderFingerprint, [], []));

            ValueTask<ActivityTemplateCompilation> Refuse(string code, string message) => ValueTask.FromResult(new ActivityTemplateCompilation(
                null, new Dictionary<string, WorkflowExecutableResumeTarget>(), [], [], [], measurements, request.ProviderFingerprint, [],
                [new(code, ActivityDiagnosticSeverity.Error, message, subject)]));
        }

        public ValueTask<ActivityTemplateDependencyDiscovery> DiscoverDependenciesAsync(ActivityTemplateDependencyDiscoveryRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityTemplateDependencyDiscovery([], []));
    }

    // This provider fixture emits only native empty-input roots. Any new authored graph must use
    // the real shared analyzer instead of silently inheriting a permissive test substitute.
    private sealed class NoAuthoredGraphAnalyzer : IActivityDataSelectionAnalyzer
    {
        public ValueTask<ActivityDataSelectionAnalysis> AnalyzeAsync(ActivityDataSelectionDocument document,
            ActivityDataSelectionTarget? target = null, ActivityDataSelectionQuery? query = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This provider fixture does not own an authored graph.");
    }

    private sealed class AcceptingValidator : IActivityDraftValidator
    {
        public ValueTask<ActivityDraftValidation> ValidateAsync(ActivityDraftValidationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityDraftValidation(request.DraftId, request.Revision, true, DateTimeOffset.UnixEpoch, []));
    }

    private sealed class AcmeActivationStrategy : IActivityActivationStrategy
    {
        public string ConsumerKey => AcmeProvider.Consumer;
        public IReadOnlyCollection<string> SupportedSchemaVersions { get; } = ["1"];
        public bool RequiresInputHydration => false;

        public ValueTask<ActivityActivationLease> ActivateAsync(ActivityActivationStrategyRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
