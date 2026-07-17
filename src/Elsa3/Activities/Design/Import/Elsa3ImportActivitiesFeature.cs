using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Events.Core.Extensions;
using Elsa.Primitives.Exceptions;
using Elsa3.Activities.Design.Import.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Elsa3.Activities.Design.Import.Services;

namespace Elsa3.Activities.Design.Import;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Elsa3")]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Import")]
[ShellFeature(
    name: "Elsa3ImportJsonActivities",
    DisplayName = "Elsa 3 Import Activities",
    Description = "Imports Elsa 3 JSON workflow activities into the design reconciliation pipeline."
)]
public class Elsa3ImportActivitiesFeature : IShellFeature
{
    /// <summary>
    /// Workflow definition collection sources; from which the activities are extracted
    /// </summary>
    public IEnumerable<string> WorkflowCollectionSourceTypes { get; set; } = [];

    public void ConfigureServices(IServiceCollection services)
    {
        foreach(var source in WorkflowCollectionSourceTypes)
        {
            var type = Type.GetType(source)
                ?? throw new FeatureConfigurationException($"JSON source type '{source}' could not be loaded");

            services.AddScoped(typeof(IActivityCollectionJsonSource), type);
        }

        services.AddScoped<IReusableActivityCollectionAnalyzer, ReusableActivityCollectionAnalyzer>();
        services.AddScoped<IReusableActivityCollectionImporter, ReusableActivityCollectionImporter>();

        services.AddEventHandlersFrom(GetType().Assembly);
    }
}
