using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa3.Activities.Design.Import;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
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

        Assert.Contains(services, x => x.ServiceType == typeof(IReusableActivityCollectionAnalyzer) && x.ImplementationType == typeof(ReusableActivityCollectionAnalyzer));
        Assert.Contains(services, x => x.ServiceType == typeof(IReusableActivityCollectionImporter) && x.ImplementationType == typeof(ReusableActivityCollectionImporter));
        Assert.Contains(services, x => x.ServiceType == typeof(IReusableActivityImportMaterializer) && x.ImplementationType == typeof(Elsa3ReusableActivityImportMaterializer));
    }

    [Fact]
    public void Import_feature_builds_and_resolves_every_owned_service()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // The feature now registers the API Explorer refresh, whose change provider depends on the
        // web host's composite EndpointDataSource; routing supplies it.
        services.AddRouting();
        services.AddScoped<IReusableActivityImportMaterializer, StubMaterializer>();
        services.AddScoped<IReusableActivityImportCommand, StubCommand>();
        services.AddScoped<IReusableActivityImportOperationStore, StubOperationStore>();

        new Elsa3ImportActivitiesFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();
        Assert.IsType<ReusableActivityCollectionAnalyzer>(scope.ServiceProvider.GetRequiredService<IReusableActivityCollectionAnalyzer>());
        Assert.IsType<ReusableActivityCollectionImporter>(scope.ServiceProvider.GetRequiredService<IReusableActivityCollectionImporter>());
        Assert.IsType<StubOperationStore>(scope.ServiceProvider.GetRequiredService<IReusableActivityImportOperationStore>());
        Assert.IsType<ReusableActivityImportOperationService>(scope.ServiceProvider.GetRequiredService<IReusableActivityImportOperationService>());
    }

    [Fact]
    public void Generic_import_feature_does_not_select_a_concrete_persistence_provider()
    {
        var featureAttribute = typeof(Elsa3ImportActivitiesFeature)
            .GetCustomAttributes(inherit: false)
            .Single(x => x.GetType().Name == "ShellFeatureAttribute");
        var dependencies = (IEnumerable<object>?)featureAttribute.GetType()
            .GetProperty("DependsOn")
            ?.GetValue(featureAttribute) ?? [];

        Assert.DoesNotContain(dependencies, x =>
            StringComparer.Ordinal.Equals(x?.ToString(), "Elsa3ImportActivitiesEntityFrameworkCore"));
    }

    [Fact]
    public async Task Single_definition_importer_routes_reusable_sources_to_collection_import()
    {
        var mapper = new Elsa3WorkflowDefinitionToWorkflowDefinitionVersion(null!, null!, null!);
        var importer = new Elsa3WorkflowDefinitionImporter(mapper, null!, null!);
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

    private sealed class StubMaterializer : IReusableActivityImportMaterializer
    {
        public ValueTask<ReusableActivityImportMutation> MaterializeAsync(
            ReusableActivityImportCollection collection,
            ReusableActivityImportPlan plan,
            IReadOnlyList<ReusableActivityImportItem> selection,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ReusableActivityImportMutation(plan.PlanId, plan.CollectionId, [], [], []));
    }

    private sealed class StubCommand : IReusableActivityImportCommand
    {
        public ValueTask<ReusableActivityImportCommitResult> CommitAsync(
            ReusableActivityImportMutation mutation,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ReusableActivityImportCommitResult(false));
    }

    private sealed class StubOperationStore : IReusableActivityImportOperationStore
    {
        public ValueTask<bool> TryCreateCollectionAsync(ReusableActivityImportCollectionHandle collection, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<ReusableActivityImportCollectionHandle?> FindCollectionAsync(string handle, ReusableActivityImportAccessScope accessScope, CancellationToken cancellationToken = default) => ValueTask.FromResult<ReusableActivityImportCollectionHandle?>(null);
        public ValueTask<ReusableActivityImportReceipt?> FindReceiptAsync(string idempotencyKey, ReusableActivityImportAccessScope accessScope, CancellationToken cancellationToken = default) => ValueTask.FromResult<ReusableActivityImportReceipt?>(null);
    }

}
