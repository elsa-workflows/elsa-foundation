using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Models;

namespace Elsa.Studio.Workflows.Handlers;

public sealed class ContributeWorkflowsStudioModule : IStudioEventHandler<OnStudioModuleManifestsCollecting>
{
    private const string ModuleVersion = "1.0.5";

    public Task Handle(OnStudioModuleManifestsCollecting @event, CancellationToken cancellationToken)
    {
        @event.Manifests.Add(new StudioModuleManifest(
            "Elsa.Studio.Workflows",
            "Workflows",
            ModuleVersion,
            $"/_content/Elsa.Studio.Workflows/studio/modules/workflows/module.js?v={ModuleVersion}",
            [$"/_content/Elsa.Studio.Workflows/studio/modules/workflows/module.css?v={ModuleVersion}"],
            "^1.0.0",
            "^1.0.0",
            ["navigation", "routes", "http", "workflow-designer"]));

        return Task.CompletedTask;
    }
}
