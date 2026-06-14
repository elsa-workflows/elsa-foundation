using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Models;

namespace Elsa.Studio.Samples.Dashboard.Handlers;

public sealed class ContributeDashboardStudioModule : IStudioEventHandler<OnStudioModuleManifestsCollecting>
{
    public Task Handle(OnStudioModuleManifestsCollecting @event, CancellationToken cancellationToken)
    {
        @event.Manifests.Add(new StudioModuleManifest(
            "Elsa.Studio.Samples.Dashboard",
            "Dashboard sample",
            "1.0.0",
            "/_content/Elsa.Studio.Samples.Dashboard/studio/modules/dashboard/module.js",
            ["/_content/Elsa.Studio.Samples.Dashboard/studio/modules/dashboard/module.css"],
            "^1.0.0",
            "^1.0.0",
            ["navigation", "routes", "dashboard-widgets"]));

        return Task.CompletedTask;
    }
}

