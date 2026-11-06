using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Core.Reconciliation.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Reconciliation.Handlers;
using Elsa.Activities.Design.Reconciliation.Options;
using Elsa.Activities.Design.Reconciliation.Services;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Testing;
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

    private async Task ReconcileAsync(IActivityReconciliationDiagnosticStore diagnostics)
    {
        await using var scope = new ActivityPublicationScope(
            PublishingNativeProvider.Sqlite.Publishing(publishing.ConnectionString, []),
            PublishingNativeProvider.Sqlite.Design(activitiesDesign.ConnectionString, []),
            PublishingNativeProvider.Sqlite.Runtime(runtime.ConnectionString, []),
            Access);
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
        var publisher = new SourceOwnedActivityVersionPublisher(
            scope.SourceCommand,
            stores,
            ReconcilerColdStartRace.CreateNodeCompiler(),
            TimeProvider.System,
            NullLogger<SourceOwnedActivityVersionPublisher>.Instance,
            [provider],
            compiler,
            new AcceptingValidator(),
            [new AcmeActivationStrategy()],
            new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]));
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
    /// A provider compiler with its own gates, as the specialist provider has: content it cannot read fails to compile,
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
