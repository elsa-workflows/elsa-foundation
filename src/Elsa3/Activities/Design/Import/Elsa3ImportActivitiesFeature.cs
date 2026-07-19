using CShells.Features;
using Elsa.Api.FastEndpoints;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Events.Core.Extensions;
using Elsa.Primitives.Exceptions;
using Elsa3.Activities.Design.Import.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Elsa3.Activities.Design.Import.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa3.Activities.Design.Import;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Elsa3")]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Import")]
[ShellFeature(
    name: "Elsa3ImportJsonActivities",
    DisplayName = "Elsa 3 Import Activities",
    Description = "Imports Elsa 3 JSON workflow activities into the design reconciliation pipeline.",
    DependsOn = new object[] { "Elsa3Mapping" }
)]
public class Elsa3ImportActivitiesFeature : FastEndpointsFeatureBase
{
    /// <summary>
    /// Workflow definition collection sources; from which the activities are extracted
    /// </summary>
    public IEnumerable<string> WorkflowCollectionSourceTypes { get; set; } = [];

    public ReusableActivityImportOptions ImportOptions { get; set; } = new();

    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        foreach(var source in WorkflowCollectionSourceTypes)
        {
            var type = Type.GetType(source)
                ?? throw new FeatureConfigurationException($"JSON source type '{source}' could not be loaded");

            services.AddScoped(typeof(IActivityCollectionJsonSource), type);
        }

        services.AddScoped<IReusableActivityCollectionAnalyzer, ReusableActivityCollectionAnalyzer>();
        services.AddScoped<IReusableActivityCollectionImporter, ReusableActivityCollectionImporter>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<ReusableActivityImportOptions>().Configure(options =>
        {
            options.MaximumUploadBytes = ImportOptions.MaximumUploadBytes;
            options.MaximumSourceVersions = ImportOptions.MaximumSourceVersions;
            options.DefaultPageSize = ImportOptions.DefaultPageSize;
            options.MaximumPageSize = ImportOptions.MaximumPageSize;
            options.CollectionLifetime = ImportOptions.CollectionLifetime;
        });
        services.TryAddScoped<IReusableActivityImportOperationService, ReusableActivityImportOperationService>();

        services.AddEventHandlersFrom(GetType().Assembly);
    }
}
