using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Commands;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Endpoints.Diagnostics.Save;

[Put(RouteConstants.RuntimeDiagnosticsSettings)]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeManage)]
[RuntimeProblems("handling runtime command")]
public sealed class Endpoint(ICommandSender commands) : ApiEndpoint<SaveRuntimeDiagnosticsSettings, RuntimeDiagnosticsSettingsView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "SaveDiagnostics";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentTypeAndPayload;
    }

    public override Task<RuntimeDiagnosticsSettingsView> HandleAsync(SaveRuntimeDiagnosticsSettings command, CancellationToken cancellationToken) =>
        commands.Send(command, cancellationToken);
}
