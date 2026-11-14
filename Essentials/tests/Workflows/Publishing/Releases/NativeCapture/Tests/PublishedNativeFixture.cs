using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Expressions.Core.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Publishing.Releases.Runtime;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Publishing.SourceCapture;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using PublishingWorkflowExecutableHasher = Elsa.Workflows.Publishing.Services.WorkflowExecutableHasher;
using Elsa.Workflows.Runtime.Services.Triggers;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Publishing.Releases.NativeCapture.Tests;

/// <summary>Real intrinsic compiler, publication handler, EF slot switch, journal and source stores. No hand-minted publication proof.</summary>
internal sealed partial class PublishedNativeFixture : IAsyncDisposable
{
    public static PersistenceScope Partition { get; } = new("native-source");
    public static ReleaseRuntimeContentScope Scope { get; } = new("trusted-source", null, Partition.Value);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "elsa-published-native-" + Guid.NewGuid().ToString("N"));
    private ServiceProvider _services = null!; private IServiceScope _scope = null!;
    public RuntimeSqliteDbContext Runtime { get; private set; } = null!;
    public PublishingSnapshotReviewSqliteDbContext Publishing { get; private set; } = null!;
    public EfWorkflowExecutableStore Materials { get; private set; } = null!;
    public EfWorkflowExecutableSourceReferenceStore References { get; private set; } = null!;
    public EfPublicationRecordStore Journals { get; private set; } = null!;
    public IReleaseRuntimeArtifactCodec Codec { get; private set; } = null!;
    public Policy Authorization { get; } = new();
    public SourceContext Context { get; } = new();
    private WorkflowExecutableRootWriteLeaseManager _leases = null!;
    private PublishWorkflowRequestHandler _publisher = null!;
    public int VersionReads { get; private set; }
    public int SelectedMaterialReads { get; private set; }
    public Dictionary<string, string> OriginalSources { get; } = new(StringComparer.Ordinal);

    public static async Task<PublishedNativeFixture> Create(bool graphOwner = false, bool primitiveOwner = false, bool releaseVerification = false)
    {
        var f = new PublishedNativeFixture(); Directory.CreateDirectory(f._directory); var access = new Accessor();
        var services = new ServiceCollection(); services.AddOptions();
        services.AddSingleton<IPersistenceAccessContextAccessor>(access);
        new SerializationFeature().ConfigureServices(services); new Elsa.Events.EventsFeature().ConfigureServices(services);
        new WorkflowDesignValidationsFeature().ConfigureServices(services);
        var structures = new DefaultActivityStructureService([]); services.AddSingleton<IActivityStructureService>(structures);
        services.AddWorkflowsDesignEntityFrameworkCore(new() { Provider = "sqlite", ConnectionString = f.Connection("workflows") });
        services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "sqlite", ConnectionString = f.Connection("activities") });
        services.AddScoped<IWorkflowExecutableHasher, Elsa.Workflows.Runtime.Services.Executables.WorkflowExecutableHasher>();
        var profile = new WorkflowReleaseRuntimeFeature(); profile.ConfigureServices(services); profile.PostConfigureServices(services);
        if (graphOwner) new Elsa.Activities.Graph.Releases.GraphActivitiesReleaseVerificationFeature().ConfigureServices(services);
        if (primitiveOwner)
        {
            new Elsa.Activities.Primitives.Releases.ActivitiesPrimitivesReleaseVerificationFeature().ConfigureServices(services);
        }
        if (primitiveOwner || releaseVerification)
        {
            var transport = new Elsa.Workflows.Publishing.Releases.WorkflowReleaseTransportFeature(); transport.ConfigureServices(services); transport.PostConfigureServices(services);
            var verification = new Elsa.Workflows.Publishing.Releases.WorkflowReleaseVerificationFeature(); verification.ConfigureServices(services); verification.PostConfigureServices(services);
        }
        f._services = services.BuildServiceProvider(); f._scope = f._services.CreateScope(); var sp = f._scope.ServiceProvider; f.Codec = sp.GetRequiredService<IReleaseRuntimeArtifactCodec>();
        await sp.GetRequiredService<WorkflowsDesignDbContext>().Database.EnsureCreatedAsync();
        await sp.GetRequiredService<ActivitiesDesignDbContext>().Database.EnsureCreatedAsync();
        f.Runtime = new(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(f.Connection("runtime")).Options);
        f.Publishing = new(new DbContextOptionsBuilder<PublishingSnapshotReviewSqliteDbContext>().UseSqlite(f.Connection("publishing")).Options);
        await f.Runtime.Database.EnsureCreatedAsync(); await f.Publishing.Database.EnsureCreatedAsync();
        f.Materials = new(f.Runtime, access); f.References = new(f.Runtime, access, new Cursor()); f.Journals = new(f.Publishing, access);
        var templates = new EfExecutableActivityTemplateStore(f.Runtime, access, new Cursor());
        f._leases = new(f.Materials, Options.Create(new WorkflowExecutableGarbageCollectionOptions { RootWriteLeaseDuration = TimeSpan.FromMinutes(1) }), TimeProvider.System);
        var versions = sp.GetRequiredService<IWorkflowDefinitionVersionStore>();
        var activityVersions = sp.GetRequiredService<IActivityDefinitionVersionStore>(); var activityPublications = sp.GetRequiredService<IActivityDefinitionVersionPublicationStore>();
        var types = sp.GetRequiredService<IWellKnownTypeRegistry>();
        types.RegisterType(typeof(string), "String");
        var inputs = new RuntimeInputBindingCompiler(types);
        var outputs = new RuntimeOutputCaptureCompiler(new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]));
        var compiler = new WorkflowExecutableCompiler(sp.GetRequiredService<IActivityDataSelectionAnalyzer>(), versions, activityVersions, activityPublications,
            templates, f.References, new ActivityTemplatePlacer(activityPublications, templates, f.References, new Sha256ActivityPlacementHasher(), structures, inputs, outputs),
            inputs, outputs, new PublishingWorkflowExecutableHasher(), new ActivityTreeProjector(structures), new ExecutableNodeCompiler(structures, types, inputs, outputs));
        var authority = new EfWorkflowActivationAuthority(f.Runtime, access); var bindings = new EfWorkflowTriggerBindingStore(f.Runtime, access);
        var extractor = new WorkflowTriggerBindingExtractor([]); var indexer = new WorkflowTriggerIndexer(extractor, bindings);
        var coordinator = new WorkflowActivationCoordinator(authority, new EfWorkflowActivationSwitch(authority, bindings, f.References, access, TimeProvider.System),
            f.References, f._leases, TimeProvider.System, indexer, bindings);
        f._publisher = new(compiler, f.Materials, f.References, extractor, bindings, sp.GetRequiredService<IWorkflowDefinitionVersionLayoutStore>(),
            authority, new InMemoryPublicationPolicyStore(), new PublicationPolicyResolver(), f.Journals, new PublicationPreflightService(),
            new PublicationActivator(coordinator, f.Journals, authority, f.References, TimeProvider.System), TimeProvider.System,
            workflowVersionStore: versions, expressionValidator: sp.GetRequiredService<IExpressionDraftSemanticValidator>());
        return f;
    }

    public NativeReleaseCaptureSource Source() => new(new ObservedVersions(_scope.ServiceProvider.GetRequiredService<IWorkflowVersionSnapshotReader>(), this), References, new ObservedMaterials(Materials, this),
        Journals, _leases, Codec, Context, Authorization, Context, Authorization, new Accessor());
    public async Task Seed(string definition, string versionId, string version, string literal = "same")
    {
        var db = _scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); var serializer = _scope.ServiceProvider.GetRequiredService<IPayloadSerializer>();
        if (!await db.Definitions.AnyAsync(x => x.Id == definition)) db.Definitions.Add(new() { Id = definition, Name = definition, TenantId = Partition.Value });
        var node = new ActivityNode("return", "", [new("value", new ArgumentValue(serializer.SerializeToElement(literal), "Literal"), null, null, null, null)], [])
        { Intrinsic = new(AuthoredWorkflowIntrinsicKind.Return, new("String")) };
        var state = new WorkflowDefinitionState([], node, [], [], null);
        var original = serializer.SerializeToElement(state).GetRawText();
        // Unknown authored data and whitespace remain in the immutable selected source, beyond the typed compiler projection.
        original = " \n" + original[..^1] + ", \"futureAuthoringFacet\":{\"λ\":[1, 2]}} \n";
        db.Versions.Add(new(definition, version, original) { Id = versionId, TenantId = Partition.Value });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); OriginalSources.Add(versionId, original);
    }
    public Task<PublishedWorkflowView> Publish(string version) => _publisher.Handle(new PublishWorkflow(version), CancellationToken.None);
    private string Connection(string name) => "Data Source=" + Path.Combine(_directory, name + ".sqlite") + ";Pooling=False";
    public async ValueTask DisposeAsync()
    {
        _scope?.Dispose(); if (_services is not null) await _services.DisposeAsync();
        if (Runtime is not null) await Runtime.DisposeAsync(); if (Publishing is not null) await Publishing.DisposeAsync();
        Directory.Delete(_directory, true);
    }
    private sealed class ObservedVersions(IWorkflowVersionSnapshotReader actual, PublishedNativeFixture owner) : IWorkflowVersionSnapshotReader
    {
        public ValueTask<WorkflowVersionSnapshot?> FindAsync(string id, string scope, WorkflowVersionSnapshotLimits limits, CancellationToken token = default)
        { owner.VersionReads++; return actual.FindAsync(id, scope, limits, token); }
    }
    // Only the source selector's read is observed. Retention uses the actual physical provider and its
    // separately qualified authorization-before-read contract; this decorator is not a selected lease backend.
    private sealed class ObservedMaterials(IWorkflowExecutableStore actual, PublishedNativeFixture owner) : IWorkflowExecutableStore
    {
        public bool SupportsModelMaterial => actual.SupportsModelMaterial;
        public IWorkflowExecutableStore GetMaterialStore() => actual.GetMaterialStore();
        public ValueTask<RuntimeMaterialSourceSnapshot?> FindMaterialBoundedAsync(string id, PersistenceScope scope, RuntimeSourceReadLimits limits, CancellationToken token = default)
        { owner.SelectedMaterialReads++; return actual.FindMaterialBoundedAsync(id, scope, limits, token); }
        public ValueTask SaveAsync(WorkflowExecutable value, CancellationToken token = default) => actual.SaveAsync(value, token);
        public ValueTask<bool> DeleteAsync(string id, CancellationToken token = default) => actual.DeleteAsync(id, token);
        public ValueTask<WorkflowExecutable?> FindAsync(string id, CancellationToken token = default) => actual.FindAsync(id, token);
        public ValueTask<RuntimeStorePage<WorkflowExecutable>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken token = default) => actual.ListPageAsync(request, token);
        public ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireRootWriteLeaseAsync(string id, string lease, DateTimeOffset expires, DateTimeOffset now, CancellationToken token = default) => actual.TryAcquireRootWriteLeaseAsync(id, lease, expires, now, token);
        public ValueTask<bool> RenewRootWriteLeaseAsync(WorkflowExecutableRootWriteLease lease, DateTimeOffset expires, DateTimeOffset now, CancellationToken token = default) => actual.RenewRootWriteLeaseAsync(lease, expires, now, token);
        public ValueTask ReleaseRootWriteLeaseAsync(WorkflowExecutableRootWriteLease lease, CancellationToken token = default) => actual.ReleaseRootWriteLeaseAsync(lease, token);
        public ValueTask<WorkflowExecutableDeletionGuard?> TryBeginDeletionAsync(string id, string operation, DateTimeOffset expires, DateTimeOffset now, CancellationToken token = default) => actual.TryBeginDeletionAsync(id, operation, expires, now, token);
        public ValueTask<bool> CancelDeletionAsync(WorkflowExecutableDeletionGuard guard, CancellationToken token = default) => actual.CancelDeletionAsync(guard, token);
        public ValueTask<bool> DeleteAsync(WorkflowExecutableDeletionGuard guard, DateTimeOffset now, CancellationToken token = default) => actual.DeleteAsync(guard, now, token);
    }
    private sealed class Accessor : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(Partition); }
    private sealed class Cursor : IRuntimeRecoveryContinuationCodec { public string Encode(string purpose, ReadOnlySpan<byte> payload) => Convert.ToBase64String(payload); public byte[] Decode(string purpose, string token) => Convert.FromBase64String(token); }
    public sealed class SourceContext : INativeReleaseCaptureContextAccessor, IReleaseRuntimeSourceContextAccessor { public ReleaseRuntimeContentScope Current { get; set; } = Scope; }
    public sealed class Policy : INativeReleaseCaptureAuthorization, IReleaseRuntimeSourceAuthorization
    {
        public bool AllowVersion { get; set; } = true; public bool AllowSelected { get; set; } = true; public bool AllowMaterial { get; set; } = true;
        public ValueTask<bool> AuthorizeWorkflowVersionAsync(ReleaseRuntimeContentScope context, string id, CancellationToken token = default) => ValueTask.FromResult(AllowVersion);
        public ValueTask<bool> AuthorizeSelectedMaterialAsync(ReleaseRuntimeContentScope context, string id, string reference, string artifact, CancellationToken token = default) => ValueTask.FromResult(AllowSelected);
        public ValueTask<bool> AuthorizeMaterialAsync(ReleaseRuntimeContentScope context, string id, string hash, WorkflowExecutableIdentity? pin, CancellationToken token = default) => ValueTask.FromResult(AllowMaterial);
        public ValueTask<bool> AuthorizeTemplateAsync(ReleaseRuntimeContentScope context, string id, string hash, CancellationToken token = default) => ValueTask.FromResult(false);
    }
}
