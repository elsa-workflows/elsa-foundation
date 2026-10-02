using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Api.AspNetCore;
using Elsa.Events.Core.Extensions;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Primitives.Exceptions;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa3.Activities.Design.Import.Authorization;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Endpoints;
using Elsa3.Activities.Design.Import.Services;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NativeEndpoints;

namespace Elsa3.Activities.Design.Import;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Elsa3")]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Import")]
[ShellFeature(
    name: "Elsa3ImportJsonActivities",
    DisplayName = "Elsa 3 Import Activities",
    Description = "Imports Elsa 3 JSON workflow activities into the design reconciliation pipeline.",
    // The Tasks feature runs the recurring sweep that deletes expired collection uploads.
    DependsOn = new object[] { "Elsa3Mapping", "Tasks" }
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
        services.AddElsaEndpoints();
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
            options.ExpiredCollectionSweepInterval = ImportOptions.ExpiredCollectionSweepInterval;
            options.ExpiredCollectionSweepBatchSize = ImportOptions.ExpiredCollectionSweepBatchSize;
        });
        services.TryAddScoped<IReusableActivityImportOperationService, ReusableActivityImportOperationService>();
        // An upload nobody applies or reads again is deleted by time. Persistence core is shared host
        // infrastructure: it gives the sweep one operation scope per persistence scope.
        services.AddPersistenceCore();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRecurringTask, ExpiredImportCollectionSweepTask>());

        services.AddEventHandlersFrom(GetType().Assembly);
        services.AddDynamicEndpointApiExplorerRefresh();
        // The owner's failure services are keyed so hosts composing several modules keep each
        // module's own error shapes; the endpoint pipeline falls back to unkeyed registrations.
        services.TryAddKeyedSingleton<IEndpointProblemWriter, ReusableActivityImportProblemWriter>(ReusableActivityImportApi.OwnerId);
        services.TryAddKeyedSingleton<IEndpointFaultRenderer, ReusableActivityImportFaultRenderer>(ReusableActivityImportApi.OwnerId);
        services.AddPermissionContributor<Elsa3ImportPermissionContributor>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        ReusableActivityImportApi.MapReusableActivityImportApi(endpoints);
}
