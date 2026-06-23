using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Models;

namespace Elsa.Studio.Diagnostics.OpenTelemetry.Handlers;

public sealed class ContributeDiagnosticsOpenTelemetryStudioModule : IStudioEventHandler<OnStudioModuleManifestsCollecting>
{
    private const string ModuleVersion = "1.0.0";

    public Task Handle(OnStudioModuleManifestsCollecting @event, CancellationToken cancellationToken)
    {
        @event.Manifests.Add(new StudioModuleManifest(
            "Elsa.Studio.Diagnostics.OpenTelemetry",
            "OpenTelemetry",
            ModuleVersion,
            $"/_content/Elsa.Studio.Diagnostics.OpenTelemetry/studio/modules/open-telemetry/module.js?v={ModuleVersion}",
            [$"/_content/Elsa.Studio.Diagnostics.OpenTelemetry/studio/modules/open-telemetry/module.css?v={ModuleVersion}"],
            "^1.0.0",
            "^1.0.0",
            ["navigation", "routes", "http", "diagnostics", "otel", "traces", "metrics", "logs"],
            "OpenTelemetryStudio"));

        return Task.CompletedTask;
    }
}
