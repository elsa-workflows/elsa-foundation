using System.Text.Json;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Api.Contracts;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Reconciliation.Clr.Services;
using Elsa.Contracts.Generator;
using ActivityContract = Elsa.Contracts.Generator.ActivityContract;
using Elsa.Primitives.Identity;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Contracts.Tests;

/// <summary>
/// The one-projection rule, phase 1 (spec 149 US4 / RFC #1191): a representative host's catalog output
/// must equal the merged contract fragments of its composed features once the server-state overlay
/// (version ids, availability, provenance) is stripped â€” and runtime-registered activities must be
/// purely additive. The catalog pipeline runs for real: scanner models persisted through the same
/// factories reconciliation uses, served by the real request handler over in-memory stores.
/// </summary>
public sealed class EquivalenceTests
{
    private static readonly string[] FeatureAssemblies =
    [
        "Elsa.Activities.Http",
        "Elsa.Activities.Sequence",
        "Elsa.Activities.Flowchart",
        "Elsa.Activities.ControlFlow",
        "Elsa.Activities.Primitives"
    ];

    private static readonly string BaseDirectory = AppContext.BaseDirectory;
    private static readonly string[] ReferencePaths = Directory.EnumerateFiles(BaseDirectory, "*.dll").ToArray();

    private static ClrAssemblyScanner CreateScanner() => new(
        new ActivityTypeVersionResolver(),
        new ActivityTypeCategoryResolver(),
        NullLogger<ClrAssemblyScanner>.Instance);

    private static ContractFragment ProjectFragment(string assemblyName)
    {
        TargetAssembly.AddProbeDirectory(BaseDirectory);
        var diagnostics = new Diagnostics();
        var fragment = new FragmentProjector(diagnostics).Project(Path.Combine(BaseDirectory, assemblyName + ".dll"), ReferencePaths);
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        return fragment;
    }

    private static (IReadOnlyList<ActivityDefinition> Definitions, IReadOnlyList<ActivityDefinitionVersion> Versions) BuildCatalogRows(
        IEnumerable<string> assemblyNames,
        out Dictionary<string, string> featureIdByTypeKey)
    {
        var scanner = CreateScanner();
        var identity = new GuidIdentityGenerator();
        var definitionFactory = new ActivityDefinitionFactory(identity);
        var versionFactory = new ActivityDefinitionVersionFactory(identity, new DefaultActivityDefinitionHasher());
        var definitions = new List<ActivityDefinition>();
        var versions = new List<ActivityDefinitionVersion>();
        featureIdByTypeKey = new Dictionary<string, string>(StringComparer.Ordinal);

        var row = 0;
        foreach (var assemblyName in assemblyNames)
        {
            var fragment = ProjectFragment(assemblyName);
            foreach (var activity in fragment.Activities.Where(candidate => candidate.FeatureId is not null))
                featureIdByTypeKey[activity.ActivityTypeKey] = activity.FeatureId!;

            foreach (var model in scanner.ScanAssembly(Path.Combine(BaseDirectory, assemblyName + ".dll"), ReferencePaths))
            {
                row++;
                var definition = definitionFactory.Create(
                    model.ActivityTypeKey, model.Category ?? string.Empty, model.DisplayName, model.Description, $"def-{row}");
                var descriptorPayload = JsonSerializer.SerializeToElement(model.Descriptor, model.Descriptor.GetType());
                versions.Add(ActivityDefinitionVersion.From(versionFactory.Create(
                    definition, model.Version, model.ProviderKey, model.ProviderSchemaVersion,
                    model.ConsumerKey, model.ConsumerSchemaVersion, descriptorPayload,
                    "CLR", assemblyName, model.Inputs, model.Outputs, model.DesignFacets, model.ExecutionType, $"ver-{row}")));
                definitions.Add(ActivityDefinition.From(definition));
            }
        }

        return (definitions, versions);
    }

    private static async Task<ActivityAuthoringCatalogView> ServeCatalog(
        IReadOnlyList<ActivityDefinition> definitions,
        IReadOnlyList<ActivityDefinitionVersion> versions,
        IReadOnlyDictionary<string, string> featureIdByTypeKey)
    {
        var handler = new ListActivityAuthoringCatalogRequestHandler(
            new StubDefinitionStore(definitions),
            new StubVersionStore(versions),
            new AllAddableEvaluator(),
            new NullSettingsStore(),
            [new IntrinsicAuthoringDescriptorProvider()],
            new MapAttributionResolver(featureIdByTypeKey));
        return await handler.Handle(new ListActivityAuthoringCatalog(ActivityCatalogAvailability.All), CancellationToken.None);
    }

