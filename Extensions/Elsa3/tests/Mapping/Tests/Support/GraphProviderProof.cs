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
using Microsoft.Extensions.DependencyInjection;
using DesignActivityContract = Elsa.Activities.Design.Core.Models.ActivityContract;

namespace Elsa3.Mapping.Tests.Support;

/// <summary>
/// Proves a converted reusable workflow against the real graph provider (spec 236 R-10): the schema-2 manifest the
/// import writes, with the contract the conversion derives, validated by <see cref="GraphActivityProvider"/> with the
/// native structure handlers registered and Complete nodes detected through the real catalog.
/// </summary>
/// <remarks>
/// Compilation is not run: <c>CompileAsync</c> needs every dependency published (publication and template), which is
/// what #2796 adds for imported activities. Validation applies the same graph, mapping and Complete rules.
/// </remarks>
internal static class GraphProviderProof
{
    public static async Task<IReadOnlyList<ActivityDiagnostic>> ValidateAsync(RealActivityCatalog catalog, Elsa3ConvertedDefinition converted)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IActivityDefinitionLookup>(catalog);
        new ActivitiesSequenceFeature().ConfigureServices(services);
        new ActivitiesControlFlowFeature().ConfigureServices(services);
        new ActivitiesFlowchartFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();

        var graphProvider = new GraphActivityProvider(
            new DefaultActivityStructureService(provider.GetServices<IActivityStructureHandler>()),
            new ActivityContractAuthoringValidator(new ConvertedTypeCapabilities()),
            provider.GetRequiredService<IServiceScopeFactory>());
        var manifest = new ActivityProviderManifest(
            GraphActivityProvider.Key,
            "2",
            Elsa3ReusableActivityImportMaterializer.BuildGraphManifest(converted));
        return await graphProvider.ValidateAsync(manifest, Contract(converted));
    }

    /// <summary>The activity contract a published import would carry: the workflow's inputs and outputs and the converted outcomes.</summary>
    public static DesignActivityContract Contract(Elsa3ConvertedDefinition converted) => new(
        "1",
        converted.State.Inputs
            .Select(input => new ActivityInputContract(input.ReferenceKey, input.Name, input.Type, false, true, null, input.StorageDriverType ?? "elsa.json"))
            .ToArray(),
        converted.State.Outputs
            .Select(output => new ActivityOutputContract(output.ReferenceKey, output.Name, output.Type, false, true, output.StorageDriverType ?? "elsa.json"))
            .ToArray(),
        converted.BoundaryOutcomes.ToArray());

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
