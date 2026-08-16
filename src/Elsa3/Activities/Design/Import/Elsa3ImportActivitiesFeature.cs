using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Events.Core.Extensions;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Primitives.Exceptions;
using Elsa3.Activities.Design.Import.Authorization;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Endpoints;
using Elsa3.Activities.Design.Import.Services;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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
public class Elsa3ImportActivitiesFeature : IWebShellFeature
{
    /// <summary>
    /// Workflow definition collection sources; from which the activities are extracted
    /// </summary>
    public IEnumerable<string> WorkflowCollectionSourceTypes { get; set; } = [];

    public ReusableActivityImportOptions ImportOptions { get; set; } = new();

    public void ConfigureServices(IServiceCollection services)
    {
        foreach (var source in WorkflowCollectionSourceTypes)
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
        services.AddPermissionContributor<Elsa3ImportPermissionContributor>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        ReusableActivityImportApi.MapReusableActivityImportApi(endpoints);
}
