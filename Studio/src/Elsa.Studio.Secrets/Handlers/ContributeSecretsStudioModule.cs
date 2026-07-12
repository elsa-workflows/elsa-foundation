using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Models;

namespace Elsa.Studio.Secrets.Handlers;

public sealed class ContributeSecretsStudioModule : IStudioEventHandler<OnStudioModuleManifestsCollecting>
{
    private const string ModuleVersion = "1.0.0";

    public Task Handle(OnStudioModuleManifestsCollecting @event, CancellationToken cancellationToken)
    {
        @event.Manifests.Add(new StudioModuleManifest(
            "Elsa.Studio.Secrets",
            "Secrets",
            ModuleVersion,
            $"/_content/Elsa.Studio.Secrets/studio/modules/secrets/module.js?v={ModuleVersion}",
            [$"/_content/Elsa.Studio.Secrets/studio/modules/secrets/module.css?v={ModuleVersion}"],
            "^1.0.0",
            "^1.0.0",
            ["navigation", "routes", "http", "expression-editors", "secrets"],
            "SecretsStudio"));

        return Task.CompletedTask;
    }
}
