using System.Linq.Expressions;
using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.Core;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Models;
using Elsa.Primitives.Persistence;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Publishing.Api.Tests;

internal sealed class TestActivityPublishingAuthorizationContext(string? tenantId = null)
    : IActivityPublishingAuthorizationContext
{
    public string? TenantId { get; } = tenantId;

    public bool CanAccessTenant(string? candidateTenantId) =>
        candidateTenantId is null || StringComparer.Ordinal.Equals(candidateTenantId, TenantId);
}

/// <summary>Builds an <see cref="IWellKnownTypeRegistry"/> seeded with the primitives these tests rely on.</summary>
internal static class TestWellKnownTypeRegistry
{
    public static IWellKnownTypeRegistry Create()
    {
        var registry = new WellKnownTypeRegistry();
        registry.RegisterType(typeof(string), "String");
        registry.RegisterType(typeof(int), "Int32");
        registry.RegisterType(typeof(bool), "Boolean");
        registry.RegisterType(typeof(object), "Object");
        registry.RegisterType(typeof(double), "Double");
        registry.RegisterType(typeof(long), "Int64");
        registry.RegisterType(typeof(decimal), "Decimal");
        registry.RegisterType(typeof(DateTime), "DateTime");
        registry.RegisterType(typeof(Guid), "Guid");
        registry.RegisterType(typeof(TestWriteLineActivity), TypeAliasConvention.CanonicalAlias(typeof(TestWriteLineActivity)));
        registry.RegisterType(typeof(TestWriteLinesActivity), TypeAliasConvention.CanonicalAlias(typeof(TestWriteLinesActivity)));
        registry.RegisterType(
            typeof(Elsa.Activities.Sequence.Activities.Sequence),
            TypeAliasConvention.CanonicalAlias(typeof(Elsa.Activities.Sequence.Activities.Sequence)));
        registry.RegisterType(
            typeof(Elsa.Activities.Flowchart.Activities.Flowchart),
            TypeAliasConvention.CanonicalAlias(typeof(Elsa.Activities.Flowchart.Activities.Flowchart)));
        return registry;
    }
}

/// <summary>
/// Maps the shared activity-version-id fixtures to the CLR type alias of a registered activity with a
/// typed result, so compiled test workflows satisfy the publish-time VF-ACT-001 contract gate the same
/// way production catalog rows do.
/// </summary>
internal static class TestActivityAliases
{
    public static string ForActivityVersionId(string id) => id switch
    {
        "activity-write-line" => TypeAliasConvention.CanonicalAlias(typeof(TestWriteLineActivity)),
        "activity-write-lines" => TypeAliasConvention.CanonicalAlias(typeof(TestWriteLinesActivity)),
        "activity-sequence" => TypeAliasConvention.CanonicalAlias(typeof(Elsa.Activities.Sequence.Activities.Sequence)),
        "activity-flowchart" => TypeAliasConvention.CanonicalAlias(typeof(Elsa.Activities.Flowchart.Activities.Flowchart)),
        _ => "Object"
    };
}

internal sealed class TestWriteLineActivity : Activity<ActivityUnit>
{
    [ActivityInput(Key = "Text")]
    public string? Text { get; set; }

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
}

internal sealed class TestWriteLinesActivity : Activity<ActivityUnit>
{
    [ActivityInput(Key = "Lines")]
    public IReadOnlyCollection<string>? Lines { get; set; }

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
}