    [Fact]
    public async Task Catalog_output_equals_merged_fragments_plus_overlay_for_the_feature_provided_surface()
    {
        var (definitions, versions) = BuildCatalogRows(FeatureAssemblies, out var featureIdByTypeKey);
        var catalog = await ServeCatalog(definitions, versions, featureIdByTypeKey);
        var fragmentActivities = FeatureAssemblies
            .Select(ProjectFragment)
            .SelectMany(fragment => fragment.Activities)
            .ToDictionary(activity => activity.ActivityTypeKey, StringComparer.Ordinal);

        var clrItems = catalog.Activities.Where(item => item.Intrinsic is null).ToArray();
        Assert.Equal(fragmentActivities.Count, clrItems.Length);

        foreach (var item in clrItems)
        {
            Assert.True(fragmentActivities.TryGetValue(item.ActivityTypeKey, out var fragmentActivity),
                $"Catalog serves '{item.ActivityTypeKey}' but no fragment describes it.");

            // Strip the server-state overlay (ActivityVersionId, Available, AvailabilityReason,
            // Provenance) and the server-generated template; the remaining content must be equal.
            var servedAsContract = ToComparableContract(item, fragmentActivity!.FeatureId, ContentHashOf(versions, item));
            Assert.Equal(
                DeterministicJson.Serialize(WithoutNotes(fragmentActivity)),
                DeterministicJson.Serialize(servedAsContract));
        }
    }

    [Fact]
    public async Task Intrinsic_catalog_items_equal_the_design_api_fragment()
    {
        var (definitions, versions) = BuildCatalogRows(FeatureAssemblies, out var featureIdByTypeKey);
        var catalog = await ServeCatalog(definitions, versions, featureIdByTypeKey);
        var fragment = ProjectFragment("Elsa.Activities.Design.Api");

        var served = catalog.Activities.Where(item => item.Intrinsic is not null).ToArray();
        Assert.Equal(fragment.Intrinsics.Count, served.Length);
        foreach (var item in served)
        {
            var contract = Assert.Single(fragment.Intrinsics, intrinsic => intrinsic.DescriptorId == item.ActivityVersionId);
            Assert.Equal(contract.ActivityTypeKey, item.ActivityTypeKey);
            Assert.Equal(contract.DisplayName, item.DisplayName);
            Assert.Equal(contract.Intrinsic.Kind, item.Intrinsic!.Kind);
            Assert.Equal(contract.Inputs.Select(i => i.ReferenceKey), item.Inputs.Select(i => i.ReferenceKey));
        }
    }

