using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Models;

namespace Elsa.Studio.Dashboard.Handlers;

public sealed class ContributeDashboardStudioModule : IStudioEventHandler<OnStudioModuleManifestsCollecting>
{
    private const string ModuleVersion = "1.0.0";
    public Task Handle(OnStudioModuleManifestsCollecting @event, CancellationToken cancellationToken)
    {
        @event.Manifests.Add(new StudioModuleManifest(
            "Elsa.Studio.Dashboard", "Dashboard", ModuleVersion,
            $"/_content/Elsa.Studio.Dashboard/studio/modules/dashboard/module.js?v={ModuleVersion}",
            [$"/_content/Elsa.Studio.Dashboard/studio/modules/dashboard/module.css?v={ModuleVersion}"],
            "^1.0.0", "^1.0.0", ["navigation", "routes", "dashboard-widgets"]));
        return Task.CompletedTask;
    }
}
