using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Commands;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Api.Endpoints.RuntimeDiagnostics;

internal sealed class SaveSettings(ICommandSender commandSender, ILogger<SaveSettings> logger)
    : ElsaCommandHandlerEndpoint<SaveRuntimeDiagnosticsSettings, RuntimeDiagnosticsSettingsView>(commandSender, logger)
{
    public override void Configure()
    {
        Put(RouteConstants.RuntimeDiagnosticsSettings);
        ConfigurePermissions();
    }
}
