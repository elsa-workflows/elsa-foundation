using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Models;
using Elsa.Studio.Weaver.Core;

namespace Elsa.Studio.Weaver.Workflows.Handlers;

public sealed class ContributeWeaverWorkflowsStudioModule : IStudioEventHandler<OnStudioModuleManifestsCollecting>
{
    private const string ModuleVersion = "1.0.0";

    public Task Handle(OnStudioModuleManifestsCollecting @event, CancellationToken cancellationToken)
    {
        @event.Manifests.Add(new StudioModuleManifest(
            "Elsa.Studio.Weaver.Workflows",
            "Weaver workflows",
            ModuleVersion,
            $"/_content/Elsa.Studio.Weaver.Workflows/studio/modules/weaver-workflows/module.js?v={ModuleVersion}",
            [],
            "^1.0.0",
            "^1.0.0",
            [
                WeaverStudioCapabilities.AiContextProviders,
                WeaverStudioCapabilities.AiPromptActions,
                WeaverStudioCapabilities.AiProposalRenderers,
                WeaverStudioCapabilities.AiTools,
                WeaverStudioCapabilities.WeaverWorkflows
            ],
            "WeaverWorkflowsStudio"));

        return Task.CompletedTask;
    }
}
