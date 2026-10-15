using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Core.Reconciliation.Models;
using Elsa.Activities.Design.Reconciliation.Clr.Services;
using Elsa.Primitives.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using CompleteActivity = Elsa.Activities.Graph.Runtime.Activities.Complete;
using DelayActivity = Elsa.Activities.Scheduling.Activities.Delay;
using DispatchWorkflowActivity = Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;
using HttpEndpointActivity = Elsa.Activities.Http.Activities.HttpEndpoint;
using IfActivity = Elsa.Activities.If.Activities.If;
using RunJavaScriptActivity = Elsa.Activities.Scripting.Activities.RunJavaScript;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;
using WriteLineActivity = Elsa.Activities.Primitives.Activities.WriteLine;

namespace Elsa3.Mapping.Tests.Support;

/// <summary>
/// An activity catalog built the way CLR reconciliation builds it: the real <see cref="ClrAssemblyScanner"/> over the
/// shipped Elsa 4 activity assemblies the map targets, with the reconciler's stable definition and version ids. Tests
/// resolve real type keys, versions, input keys, slots and outcomes through it (spec 236 SC-002), never a stub that
/// answers any key.
/// </summary>
internal sealed class RealActivityCatalog : IActivityDefinitionLookup
{
    private static readonly Lazy<IReadOnlyList<ActivityVersionReconciliationModel>> Shipped = new(() => Scan(ShippedAssemblies!));

    private readonly List<CatalogVersion> _versions = [];

    private RealActivityCatalog(IEnumerable<ActivityVersionReconciliationModel> models)
    {
        foreach (var model in models)
            Add(model);
    }

    /// <summary>
    /// One anchor type per shipped activity assembly the map targets. Referencing a type makes the compiler enforce
    /// the project reference.
    /// </summary>
    public static IReadOnlyList<Type> Anchors { get; } =
    [
        typeof(IfActivity),
        typeof(SequenceActivity),
        typeof(FlowchartActivity),
        typeof(WriteLineActivity),
        typeof(DelayActivity),
        typeof(HttpEndpointActivity),
        typeof(DispatchWorkflowActivity),
        typeof(RunJavaScriptActivity),
        typeof(CompleteActivity),
    ];

    private static IReadOnlyList<Assembly> ShippedAssemblies { get; } =
        Anchors.Select(anchor => anchor.Assembly).Distinct().ToArray();

    /// <summary>Every activity version the shipped assemblies declare, as CLR reconciliation records them.</summary>
    public static IReadOnlyList<ActivityVersionReconciliationModel> ShippedModels => Shipped.Value;

    /// <summary>A catalog holding every shipped activity at its reconciled version.</summary>
    public static RealActivityCatalog Create() => new(Shipped.Value);

    /// <summary>A catalog holding the shipped activities plus every activity in <paramref name="assemblies"/>.</summary>
    public static RealActivityCatalog CreateWith(params Assembly[] assemblies) =>
        new(Shipped.Value.Concat(Scan(assemblies)));

    /// <summary>All versions currently in the catalog.</summary>
    public IReadOnlyList<IActivityDefinitionVersion> Versions => _versions;

    /// <summary>Adds a version of an existing type at <paramref name="version"/>, cloned from its first version.</summary>
    public RealActivityCatalog WithVersion(string activityTypeKey, string version)
    {
        var template = _versions.First(item => StringComparer.Ordinal.Equals(item.Definition.ActivityTypeKey, activityTypeKey));
        _versions.Add(template with
        {
            Id = ActivityCatalogStableIds.VersionId(activityTypeKey, version)!,
            Version = version
        });
        return this;
    }

    /// <summary>
    /// Adds a test activity at version 1.0.0 under <paramref name="activityTypeKey"/> (a key no shipped activity uses),
    /// declaring <paramref name="inputs"/>; an Elsa 3 node of that type resolves to it verbatim.
    /// </summary>
    public RealActivityCatalog WithActivity(string activityTypeKey, params InputDefinition[] inputs)
    {
        Add(new ActivityVersionReconciliationModel(
            null, "1.0.0", activityTypeKey, activityTypeKey, "Test", null,
            "elsa.clr-activity", "1", "elsa.clr-activity", "1", new object(),
            inputs, [], [], ActivityExecutionType.Action));
        return this;
    }

    /// <summary>Adds a version whose version string is not valid SemVer, which resolution must ignore.</summary>
    public RealActivityCatalog WithInvalidVersion(string activityTypeKey, string version)
    {
        var template = _versions.First(item => StringComparer.Ordinal.Equals(item.Definition.ActivityTypeKey, activityTypeKey));
        _versions.Add(template with { Id = $"{template.Id}:invalid:{version}", Version = version });
        return this;
    }

