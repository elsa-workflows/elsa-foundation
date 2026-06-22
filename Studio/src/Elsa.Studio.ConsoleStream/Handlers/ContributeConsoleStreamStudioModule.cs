using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Models;

namespace Elsa.Studio.ConsoleStream.Handlers;

public sealed class ContributeConsoleStreamStudioModule : IStudioEventHandler<OnStudioModuleManifestsCollecting>
{
    private const string ModuleVersion = "1.0.5";

    public Task Handle(OnStudioModuleManifestsCollecting @event, CancellationToken cancellationToken)
    {
        @event.Manifests.Add(new StudioModuleManifest(
            "Elsa.Studio.ConsoleStream",
            "Console stream",
            ModuleVersion,
            $"/_content/Elsa.Studio.ConsoleStream/studio/modules/console-stream/module.js?v={ModuleVersion}",
            [$"/_content/Elsa.Studio.ConsoleStream/studio/modules/console-stream/module.css?v={ModuleVersion}"],
            "^1.0.0",
            "^1.0.0",
            ["navigation", "routes", "panels", "console-stream", "signalr", "diagnostics"],
            "ConsoleStream"));

        return Task.CompletedTask;
    }
}
