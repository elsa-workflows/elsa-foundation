using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Handlers;

public sealed class GetRuntimeDiagnosticsSettingsRequestHandler(IRuntimeDiagnosticsSettingsStore settingsStore)
    : IRequestHandler<GetRuntimeDiagnosticsSettings, RuntimeDiagnosticsSettingsView>
{
    public async Task<RuntimeDiagnosticsSettingsView> Handle(GetRuntimeDiagnosticsSettings request, CancellationToken cancellationToken)
    {
        var scope = string.IsNullOrWhiteSpace(request.Scope)
            ? RuntimeDiagnosticsSettings.HostDefaultScope
            : request.Scope;
        var settings = await settingsStore.LoadAsync(scope, cancellationToken) ?? RuntimeDiagnosticsSettings.Default;

        return RuntimeDiagnosticsSettingsResolver.Resolve(settings);
    }
}
