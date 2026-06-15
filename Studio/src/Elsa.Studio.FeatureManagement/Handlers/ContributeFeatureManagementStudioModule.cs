using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Models;

namespace Elsa.Studio.FeatureManagement.Handlers;

public sealed class ContributeFeatureManagementStudioModule : IStudioEventHandler<OnStudioModuleManifestsCollecting>
{
    private const string ModuleVersion = "1.0.0";

    public Task Handle(OnStudioModuleManifestsCollecting @event, CancellationToken cancellationToken)
    {
        @event.Manifests.Add(new StudioModuleManifest(
            "Elsa.Studio.FeatureManagement",
            "Feature management",
            ModuleVersion,
            "/_content/Elsa.Studio.FeatureManagement/studio/modules/features/module.js",
            [$"/_content/Elsa.Studio.FeatureManagement/studio/modules/features/module.css?v={ModuleVersion}"],
            "^1.0.0",
            "^1.0.0",
            ["navigation", "routes", "http", "setting-editors"]));

        return Task.CompletedTask;
    }
}
