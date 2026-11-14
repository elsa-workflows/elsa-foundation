using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Activities.Graph.Design.Services;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Serialization.Core;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Services;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Publishing.SourceCapture;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Workflows.Publishing.Releases.NativeCapture.Tests;

internal sealed partial class PublishedNativeFixture
{
    public CatalogPolicy CatalogAuthorization { get; } = new();
    public TemplatePolicy TemplateAuthorization { get; } = new();
    public T Service<T>() where T : notnull => _scope.ServiceProvider.GetRequiredService<T>();
    public IEnumerable<T> ServicesOf<T>() where T : notnull => _scope.ServiceProvider.GetServices<T>();
    public int CatalogReads { get; private set; }
    public int CatalogTemplateReads { get; private set; }
    public NativeCatalogSourceMemberContributor CatalogSource() => new(Codec, References,
        new ObservedCatalogTemplates(new EfExecutableActivityTemplateStore(Runtime, new Accessor(), new Cursor()), this), Context, CatalogAuthorization, Context, TemplateAuthorization,
        new Accessor(), new ObservedCatalogPublications(Service<IActivityPublicationSnapshotReader>(), this));
    private sealed class ObservedCatalogPublications(IActivityPublicationSnapshotReader actual, PublishedNativeFixture owner) : IActivityPublicationSnapshotReader
    {
        public ValueTask<ActivityPublicationSnapshot?> FindAsync(string id, string scope, ActivityPublicationSnapshotLimits limits, CancellationToken token = default)
        { owner.CatalogReads++; return actual.FindAsync(id, scope, limits, token); }
    }
    private sealed class ObservedCatalogTemplates(IExecutableActivityTemplateReader actual, PublishedNativeFixture owner) : IExecutableActivityTemplateReader
    {
        public ValueTask<RuntimeTemplateSourceSnapshot?> FindBoundedAsync(string id, PersistenceScope scope, RuntimeSourceReadLimits limits, CancellationToken token = default)
        { owner.CatalogTemplateReads++; return actual.FindBoundedAsync(id, scope, limits, token); }
        public ValueTask<ExecutableActivityTemplate?> FindAsync(string id, CancellationToken token = default) => actual.FindAsync(id, token);
        public ValueTask<ExecutableActivityTemplate?> FindByHashAsync(string hash, CancellationToken token = default) => actual.FindByHashAsync(hash, token);
        public ValueTask<RuntimeStorePage<ExecutableActivityTemplate>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken token = default) => actual.ListPageAsync(request, token);
    }
    public async Task<string> PublishClrCatalog()
    {
        const string id = "actual-clr-version"; var types = Service<IWellKnownTypeRegistry>(); types.RegisterType(typeof(WriteLine), typeof(WriteLine).FullName!);
        var serializer = Service<IPayloadSerializer>(); var structures = Service<IActivityStructureService>();
        var input = new RuntimeInputBindingCompiler(types);
        var output = new RuntimeOutputCaptureCompiler(new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]));
        var nodes = new ExecutableNodeCompiler(structures, types, input, output);
        var publications = Service<IActivityDefinitionVersionPublicationStore>();
        var command = new EfSourceActivityPublicationCommand(new EfExecutableActivityTemplateStore(Runtime, new Accessor(), new Cursor()), References, publications, new Accessor());
        var definition = new ActivityDefinition { Id = "actual-clr-definition", ActivityTypeKey = "fixture.ReadableCatalogName", Category = "Tests" };
        var version = new ActivityDefinitionVersion("1.0.0", definition.Id)
        {
            Id = id, Definition = definition, ProviderKey = "elsa.clr-activity", ProviderSchemaVersion = "1", ConsumerKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ConsumerSchemaVersion = "1", DescriptorPayload = serializer.SerializeToElement(new ClrActivityDescriptor(typeof(WriteLine).FullName!)),
            SourceKind = "ClrActivityType", SourceId = typeof(WriteLine).FullName!, Hash = "source-owned-content-fingerprint",
            Inputs = [new("text", nameof(WriteLine.Text), new("String"), null, "Text", null, IsNullable: false, IsRequired: true)],
            DesignFacets = [new("elsa.outcomes", "1", JsonSerializer.SerializeToElement(new { ports = new[] { new { referenceKey = "Done", name = "Done", type = "outcome" } } }))]
        };
        var result = await new SourceOwnedActivityVersionPublisher(command, publications, nodes, TimeProvider.System,
            NullLogger<SourceOwnedActivityVersionPublisher>.Instance).PublishAsync(definition, version);
        if (!result.IsPublished) throw new InvalidOperationException("The real source-owned CLR publication did not succeed.");
        return id;
    }
    public async Task<string> PublishReusableGraph(string childVersion, string schema = "1")
    {
        const string definitionId = "actual-reusable-definition", draftId = "actual-reusable-draft", versionId = "actual-reusable-version";
        var now = DateTimeOffset.UtcNow; var db = Service<ActivitiesDesignDbContext>(); var access = new Accessor();
        var stores = new EfActivityDesignStores(db, access, null, new EfActivityManagementProjectionWriter(db, access));
        var definition = new ActivityDefinition { Id = definitionId, ActivityTypeKey = "fixture.ReusableGraph", Category = "Tests", CreatedAt = now, LastModifiedAt = now };
        var serializer = Service<IPayloadSerializer>();
        var child = new ActivityNode("body", childVersion, [new("text", new ArgumentValue(serializer.SerializeToElement("captured child"), "Literal"), null, null, null, null)], []);
        var providerManifest = new ActivityProviderManifest(GraphActivityProvider.Key, schema, serializer.SerializeToElement(new
        {
            rootActivity = child, variables = Array.Empty<object>(), outputMappings = Array.Empty<object>(),
            outcomeMappings = schema == "2" ? new[] { new { sourceOutcomeReferenceKey = "Done", boundaryOutcomeReferenceKey = "done" } } : []
        }));
        var contract = new Elsa.Activities.Design.Core.Models.ActivityContract("1", [], [], [new("done", "Done", true)]);
        await stores.ExecuteAsync(new CreateActivityDefinitionRequest(definition,
            new ActivityDefinitionAuthoringState { Id = "actual-reusable-authority", DefinitionId = definitionId, ContentAuthority = new(ActivityContentAuthorityKind.Design, "elsa.design"), CreatedAt = now, LastModifiedAt = now },
            new ActivityDefinitionDraft { Id = draftId, DefinitionId = definitionId, Revision = 1, State = new(contract, providerManifest, new Dictionary<string, string>()), CreatedAt = now, LastModifiedAt = now },
            new ActivityDefinitionDraftLayout { Id = draftId, DraftId = draftId, Revision = 1, Records = [], CreatedAt = now, LastModifiedAt = now }));
        var structures = Service<IActivityStructureService>();
        var provider = new GraphActivityProvider(structures, new ActivityContractAuthoringValidator(new EmptyPublicContractTypes()));
        var templates = new EfExecutableActivityTemplateStore(Runtime, access, new Cursor());
        var compiler = new ActivityTemplateCompiler(new ActivityTemplateProviderCompilerRegistry([provider]), new ActivityTemplateDependencyDiscovererRegistry([provider]),
            stores, stores, templates, new AcceptAllActivityTemplateAdmissionPolicy(), TimeProvider.System, [provider], Service<IActivityDataSelectionAnalyzer>(), structures);
        var compilation = await compiler.CompileAsync(new(definition, new ActivityTemplateCandidate("ActivityDraft", draftId, 1, contract, providerManifest), versionId, "1.0.0", 0));
        if (!compilation.IsSuccessful || compilation.Template is null) throw new InvalidOperationException("The actual Graph provider did not compile the reusable source: " + string.Join(",", compilation.Diagnostics.Select(value => value.Code)));
        var template = compilation.Template; var sourceId = "actual-reusable-reference"; var projection = new ActivityCatalogProjection();
        var catalog = projection.CatalogVersion(definition, contract, providerManifest, versionId, "1.0.0", "ActivityDefinitionDraft", draftId, template, now);
        var publication = projection.Publication(definition, contract, providerManifest, versionId, "1.0.0", template.ProviderFingerprint, sourceId, template, compilation.Measurements, now,
            ActivityDefinitionVersionResolutionKind.ReusableTemplateBoundary, draftId);
        var layout = new ActivityDefinitionVersionLayout { Id = versionId, DefinitionVersionId = versionId, Records = [], CreatedAt = now, LastModifiedAt = now };
        var edges = template.DirectDependencies.Select(dependency => new ActivityDependencyEdge
        {
            Id = "edge-" + dependency.OccurrenceId, OwnerVersionId = versionId, OwnerTemplateHash = template.TemplateHash,
            DependencyVersionId = dependency.DefinitionVersionId, DependencyTemplateHash = dependency.TemplateHash, OccurrenceId = dependency.OccurrenceId,
            NodeOrigin = dependency.NodeOrigin.Segments.Select(segment => new ActivityNodeOrigin(segment.Kind.ToString(), segment.Id)).ToArray(), CreatedAt = now, LastModifiedAt = now
        }).ToArray();
        var source = new WorkflowExecutableSourceReference(sourceId, template.TemplateId, "ActivityDefinitionDraft", draftId, "1.0.0", definitionId, versionId, "1.0.0", now, now, WorkflowExecutableReferenceScope.Published);
        const string key = "actual-reusable-operation", review = "sha256:owner-command-witness";
        var receipt = new ActivityPublicationReceipt(null, key, ActivityPublicationRequestFingerprint.Compute(draftId, 1, null, "1.0.0", review), ActivityPublicationReceiptStatus.Applied,
            draftId, 1, null, review, "1.0.0", new(definitionId, versionId, draftId, "1.0.0", template.TemplateId, template.TemplateHash, sourceId, now), null, [], now);
        // The real compiler output is published by its owning cross-domain commit, not direct row seeding.
        // This witness deliberately does not claim API review/preflight or endpoint authorization acceptance.
        await new EfActivityPublicationCommand(new EfActivityPublicationReceiptStore(Publishing, access), templates, References, stores, access)
            .ExecuteAsync(new(null, new(draftId, 1, definitionId, null, catalog, publication, layout, edges), template, source, receipt));
        return versionId;
    }
    private sealed class EmptyPublicContractTypes : IActivityContractCapabilityCatalog
    {
        // The reusable public contract has no inputs or outputs; its actual Graph child is independently resolved.
        public IReadOnlyCollection<ActivityContractTypeCapability> Types => [];
    }
    public async Task SeedClrWorkflow(string catalogVersion)
    {
        var db = Service<WorkflowsDesignDbContext>(); var serializer = Service<IPayloadSerializer>();
        db.Definitions.Add(new() { Id = "actual-clr-workflow", Name = "Actual CLR workflow", TenantId = Partition.Value });
        var node = new ActivityNode("reusable", catalogVersion, [], []);
        var state = new WorkflowDefinitionState([], node, [], [], null); var raw = serializer.SerializeToElement(state).GetRawText();
        db.Versions.Add(new("actual-clr-workflow", "1.0.0", raw) { Id = "actual-clr-workflow-version", TenantId = Partition.Value });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); OriginalSources.Add("actual-clr-workflow-version", raw);
    }
    public sealed class CatalogPolicy : INativeReleaseCatalogCaptureAuthorization
    {
        public bool Allow { get; set; } = true;
        public ValueTask<bool> AuthorizeActivityVersionAsync(ReleaseRuntimeContentScope context, string version, CancellationToken token = default) => ValueTask.FromResult(Allow);
    }
    public sealed class TemplatePolicy : IReleaseRuntimeSourceAuthorization
    {
        public bool Allow { get; set; } = true;
        public ValueTask<bool> AuthorizeMaterialAsync(ReleaseRuntimeContentScope context, string id, string hash, WorkflowExecutableIdentity? pin, CancellationToken token = default) => ValueTask.FromResult(false);
        public ValueTask<bool> AuthorizeTemplateAsync(ReleaseRuntimeContentScope context, string id, string hash, CancellationToken token = default) => ValueTask.FromResult(Allow);
    }
}
