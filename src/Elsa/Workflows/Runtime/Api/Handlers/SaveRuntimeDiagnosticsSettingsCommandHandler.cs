using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Commands;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Handlers;

public sealed class SaveRuntimeDiagnosticsSettingsCommandHandler(IRuntimeDiagnosticsSettingsStore settingsStore)
    : ICommandHandler<SaveRuntimeDiagnosticsSettings, RuntimeDiagnosticsSettingsView>
{
    public async Task<RuntimeDiagnosticsSettingsView> Handle(SaveRuntimeDiagnosticsSettings command, CancellationToken cancellationToken)
    {
        var settings = new RuntimeDiagnosticsSettings
        {
            Scope = string.IsNullOrWhiteSpace(command.Scope)
                ? RuntimeDiagnosticsSettings.HostDefaultScope
                : command.Scope,
            DefaultLevel = command.DefaultLevel,
            SubjectOverrides = command.SubjectOverrides ?? new Dictionary<string, RuntimeDiagnosticsEvidenceLevel>(StringComparer.Ordinal),
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var savedSettings = await settingsStore.SaveAsync(settings, cancellationToken);
        return RuntimeDiagnosticsSettingsResolver.Resolve(savedSettings);
    }
}
