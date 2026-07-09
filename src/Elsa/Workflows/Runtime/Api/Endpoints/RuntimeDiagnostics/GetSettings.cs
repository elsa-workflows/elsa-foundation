using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Api.Endpoints.RuntimeDiagnostics;

internal sealed class GetSettings(IRequestSender requestSender, ILogger<GetSettings> logger)
    : ElsaRequestHandlerEndpoint<GetRuntimeDiagnosticsSettings, RuntimeDiagnosticsSettingsView>(requestSender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.RuntimeDiagnosticsSettings);
        ConfigurePermissions();
    }
}
