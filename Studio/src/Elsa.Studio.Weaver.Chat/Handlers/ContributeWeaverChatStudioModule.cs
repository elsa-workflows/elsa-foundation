using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Models;
using Elsa.Studio.Weaver.Core;

namespace Elsa.Studio.Weaver.Chat.Handlers;

public sealed class ContributeWeaverChatStudioModule : IStudioEventHandler<OnStudioModuleManifestsCollecting>
{
    private const string ModuleVersion = "1.0.0";

    public Task Handle(OnStudioModuleManifestsCollecting @event, CancellationToken cancellationToken)
    {
        @event.Manifests.Add(new StudioModuleManifest(
            "Elsa.Studio.Weaver.Chat",
            "Weaver chat",
            ModuleVersion,
            $"/_content/Elsa.Studio.Weaver.Chat/studio/modules/weaver-chat/module.js?v={ModuleVersion}",
            [$"/_content/Elsa.Studio.Weaver.Chat/studio/modules/weaver-chat/module.css?v={ModuleVersion}"],
            "^1.0.0",
            "^1.0.0",
            [
                "navigation",
                "routes",
                "panels",
                "http",
                WeaverStudioCapabilities.AiCapabilities,
                WeaverStudioCapabilities.AiSurfaces,
                WeaverStudioCapabilities.WeaverChat
            ],
            "WeaverChatStudio"));

        return Task.CompletedTask;
    }
}