    /// <summary>Removes every version of <paramref name="activityTypeKey"/>, keeping its definition.</summary>
    public RealActivityCatalog WithoutVersions(string activityTypeKey)
    {
        var definition = _versions.First(item => StringComparer.Ordinal.Equals(item.Definition.ActivityTypeKey, activityTypeKey)).Definition;
        _versions.RemoveAll(item => ReferenceEquals(item.Definition, definition));
        _orphanDefinitions.Add(definition);
        return this;
    }

    private readonly List<IActivityDefinition> _orphanDefinitions = [];

    /// <summary>The catalog version id of the highest shipped version of <paramref name="activityTypeKey"/>.</summary>
    public string VersionIdOf(string activityTypeKey) =>
        _versions.First(item => StringComparer.Ordinal.Equals(item.Definition.ActivityTypeKey, activityTypeKey)).Id;

    public Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default)
    {
        var definition = Definitions()
            .FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, idOrActivityTypeKey) ||
                                    StringComparer.Ordinal.Equals(item.ActivityTypeKey, idOrActivityTypeKey));
        return definition is null
            ? Task.FromException<IActivityDefinition>(new EntityNotFoundException($"Activity definition could not be found for activity-type-key/id '{idOrActivityTypeKey}'"))
            : Task.FromResult(definition);
    }

    public Task<IEnumerable<IActivityDefinition>> ListDefinitions(
        string? id = null,
        string? category = null,
        string? searchTerm = null,
        string? displayName = null,
        string? description = null,
        bool? tenantAgnostic = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IEnumerable<IActivityDefinition>>(Definitions().ToArray());

    public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default)
    {
        var version = _versions.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, versionId));
        return version is null
            ? Task.FromException<IActivityDefinitionVersion>(new EntityNotFoundException($"Activity version '{versionId}' could not be found."))
            : Task.FromResult<IActivityDefinitionVersion>(version);
    }

    public Task<IActivityDefinitionVersion?> FindVersion(string versionId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IActivityDefinitionVersion?>(_versions.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, versionId)));

    public Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(string definitionId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IEnumerable<ActivityDefinitionVersionSummary>>(_versions
            .Where(item => StringComparer.Ordinal.Equals(item.DefinitionId, definitionId))
            .Select(item => new ActivityDefinitionVersionSummary(item.Id, item.Version, DateTimeOffset.UnixEpoch, item.ExecutionType))
            .ToArray());

    private IEnumerable<IActivityDefinition> Definitions() =>
        _versions.Select(item => item.Definition).Concat(_orphanDefinitions).Distinct();

    private void Add(ActivityVersionReconciliationModel model)
    {
        var definitionId = ActivityCatalogStableIds.DefinitionId(model.ActivityTypeKey)!;
        var definition = _versions.Select(item => item.Definition)
                             .FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, definitionId))
                         ?? new CatalogDefinition(definitionId, model.ActivityTypeKey, model.Category ?? "Elsa", model.DisplayName, model.Description);
        _versions.Add(new CatalogVersion(
            ActivityCatalogStableIds.VersionId(model.ActivityTypeKey, model.Version)!,
            model.Version,
            definition,
            model.ProviderKey,
            model.ProviderSchemaVersion,
            model.ConsumerKey,
            model.ConsumerSchemaVersion,
            model.Inputs.ToArray(),
            model.Outputs.ToArray(),
            model.DesignFacets.ToArray(),
            model.ExecutionType));
    }

    private static IReadOnlyList<ActivityVersionReconciliationModel> Scan(IEnumerable<Assembly> assemblies)
    {
        var directory = Path.Combine(Path.GetTempPath(), "elsa3-mapping-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var assembly in assemblies.Distinct())
                File.Copy(assembly.Location, Path.Combine(directory, Path.GetFileName(assembly.Location)), overwrite: true);

            var scanner = new ClrAssemblyScanner(
                new ActivityTypeVersionResolver(),
                new ActivityTypeCategoryResolver(),
                NullLogger<ClrAssemblyScanner>.Instance);
            return scanner.Scan(directory);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; a locked file must not fail the test.
            }
        }
    }

    private sealed record CatalogDefinition(
        string Id,
        string ActivityTypeKey,
        string Category,
        string? DisplayName,
        string? Description) : IActivityDefinition;

    private sealed record CatalogVersion(
        string Id,
        string Version,
        IActivityDefinition Definition,
        string ProviderKey,
        string ProviderSchemaVersion,
        string ConsumerKey,
        string ConsumerSchemaVersion,
        IEnumerable<InputDefinition> Inputs,
        IEnumerable<OutputDefinition> Outputs,
        IEnumerable<ActivityDesignFacet> DesignFacets,
        ActivityExecutionType ExecutionType) : IActivityDefinitionVersion
    {
        public string DefinitionId => Definition.Id;
        public JsonElement DescriptorPayload => JsonSerializer.SerializeToElement(new { });
        public string SourceKind => "clr";
        public string SourceId => Definition.ActivityTypeKey;
        public string? Hash => null;
    }
}