/// <summary>
/// Constructs the <see cref="WorkflowExecutableCompiler"/> and its decomposition collaborators (W30b, #418)
/// for tests that exercise the compiler directly, keeping the collaborator wiring in one place.
/// </summary>
internal static class TestCompiler
{
    public static Elsa.Workflows.Publishing.Api.Services.WorkflowExecutableCompiler Create(
        Elsa.Workflows.Design.Persistence.Core.Stores.IWorkflowDefinitionVersionStore workflowVersions,
        Elsa.Activities.Design.Persistence.Core.Stores.IActivityDefinitionVersionStore activityVersions,
        Elsa.Workflows.Design.Core.Contracts.IActivityStructureService activityStructureService,
        IWellKnownTypeRegistry wellKnownTypeRegistry,
        IActivityDefinitionVersionPublicationStore? activityPublications = null,
        IExecutableActivityTemplateReader? activityTemplates = null,
        IWorkflowExecutableSourceReferenceReader? sourceReferences = null,
        WorkflowExecutablePlacementSidecarContext? placementSidecars = null,
        IRuntimeDurableValueStorageDriverRegistry? storageDrivers = null,
        IExecutableNodeMetadataEnricher? metadataEnricher = null)
    {
        var publications = activityPublications ?? new EmptyActivityPublicationStore();
        var templates = activityTemplates ?? new EmptyActivityTemplateReader();
        var references = sourceReferences ?? new EmptySourceReferenceReader();
        var inputCompiler = new RuntimeInputBindingCompiler(wellKnownTypeRegistry);
        var outputCompiler = new RuntimeOutputCaptureCompiler(storageDrivers ?? new RuntimeDurableValueStorageDriverRegistry(
            [new JsonRuntimeDurableValueStorageDriver()]));
        return new(
            workflowVersions,
            activityVersions,
            publications,
            templates,
            references,
            new ActivityTemplatePlacer(publications, templates, references, new Sha256ActivityPlacementHasher()),
            inputCompiler,
            outputCompiler,
            new WorkflowExecutableHasher(),
            new ActivityTreeProjector(activityStructureService),
            new ExecutableNodeCompiler(
                activityStructureService,
                wellKnownTypeRegistry,
                inputCompiler),
            placementSidecars,
            metadataEnricher);
    }

    private sealed class EmptyActivityPublicationStore : IActivityDefinitionVersionPublicationStore
    {
        public Task<ActivityDefinitionVersionPublication?> FindAsync(string definitionVersionId, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinitionVersionPublication?>(null);
        public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionVersionPublication>>([]);
    }

    private sealed class EmptyActivityTemplateReader : IExecutableActivityTemplateReader
    {
        public ValueTask<ExecutableActivityTemplate?> FindAsync(string templateId, CancellationToken cancellationToken = default) => ValueTask.FromResult<ExecutableActivityTemplate?>(null);
        public ValueTask<ExecutableActivityTemplate?> FindByHashAsync(string templateHash, CancellationToken cancellationToken = default) => ValueTask.FromResult<ExecutableActivityTemplate?>(null);
        public ValueTask<RuntimeStorePage<ExecutableActivityTemplate>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TestRuntimeStorePages.Page(request, Array.Empty<ExecutableActivityTemplate>(), template => template.TemplateId, "activity-template"));
    }

    private sealed class EmptySourceReferenceReader : IWorkflowExecutableSourceReferenceReader
    {
        public ValueTask<WorkflowExecutableSourceReference?> FindAsync(string sourceReferenceId, CancellationToken cancellationToken = default) => ValueTask.FromResult<WorkflowExecutableSourceReference?>(null);
        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByArtifactPageAsync(WorkflowExecutableSourceReferenceArtifactPageQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TestRuntimeStorePages.Page(query, Array.Empty<WorkflowExecutableSourceReference>(), reference => reference.SourceReferenceId, $"source-reference:artifact:{query.ArtifactId}"));
        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListPageAsync(WorkflowExecutableSourceReferencePageQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TestRuntimeStorePages.Page(query, Array.Empty<WorkflowExecutableSourceReference>(), reference => reference.SourceReferenceId, TestRuntimeStorePages.SourceReferenceQueryBinding(query)));
        public ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(WorkflowExecutableArtifactCandidateBatch candidates, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TestRuntimeStorePages.UnreferencedArtifactIds(candidates, Array.Empty<WorkflowExecutableSourceReference>(), now));
    }
}

internal static class TestRuntimeStorePages
{
    public static string SourceReferenceQueryBinding(WorkflowExecutableSourceReferencePageQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return FormattableString.Invariant(
            $"source-reference:list:{query.Scope?.ToString() ?? "all"}:{query.LiveOnly}:{query.Now?.UtcTicks}");
    }

