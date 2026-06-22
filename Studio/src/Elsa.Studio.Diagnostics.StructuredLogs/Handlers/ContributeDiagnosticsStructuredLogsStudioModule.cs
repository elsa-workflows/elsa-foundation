using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Models;

namespace Elsa.Studio.Diagnostics.StructuredLogs.Handlers;

public sealed class ContributeDiagnosticsStructuredLogsStudioModule : IStudioEventHandler<OnStudioModuleManifestsCollecting>
{
    private const string ModuleVersion = "1.0.4";

    public Task Handle(OnStudioModuleManifestsCollecting @event, CancellationToken cancellationToken)
    {
        @event.Manifests.Add(new StudioModuleManifest(
            "Elsa.Studio.Diagnostics.StructuredLogs",
            "Structured logs",
            ModuleVersion,
            $"/_content/Elsa.Studio.Diagnostics.StructuredLogs/studio/modules/structured-logs/module.js?v={ModuleVersion}",
            [$"/_content/Elsa.Studio.Diagnostics.StructuredLogs/studio/modules/structured-logs/module.css?v={ModuleVersion}"],
            "^1.0.0",
            "^1.0.0",
            ["navigation", "routes", "panels", "http", "sse", "diagnostics"],
            "StructuredLogsStudio"));

        return Task.CompletedTask;
    }
}
