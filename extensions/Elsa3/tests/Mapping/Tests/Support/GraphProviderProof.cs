using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Graph.Design.Services;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Sequence;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Services;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Services;
using Elsa.Workflows.Publishing.Core.Models;
using Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa3.Mapping.Tests.Support;

/// <summary>
/// Proves a converted reusable workflow against the real graph provider (spec 236 R-10): the schema-2 manifest the
/// import writes, with the contract the conversion derives, validated by <see cref="GraphActivityProvider"/> with the
/// native structure handlers registered and Complete nodes detected through the real catalog.
/// </summary>
/// <remarks>
/// <see cref="CompileAsync"/> also compiles it (spec 246 FR-015), with each dependency resolved as the template compiler
/// resolves a published one (<see cref="CatalogResolvedDependencies"/>), so a converted workflow that validates also
/// compiles to an executable graph boundary.
/// </remarks>
internal static class GraphProviderProof
{
    public static async Task<IReadOnlyList<ActivityDiagnostic>> ValidateAsync(RealActivityCatalog catalog, Elsa3ConvertedDefinition converted)
    {
        await using var provider = Services(catalog);
        return await GraphProvider(provider).ValidateAsync(Manifest(converted), Elsa3ImportedActivityContract.Build(converted));
    }

    /// <summary>
    /// Compiles the converted workflow as the publisher would after preflight. Returns the compilation; a successful one
    /// has an executable root and no error diagnostic.
    /// </summary>
    public static async Task<ActivityTemplateCompilation> CompileAsync(
        RealActivityCatalog catalog,
        Elsa3ConvertedDefinition converted,
        IReadOnlyDictionary<string, ImportedSibling>? siblings = null)
    {
        await using var provider = Services(catalog);
        var graphProvider = GraphProvider(provider);
        var manifest = Manifest(converted);
        var discovery = await graphProvider.DiscoverDependenciesAsync(new("imported-definition", "imported-draft", 1, manifest));
        Assert.DoesNotContain(discovery.Diagnostics, diagnostic => diagnostic.Severity == ActivityDiagnosticSeverity.Error);
        var dependencies = await CatalogResolvedDependencies.ResolveAsync(catalog, discovery.Dependencies, siblings);
        return await graphProvider.CompileAsync(new(
            "imported-definition",
            "elsa3.imported",
            "imported-draft",
            1,
            "1.0.0",
            Elsa3ImportedActivityContract.Build(converted),
            manifest,
            dependencies,
            "test-provider-fingerprint"));
    }

    /// <summary>Asserts the compilation produced an executable graph boundary without errors.</summary>
    public static void AssertCompiled(ActivityTemplateCompilation compilation)
    {
        Assert.DoesNotContain(compilation.Diagnostics, diagnostic => diagnostic.Severity == ActivityDiagnosticSeverity.Error);
        Assert.NotNull(compilation.ExecutableRoot);
    }

    private static ActivityProviderManifest Manifest(Elsa3ConvertedDefinition converted) => new(
        GraphActivityProvider.Key,
        "2",
        Elsa3ReusableActivityImportMaterializer.BuildGraphManifest(converted));

    private static GraphActivityProvider GraphProvider(IServiceProvider provider) => new(
        new DefaultActivityStructureService(provider.GetServices<IActivityStructureHandler>()),
        new ActivityContractAuthoringValidator(new ConvertedTypeCapabilities()),
        provider.GetRequiredService<IServiceScopeFactory>());

    private static ServiceProvider Services(RealActivityCatalog catalog)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IActivityDefinitionLookup>(catalog);
        new ActivitiesSequenceFeature().ConfigureServices(services);
        new ActivitiesControlFlowFeature().ConfigureServices(services);
        new ActivitiesFlowchartFeature().ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    /// <summary>The type aliases converted Elsa 3 definitions use, all storable as JSON.</summary>
    private sealed class ConvertedTypeCapabilities : IActivityContractCapabilityCatalog
    {
        public IReadOnlyCollection<ActivityContractTypeCapability> Types { get; } =
            new[] { "Object", "String", "Int32", "Int64", "Boolean", "Decimal", "Double", "DateTime", "DateTimeOffset", "TimeSpan", "Guid", "JsonElement" }
                .Select(alias => new ActivityContractTypeCapability(
                    alias,
                    alias,
                    "Test",
                    "Test",
                    Enum.GetValues<CollectionKind>().ToHashSet(),
                    true,
                    true,
                    new HashSet<string>(StringComparer.Ordinal) { "elsa.json" }))
                .ToArray();
    }
}
