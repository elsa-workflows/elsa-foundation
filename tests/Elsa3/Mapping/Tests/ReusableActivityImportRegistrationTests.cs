using Elsa3.Activities.Design.Import;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Persistence.Groundwork;
using Elsa3.Activities.Design.Import.Persistence.Groundwork.Services;
using Elsa3.Activities.Design.Import.Services;
using Elsa3.Mapping;
using Elsa3.Mapping.Mappings;
using Elsa3.Mapping.Services;
using Elsa3.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa3.Mapping.Tests;

public sealed class ReusableActivityImportRegistrationTests
{
    [Fact]
    public void Features_register_analysis_mapping_orchestration_and_atomic_adapter()
    {
        var services = new ServiceCollection();

        new Elsa3ImportActivitiesFeature().ConfigureServices(services);
        new Elsa3MappingFeature().ConfigureServices(services);
        new Elsa3ImportActivitiesGroundworkFeature().ConfigureServices(services);

        Assert.Contains(services, x => x.ServiceType == typeof(IReusableActivityCollectionAnalyzer) && x.ImplementationType == typeof(ReusableActivityCollectionAnalyzer));
        Assert.Contains(services, x => x.ServiceType == typeof(IReusableActivityCollectionImporter) && x.ImplementationType == typeof(ReusableActivityCollectionImporter));
        Assert.Contains(services, x => x.ServiceType == typeof(IReusableActivityImportMaterializer) && x.ImplementationType == typeof(Elsa3ReusableActivityImportMaterializer));
        Assert.Contains(services, x => x.ServiceType == typeof(IReusableActivityImportCommand) && x.ImplementationType == typeof(GroundworkReusableActivityImportCommand));
    }

    [Fact]
    public async Task Single_definition_importer_routes_reusable_sources_to_collection_import()
    {
        var mapper = new Elsa3WorkflowDefinitionToWorkflowDefinitionVersion(null!, null!, null!);
        var importer = new Elsa3WorkflowDefinitionImporter(mapper);
        var source = ReusableActivityImportFixtures.Workflow(
            "reusable",
            "reusable-v1",
            1,
            true,
            ReusableActivityImportFixtures.Leaf("root"));

        var result = await importer.ImportAsync(new(
            Elsa3MigrationInputKind.WorkflowDefinitionExportJson,
            source));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.False(result.Succeeded);
        Assert.Equal(ReusableActivityImportDiagnosticCodes.SelectionInvalid, diagnostic.Code);
        Assert.Contains("collection-aware", diagnostic.Message, StringComparison.Ordinal);
    }
}