    public static RuntimeStorePage<T> Page<T>(
        RuntimeStorePageRequest request,
        IEnumerable<T> values,
        Func<T, string> identity,
        string queryBinding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(queryBinding);

        var prefix = $"{queryBinding}:";
        var lastId = request.ContinuationToken switch
        {
            null => null,
            { } token when token.StartsWith(prefix, StringComparison.Ordinal) => token[prefix.Length..],
            _ => throw new ArgumentException("The page continuation does not belong to this fake query.", nameof(request))
        };
        var ordered = values
            .OrderBy(identity, StringComparer.Ordinal)
            .Where(value => lastId is null || StringComparer.Ordinal.Compare(identity(value), lastId) > 0)
            .Take(request.Limit + 1)
            .ToArray();
        var items = ordered.Take(request.Limit).ToArray();
        var continuation = ordered.Length > request.Limit ? $"{prefix}{identity(items[^1])}" : null;
        return new RuntimeStorePage<T>(request, items, continuation);
    }

    public static IReadOnlyCollection<string> UnreferencedArtifactIds(
        WorkflowExecutableArtifactCandidateBatch candidates,
        IEnumerable<WorkflowExecutableSourceReference> references,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(references);

        var liveArtifactIds = references
            .Where(reference => reference.IsLive(now))
            .Select(reference => reference.ArtifactId)
            .ToHashSet(StringComparer.Ordinal);
        return candidates.ArtifactIds.Where(artifactId => !liveArtifactIds.Contains(artifactId)).ToArray();
    }
}

internal static class TestRootWriteLeases
{
    public static IWorkflowExecutableRootWriteLeaseManager Create(
        IWorkflowExecutableStore executableStore,
        TimeProvider? timeProvider = null) =>
        new WorkflowExecutableRootWriteLeaseManager(
            executableStore,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions()),
            timeProvider ?? TimeProvider.System);
}

/// <summary>Reusable immutable executable/dependency builders for compiler and publication tests.</summary>
internal static class TestExecutable
{
    public static WorkflowExecutableIdentity Identity(
        string artifactId,
        string artifactHash,
        string definitionId = "definition-child",
        string definitionVersionId = "version-child") =>
        new(artifactId, definitionId, definitionVersionId, "1.0.0", artifactHash);

    public static WorkflowExecutable Create(
        WorkflowExecutableIdentity identity,
        params WorkflowExecutableDependency[] dependencies) =>
        new(
            identity,
            new ExecutableNode(
                executableNodeId: "node-root",
                authoredActivityId: "stored-root",
                activityType: "Test.Stored",
                activityTypeVersion: "1.0.0",
                descriptor: new RuntimeActivityDescriptor(
                    "Test",
                    RuntimeActivityDescriptor.InitialSchemaVersion,
                    JsonSerializer.SerializeToElement(new { })),
                inputBindings: new Dictionary<string, RuntimeInputBinding>(),
                outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
                metadata: new Dictionary<string, string>()),
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(),
            inputContract: new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, []),
            dependencies);
}

/// <summary>Minimal in-memory activity version read port: only the routes the bridge uses are real.</summary>
internal sealed class FakeActivityVersionStore(List<ActivityDefinitionVersion> items) : IActivityDefinitionVersionStore
{
    public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(items.Single(x => x.Id == versionId));

    public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(items.FirstOrDefault(x => x.Id == versionId)
            ?? throw new ArgumentException($"Activity definition version with id '{versionId}' does not exist"));

    public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(items.FirstOrDefault(x => x.DefinitionId == definitionId && x.SemVerSortKey == semVerSortKey));

    public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>(items.Where(x => x.DefinitionId == definitionId).ToList());

    public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default)
    {
        var idSet = definitionIds.ToHashSet();
        return Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>(items.Where(x => idSet.Contains(x.DefinitionId)).ToList());
    }

    public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>(items);
}