    [Fact]
    public void Structure_registry_equals_the_fragments_structure_entries()
    {
        // The registry endpoint enumerates DI-registered IActivityStructureHandler instances; compose the
        // structure-owning features for real and compare with the union of their fragments.
        var services = new ServiceCollection();
        new Elsa.Activities.Sequence.ActivitiesSequenceFeature().ConfigureServices(services);
        new Elsa.Activities.Flowchart.ActivitiesFlowchartFeature().ConfigureServices(services);
        new Elsa.Activities.ControlFlow.ActivitiesControlFlowFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var served = provider.GetServices<IActivityStructureHandler>()
            .Select(handler => (handler.Kind, handler.SchemaVersion, handler.SupportsScopedVariables))
            .OrderBy(entry => entry.Kind, StringComparer.Ordinal)
            .ToArray();

        var fromFragments = new[] { "Elsa.Activities.Sequence", "Elsa.Activities.Flowchart", "Elsa.Activities.ControlFlow" }
            .Select(ProjectFragment)
            .SelectMany(fragment => fragment.Structures)
            .Select(structure => (Kind: structure.Kind, structure.SchemaVersion, structure.SupportsScopedVariables))
            .OrderBy(entry => entry.Kind, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(fromFragments, served);
    }

    [Fact]
    public async Task Runtime_registered_activities_are_additive_never_a_reprojection()
    {
        var (definitions, versions) = BuildCatalogRows(FeatureAssemblies, out var featureIdByTypeKey);

        // A store-fed (design-authored) activity exists only in this server's store â€” no fragment anywhere.
        var identity = new GuidIdentityGenerator();
        var definitionFactory = new ActivityDefinitionFactory(identity);
        var versionFactory = new ActivityDefinitionVersionFactory(identity, new DefaultActivityDefinitionHasher());
        var dynamicModel = definitionFactory.Create("Acme.StoreFed", "Custom", "Store fed", null, "def-dynamic");
        var dynamicDefinition = ActivityDefinition.From(dynamicModel);
        var dynamicVersion = ActivityDefinitionVersion.From(versionFactory.Create(
            dynamicModel, "1.0.0", "elsa.graph-activity", "1", "elsa.graph-activity", "1",
            JsonSerializer.SerializeToElement(new { }), "Design", "authoring", [], [], [], ActivityExecutionType.Action, "ver-dynamic"));

        var catalog = await ServeCatalog(
            [.. definitions, dynamicDefinition],
            [.. versions, dynamicVersion],
            featureIdByTypeKey);

        var fragmentTypeKeys = FeatureAssemblies
            .Select(ProjectFragment)
            .SelectMany(fragment => fragment.Activities)
            .Select(activity => activity.ActivityTypeKey)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(catalog.Activities, item => item.ActivityTypeKey == "Acme.StoreFed");
        // Union semantics: the dynamic item does not displace or duplicate any fragment-described item.
        Assert.Equal(fragmentTypeKeys.Count, catalog.Activities.Count(item => fragmentTypeKeys.Contains(item.ActivityTypeKey)));
    }

    private static string ContentHashOf(IReadOnlyList<ActivityDefinitionVersion> versions, ActivityAuthoringDescriptorView item) =>
        versions.Single(version => version.Id == item.ActivityVersionId).Hash
        ?? throw new InvalidOperationException($"Version row '{item.ActivityVersionId}' has no hash.");

    /// <summary>
    /// Maps a served catalog item to the fragment contract shape. Deliberately an independent mapping:
    /// if either the endpoint projection or the fragment projection changes a field, the equality below
    /// breaks â€” which is exactly the drift signal this test exists to give.
    /// </summary>
    /// <summary>
    /// Drops <c>[ConsumerNote]</c> content before comparing, because notes are deliberately fragment-only:
    /// they are excluded from the activity content hash so a documentation edit cannot force a new activity
    /// version, which means the persisted rows the catalog endpoint serves from never carry them.
    /// </summary>
    /// <remarks>
    /// This is the ONE field the two projections may legitimately differ on, and narrowing the exclusion to
    /// exactly it is the point: every other difference still breaks the equality above. If notes ever become
    /// server-served, delete this and the test tightens by itself.
    /// </remarks>
    private static ActivityContract WithoutNotes(ActivityContract activity) => activity with
    {
        Notes = null,
        Inputs = activity.Inputs.Select(input => input with { Notes = null }).ToArray()
    };

    private static ActivityContract ToComparableContract(ActivityAuthoringDescriptorView item, string? featureId, string contentHash) => new(
        featureId,
        item.ActivityTypeKey,
        // The server legitimately carries SemVer build metadata (SourceLink sha); fragments strip it
        // because identity is build-metadata-insensitive (§E2.8) and committed artifacts must be
        // commit-stable. Normalize the served version the same way.
        FragmentProjector.StripBuildMetadata(item.Version),
        contentHash,
        item.DisplayName,
        string.IsNullOrEmpty(item.Category) ? null : item.Category,
        item.Description,
        item.ExecutionType,
        item.Inputs.Select(input => new InputContract(
            input.ReferenceKey, input.Name, input.Type, input.CollectionKind.ToString(), input.DisplayName,
            input.Description, input.Order, input.Category, input.IsBrowsable, input.IsRequired, input.IsNullable,
            input.UiHint, input.DefaultValue, input.HasStaticDefault, input.DefaultSyntax, input.UiSpecifications,
            input.EnumValues)).ToArray(),
        item.Outputs.Select(output => new OutputContract(
            output.ReferenceKey ?? output.Name, output.Name, output.Type, output.CollectionKind.ToString(),
            output.DisplayName, output.Description, output.Category, output.IsBrowsable, output.IsRequired)).ToArray(),
        item.Ports.Select(port => new PortContract(port.Name, port.DisplayName, port.Type, port.IsBrowsable, port.ReferenceKey ?? port.Name)).ToArray(),
        item.ContainerStructure);

    private sealed class StubDefinitionStore(IReadOnlyList<ActivityDefinition> definitions) : IActivityDefinitionStore
    {
        public Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActivityDefinition?> FindAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinition>> ListAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => Task.FromResult(definitions);
        public Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubVersionStore(IReadOnlyList<ActivityDefinitionVersion> versions) : IActivityDefinitionVersionStore
    {
        public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(versions);
    }

    private sealed class AllAddableEvaluator : IActivityAvailabilityEvaluator
    {
        public IReadOnlyCollection<IActivityDefinition> FilterAddable(IEnumerable<IActivityDefinition> activities, ActivityAvailabilitySettings? managementSettings = null) =>
            activities.ToArray();
    }

    private sealed class NullSettingsStore : IActivityAvailabilitySettingsStore
    {
        public Task<ActivityAvailabilitySettings?> LoadAsync(string scope, CancellationToken cancellationToken = default) =>
            Task.FromResult<ActivityAvailabilitySettings?>(null);

        public Task SaveAsync(ActivityAvailabilitySettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class MapAttributionResolver(IReadOnlyDictionary<string, string> featureIdByTypeKey) : IActivityFeatureAttributionResolver
    {
        public ValueTask<string?> ResolveProvidingFeatureAsync(string activityTypeKey, CancellationToken cancellationToken) =>
            ValueTask.FromResult(featureIdByTypeKey.TryGetValue(activityTypeKey, out var featureId) ? featureId : null);
    }
}


