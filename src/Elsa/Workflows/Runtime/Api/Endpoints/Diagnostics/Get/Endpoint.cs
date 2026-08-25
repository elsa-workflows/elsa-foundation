using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Endpoints.Diagnostics.Get;

[Get(RouteConstants.RuntimeDiagnosticsSettings)]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[RuntimeProblems("handling runtime request", NotFoundArms = true)]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<GetRuntimeDiagnosticsSettings, RuntimeDiagnosticsSettingsView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetDiagnostics";

    public override Task<RuntimeDiagnosticsSettingsView> HandleAsync(GetRuntimeDiagnosticsSettings request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
