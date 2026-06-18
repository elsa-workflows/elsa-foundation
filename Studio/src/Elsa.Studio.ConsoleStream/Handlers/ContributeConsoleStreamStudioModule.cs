using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Models;

namespace Elsa.Studio.ConsoleStream.Handlers;

public sealed class ContributeConsoleStreamStudioModule : IStudioEventHandler<OnStudioModuleManifestsCollecting>
{
    public Task Handle(OnStudioModuleManifestsCollecting @event, CancellationToken cancellationToken)
    {
        @event.Manifests.Add(new StudioModuleManifest(
            "Elsa.Studio.ConsoleStream",
            "Console stream",
            "1.0.3",
            "/_content/Elsa.Studio.ConsoleStream/studio/modules/console-stream/module.js",
            ["/_content/Elsa.Studio.ConsoleStream/studio/modules/console-stream/module.css"],
            "^1.0.0",
            "^1.0.0",
            ["panels", "console-stream", "signalr"],
            "ConsoleStream"));

        return Task.CompletedTask;
    }
}
