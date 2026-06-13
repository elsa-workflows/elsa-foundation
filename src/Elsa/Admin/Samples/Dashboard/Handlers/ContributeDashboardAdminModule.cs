using Elsa.Admin.Core.Events;
using Elsa.Admin.Core.Models;
using Elsa.Events.Core.Contracts;

namespace Elsa.Admin.Samples.Dashboard.Handlers;

public sealed class ContributeDashboardAdminModule : IEventHandler<OnAdminModuleManifestsCollecting>
{
    public Task Handle(OnAdminModuleManifestsCollecting @event, CancellationToken cancellationToken)
    {
        @event.Manifests.Add(new AdminModuleManifest(
            "Elsa.Admin.Samples.Dashboard",
            "Dashboard Sample",
            "1.0.0",
            "/_content/Elsa.Admin.Samples.Dashboard/admin/modules/dashboard/module.js",
            ["/_content/Elsa.Admin.Samples.Dashboard/admin/modules/dashboard/module.css"],
            "^1.0.0",
            "^1.0.0",
            ["dashboard"]));

        return Task.CompletedTask;
    }
}
